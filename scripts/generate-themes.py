#!/usr/bin/env python3
"""Generator słowników motywów SentinelX (Dark / Deep Dark / Light-System).

Dlaczego skrypt, a nie ręczna edycja: tokeny kolorów żyją w Themes/Colors.xaml
i jest ich ~50. Słowniki motywów muszą nadpisywać WYŁĄCZNIE klucze *Color —
pominięty klucz dziedziczy ciemną wartość i psuje jasny motyw (K6 w
docs/GUI-MASTER-PROMPT-MEGA.md). Skrypt czyta listę kluczy z Colors.xaml,
dokleja wartości z tabeli poniżej i raportuje:
  • brakujące tokeny (klucz w Colors.xaml bez wartości motywu),
  • martwe wartości (token motywu bez klucza w Colors.xaml),
  • kontrast WCAG tekstu i stanów wobec powierzchni w każdym motywie.

Uruchomienie:  python3 scripts/generate-themes.py            (zapisuje pliki)
               python3 scripts/generate-themes.py --check    (tylko weryfikacja)
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
XAML_NS = "{http://schemas.microsoft.com/winfx/2006/xaml}"
HEADER = ('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" '
          'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
          '    <!-- Wygenerowane przez scripts/generate-themes.py — nadpisują wyłącznie tokeny *Color.\n'
          '         Zmiana kolorów: tabela w tym skrypcie, nie ten plik. -->\n')

# Wartości domyślne (= motyw Dark) muszą być IDENTYCZNE jak w Themes/Colors.xaml.
DARK = {
    "SxBackgroundColor": "#262624",
    "SxSurfaceColor": "#30302E",
    "SxSurfaceRaisedColor": "#3A3936",
    "SxSurfaceHoverColor": "#3D3C38",
    "SxSurfaceActiveColor": "#45443F",
    "SxSurfaceSunkenColor": "#1F1E1D",
    "SxSurfaceGlassColor": "#B330302E",
    "SxSidebarColor": "#1F1E1D",
    "SxSidebarHoverColor": "#2B2A27",
    "SxSidebarActiveColor": "#34332F",
    "SxBorderColor": "#4A4842",
    "SxBorderSubtleColor": "#383630",
    "SxBorderStrongColor": "#5C5A52",
    "SxTextPrimaryColor": "#F5F4EE",
    "SxTextSecondaryColor": "#C2BFB4",
    "SxTextMutedColor": "#A8A496",
    "SxTextDisabledColor": "#6E6B60",
    "SxTextOnAccentColor": "#FFFFFF",
    "SxAccentCyanColor": "#DE8668",
    "SxAccentCyanDimColor": "#C15F3C",
    "SxAccentCyanSoftColor": "#2A1B14",
    "SxAccentVioletColor": "#C9A6E8",
    "SxAccentVioletDimColor": "#8B5FBF",
    "SxAccentVioletSoftColor": "#241B2E",
    "SxAccentBlueColor": "#9DB8DE",
    "SxAccentBlueDimColor": "#5E82B5",
    "SxAccentBlueSoftColor": "#1D2634",
    "SxAccentTealColor": "#7FB5A8",
    "SxAccentTealDimColor": "#4E8578",
    "SxAccentTealSoftColor": "#1B2C27",
    "SxAccentPinkColor": "#E39B9B",
    "SxAccentPinkDimColor": "#B56A6A",
    "SxAccentPinkSoftColor": "#32201F",
    "SxAccentLimeColor": "#B5C98A",
    "SxAccentLimeDimColor": "#7E9455",
    "SxRoyalGoldColor": "#E3C88A",
    "SxRoyalGoldDimColor": "#9C7F3E",
    "SxRoyalGoldSoftColor": "#332B18",
    "SxAccentGradStartColor": "#B0573A",
    "SxAccentGradEndColor": "#6D4A8F",
    "SxSuccessColor": "#7FC896",
    "SxSuccessDimColor": "#2E5C3E",
    "SxSuccessGradEndColor": "#356B48",
    "SxSuccessBgColor": "#1C2E22",
    "SxWarningColor": "#E5B362",
    "SxWarningDimColor": "#8A6428",
    "SxWarningBgColor": "#2E2515",
    "SxErrorColor": "#E8847A",
    "SxErrorDimColor": "#6B2B26",
    "SxErrorGradEndColor": "#C9615A",
    "SxErrorBgColor": "#2E1A18",
    "SxInfoColor": "#8FBAD6",
    "SxInfoBgColor": "#1C2730",
    "SxVoiceActiveColor": "#DE8668",
    "SxVoiceStandbyColor": "#E5B362",
    "SxVoiceOffColor": "#A8A496",
    "SxFocusRingColor": "#EEA171",
    "SxShadowColor": "#66000000",
    "SxShimmerColor": "#14FFFFFF",
    "SxRevealColor": "#2EDE8668",
    "SxWhiteColor": "#FFFFFF",
    "SxTransparentColor": "#00000000",
    "SxUserBubbleColor": "#3A2A22",
    "SxUserBubbleBorderColor": "#8A5A44",
    "SxAssistantBubbleColor": "#34332F",
    "SxAssistantBubbleBorderColor": "#4A4842",
    "SxAuroraAColor": "#C15F3C",
    "SxAuroraBColor": "#8B5FBF",
    "SxAuroraCColor": "#D9A05B",
    "SxOverlayBgColor": "#CC262624",
    "SxScrimColor": "#B31F1E1B",
}

# Deep Dark: ta sama paleta akcentów, ciemniejsze powierzchnie (mniej poświaty, więcej kontrastu krawędzi).
DEEP_DARK = dict(DARK, **{
    "SxBackgroundColor": "#1A1918",
    "SxSurfaceColor": "#222120",
    "SxSurfaceRaisedColor": "#292826",
    "SxSurfaceHoverColor": "#302F2C",
    "SxSurfaceActiveColor": "#3A3834",
    "SxSurfaceSunkenColor": "#141312",
    "SxSurfaceGlassColor": "#B3222120",
    "SxSidebarColor": "#171615",
    "SxSidebarHoverColor": "#232221",
    "SxSidebarActiveColor": "#2C2B28",
    "SxBorderColor": "#3E3C36",
    "SxBorderSubtleColor": "#2E2C27",
    "SxBorderStrongColor": "#4E4C44",
    "SxSuccessBgColor": "#14231A",
    "SxWarningBgColor": "#241D10",
    "SxRoyalGoldColor": "#EFD79A",
    "SxRoyalGoldDimColor": "#8A6D2E",
    "SxRoyalGoldSoftColor": "#292212",
    "SxErrorBgColor": "#241413",
    "SxInfoBgColor": "#151E26",
    "SxUserBubbleColor": "#32241C",
    "SxAssistantBubbleColor": "#262523",
    "SxAssistantBubbleBorderColor": "#3E3C36",
    "SxOverlayBgColor": "#CC1A1918",
    "SxScrimColor": "#C2131210",
})

# Light / System: jasne powierzchnie, ciemniejsze akcenty (kontrast tekstu ≥ 4,5:1).
LIGHT = {
    "SxBackgroundColor": "#FAF9F5",
    "SxSurfaceColor": "#FFFFFF",
    "SxSurfaceRaisedColor": "#FFFFFF",
    "SxSurfaceHoverColor": "#F0EEE6",
    "SxSurfaceActiveColor": "#E8E4D8",
    "SxSurfaceSunkenColor": "#F5F4EE",
    "SxSurfaceGlassColor": "#CCFFFFFF",
    "SxSidebarColor": "#F0EEE6",
    "SxSidebarHoverColor": "#E8E4D8",
    "SxSidebarActiveColor": "#DFDACC",
    "SxBorderColor": "#D9D4C5",
    "SxBorderSubtleColor": "#E8E4D8",
    "SxBorderStrongColor": "#C2BCA8",
    "SxTextPrimaryColor": "#1F1E1D",
    "SxTextSecondaryColor": "#55524A",
    "SxTextMutedColor": "#6E6A5E",
    "SxTextDisabledColor": "#A8A399",
    "SxTextOnAccentColor": "#FFFFFF",
    "SxAccentCyanColor": "#B0522F",
    "SxAccentCyanDimColor": "#9A4628",
    "SxAccentCyanSoftColor": "#FAF0EA",
    "SxAccentVioletColor": "#7742A8",
    "SxAccentVioletDimColor": "#663994",
    "SxAccentVioletSoftColor": "#EFE8F7",
    "SxAccentBlueColor": "#3E6BB4",
    "SxAccentBlueDimColor": "#33578F",
    "SxAccentBlueSoftColor": "#E7EEF8",
    "SxAccentTealColor": "#2E6B5E",
    "SxAccentTealDimColor": "#24564B",
    "SxAccentTealSoftColor": "#E4F0EC",
    "SxAccentPinkColor": "#A8455E",
    "SxAccentPinkDimColor": "#8A3A4E",
    "SxAccentPinkSoftColor": "#F8E7EA",
    "SxAccentLimeColor": "#5E7226",
    "SxAccentLimeDimColor": "#4C5C1E",
    "SxRoyalGoldColor": "#7A5B10",
    "SxRoyalGoldDimColor": "#5C440C",
    "SxRoyalGoldSoftColor": "#F6ECD2",
    "SxAccentGradStartColor": "#B0522F",
    "SxAccentGradEndColor": "#6B4FA0",
    "SxSuccessColor": "#276A44",
    "SxSuccessDimColor": "#1F5638",
    "SxSuccessGradEndColor": "#2E7D4F",
    "SxSuccessBgColor": "#DFF0E4",
    "SxWarningColor": "#8A5A14",
    "SxWarningDimColor": "#6E4A10",
    "SxWarningBgColor": "#F7EBD2",
    "SxErrorColor": "#B33830",
    "SxErrorDimColor": "#7E241E",
    "SxErrorGradEndColor": "#96322B",
    "SxErrorBgColor": "#F8E3E0",
    "SxInfoColor": "#2E5F80",
    "SxInfoBgColor": "#E2EEF5",
    "SxVoiceActiveColor": "#B0522F",
    "SxVoiceStandbyColor": "#8A5A14",
    "SxVoiceOffColor": "#6E6A5E",
    "SxFocusRingColor": "#C15F3C",
    "SxShadowColor": "#33203050",
    "SxShimmerColor": "#1A203050",
    "SxRevealColor": "#26C15F3C",
    "SxWhiteColor": "#FFFFFF",
    "SxTransparentColor": "#00000000",
    "SxUserBubbleColor": "#F3E0D6",
    "SxUserBubbleBorderColor": "#DDB9A6",
    "SxAssistantBubbleColor": "#F7F5EF",
    "SxAssistantBubbleBorderColor": "#E0DBCC",
    "SxAuroraAColor": "#C15F3C",
    "SxAuroraBColor": "#7742A8",
    "SxAuroraCColor": "#C98A4B",
    "SxOverlayBgColor": "#E6FAF9F5",
    "SxScrimColor": "#8C2A2822",
}

# Pary (tekst, tło), które muszą mieć kontrast ≥ 4,5:1 w każdym motywie.
CONTRAST_PAIRS = [
    ("SxTextPrimaryColor", "SxBackgroundColor"),
    ("SxTextPrimaryColor", "SxSurfaceColor"),
    ("SxTextSecondaryColor", "SxSurfaceColor"),
    ("SxTextSecondaryColor", "SxBackgroundColor"),
    ("SxTextMutedColor", "SxSurfaceColor"),
    ("SxSuccessColor", "SxSurfaceColor"),
    ("SxWarningColor", "SxSurfaceColor"),
    ("SxErrorColor", "SxSurfaceColor"),
    ("SxAccentCyanColor", "SxSurfaceColor"),
    ("SxAccentVioletColor", "SxSurfaceColor"),
    ("SxWhiteColor", "SxAccentGradStartColor"),
    ("SxWhiteColor", "SxAccentGradEndColor"),
    ("SxSuccessColor", "SxSuccessBgColor"),
    ("SxWarningColor", "SxWarningBgColor"),
    ("SxRoyalGoldColor", "SxSurfaceColor"),
    ("SxRoyalGoldColor", "SxRoyalGoldSoftColor"),
    ("SxErrorColor", "SxErrorBgColor"),
    ("SxInfoColor", "SxInfoBgColor"),
    ("SxAccentCyanColor", "SxAccentCyanSoftColor"),
    ("SxTextPrimaryColor", "SxUserBubbleColor"),
    ("SxTextPrimaryColor", "SxAssistantBubbleColor"),
]
# Duże litery (≥ 24 px / 19 px bold) wystarczy 3:1.
CONTRAST_PAIRS_LARGE = [
    ("SxAccentCyanColor", "SxBackgroundColor"),
    ("SxAccentPinkColor", "SxSurfaceColor"),
    ("SxAccentBlueColor", "SxSurfaceColor"),
    ("SxAccentTealColor", "SxSurfaceColor"),
    ("SxAccentLimeColor", "SxSurfaceColor"),
    ("SxWhiteColor", "SxErrorDimColor"),
    ("SxWhiteColor", "SxErrorGradEndColor"),
    ("SxWhiteColor", "SxSuccessDimColor"),
    ("SxWhiteColor", "SxSuccessGradEndColor"),
]


def parse_color(value: str):
    text = value.lstrip("#")
    if len(text) == 6:
        alpha, rgb = 255, text
    elif len(text) == 8:
        alpha, rgb = int(text[0:2], 16), text[2:]
    else:
        raise ValueError(f"Nieobsłużony zapis koloru: {value}")
    channels = [int(rgb[i:i + 2], 16) / 255 for i in (0, 2, 4)]

    def linear(channel):
        return channel / 12.92 if channel <= 0.03928 else ((channel + 0.055) / 1.055) ** 2.4

    luminance = 0.2126 * linear(channels[0]) + 0.7152 * linear(channels[1]) + 0.0722 * linear(channels[2])
    return (alpha / 255, luminance)


def contrast_ratio(foreground: str, background: str) -> float:
    """Kontrast WCAG z uwzględnieniem alfa: kolor półprzezroczysty jest sklepany z tłem."""
    fa, fl = parse_color(foreground)
    ba, bl = parse_color(background)
    if fa < 1:
        # Składanie alfa zmienia zarówno luminancję, jak i przezroczystość — liczymy wynik.
        fl = fa * fl + (1 - fa) * bl
        fa = 1.0
    lighter, darker = max(fl, bl), min(fl, bl)
    return (lighter + 0.05) / (darker + 0.05)


def color_keys() -> list:
    document = ET.parse(ROOT / "Themes" / "Colors.xaml")
    return [node.attrib[XAML_NS + "Key"] for node in document.iter()
            if node.tag.endswith("}Color") and node.attrib.get(XAML_NS + "Key")]


def render(palette: dict, keys: list) -> str:
    body = "".join(f'    <Color x:Key="{key}">{palette[key]}</Color>\n' for key in keys)
    return HEADER + body + "</ResourceDictionary>\n"


def main() -> int:
    keys = color_keys()
    problems = []
    for name, palette in (("Dark", DARK), ("Deep Dark", DEEP_DARK), ("Light", LIGHT)):
        missing = [key for key in keys if key not in palette]
        extra = [key for key in palette if key not in keys]
        if missing:
            problems.append(f"{name}: brak wartości dla {missing}")
        if extra:
            problems.append(f"{name}: martwe tokeny {extra}")
    for name, palette in (("Dark", DARK), ("Deep Dark", DEEP_DARK), ("Light", LIGHT)):
        for text, background in CONTRAST_PAIRS:
            ratio = contrast_ratio(palette[text], palette[background])
            if ratio < 4.5:
                problems.append(f"{name}: kontrast {text}/{background} = {ratio:.2f} (< 4,5)")
        for text, background in CONTRAST_PAIRS_LARGE:
            ratio = contrast_ratio(palette[text], palette[background])
            if ratio < 3.0:
                problems.append(f"{name}: kontrast dużych liter {text}/{background} = {ratio:.2f} (< 3,0)")
    if problems:
        print("FAIL:")
        for problem in problems:
            print("  -", problem)
        return 1

    if "--check" not in sys.argv:
        (ROOT / "Themes" / "DarkTheme.xaml").write_text(render(DARK, keys), encoding="utf-8")
        (ROOT / "Themes" / "DeepDarkTheme.xaml").write_text(render(DEEP_DARK, keys), encoding="utf-8")
        (ROOT / "Themes" / "LightTheme.xaml").write_text(render(LIGHT, keys), encoding="utf-8")
    samples = [(text, background, contrast_ratio(LIGHT[text], LIGHT[background]))
               for text, background in CONTRAST_PAIRS[:5]]
    print("PASS: " + str(len(keys)) + " tokenów × 3 motywy, kontrast OK")
    for text, background, ratio in samples:
        print(f"  Light {text} / {background} = {ratio:.2f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
