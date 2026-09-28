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
    "SxBackgroundColor": "#0C0C0F",
    "SxSurfaceColor": "#131316",
    "SxSurfaceRaisedColor": "#19191D",
    "SxSurfaceHoverColor": "#1E1E24",
    "SxSurfaceActiveColor": "#26262D",
    "SxSurfaceSunkenColor": "#08080A",
    "SxSurfaceGlassColor": "#B3131316",
    "SxSidebarColor": "#09090B",
    "SxSidebarHoverColor": "#16161A",
    "SxSidebarActiveColor": "#1E1E24",
    "SxBorderColor": "#2A2A31",
    "SxBorderSubtleColor": "#1A1A1F",
    "SxBorderStrongColor": "#3A3A44",
    "SxTextPrimaryColor": "#F4F4F2",
    "SxTextSecondaryColor": "#A0A0AC",
    "SxTextMutedColor": "#85858F",
    "SxTextDisabledColor": "#4A4A54",
    "SxTextOnAccentColor": "#0D0D12",
    "SxAccentCyanColor": "#A195F5",
    "SxAccentCyanDimColor": "#7A6BE0",
    "SxAccentCyanSoftColor": "#242148",
    "SxAccentVioletColor": "#8FB8E8",
    "SxAccentVioletDimColor": "#5B84C4",
    "SxAccentVioletSoftColor": "#1B2737",
    "SxAccentBlueColor": "#6FCFAF",
    "SxAccentBlueDimColor": "#3E9B79",
    "SxAccentBlueSoftColor": "#16302A",
    "SxAccentTealColor": "#5FB8C9",
    "SxAccentTealDimColor": "#37819B",
    "SxAccentTealSoftColor": "#14303A",
    "SxAccentPinkColor": "#E39AB8",
    "SxAccentPinkDimColor": "#B25E82",
    "SxAccentPinkSoftColor": "#32202B",
    "SxAccentLimeColor": "#A8C98A",
    "SxAccentLimeDimColor": "#6F9455",
    "SxRoyalGoldColor": "#E8C97E",
    "SxRoyalGoldDimColor": "#A07F35",
    "SxRoyalGoldSoftColor": "#332B15",
    "SxAccentGradStartColor": "#6D5BD8",
    "SxAccentGradEndColor": "#A84F87",
    "SxSuccessColor": "#6FCF9A",
    "SxSuccessDimColor": "#2E6B4C",
    "SxSuccessGradEndColor": "#3D9A68",
    "SxSuccessBgColor": "#17281F",
    "SxWarningColor": "#E8C063",
    "SxWarningDimColor": "#94702A",
    "SxWarningBgColor": "#2B2412",
    "SxErrorColor": "#EF8B80",
    "SxErrorDimColor": "#7E352C",
    "SxErrorGradEndColor": "#D0665C",
    "SxErrorBgColor": "#2E1B18",
    "SxInfoColor": "#86B8E0",
    "SxInfoBgColor": "#17242E",
    "SxVoiceActiveColor": "#A195F5",
    "SxVoiceStandbyColor": "#E8C063",
    "SxVoiceOffColor": "#85858F",
    "SxFocusRingColor": "#C3B8FF",
    "SxShadowColor": "#66000000",
    "SxShimmerColor": "#14FFFFFF",
    "SxRevealColor": "#2EA195F5",
    "SxWhiteColor": "#FFFFFF",
    "SxTransparentColor": "#00000000",
    "SxUserBubbleColor": "#26243C",
    "SxUserBubbleBorderColor": "#4E4794",
    "SxAssistantBubbleColor": "#1B1B1F",
    "SxAssistantBubbleBorderColor": "#2A2A31",
    "SxAuroraAColor": "#6D5BD8",
    "SxAuroraBColor": "#37819B",
    "SxAuroraCColor": "#C46B8A",
    "SxOverlayBgColor": "#CC0C0C0F",
    "SxScrimColor": "#B308080A",
}

# Deep Dark: ta sama paleta akcentów, ciemniejsze powierzchnie (mniej poświaty, więcej kontrastu krawędzi).
DEEP_DARK = dict(DARK, **{
    "SxBackgroundColor": "#070709",
    "SxSurfaceColor": "#0E0E11",
    "SxSurfaceRaisedColor": "#131317",
    "SxSurfaceHoverColor": "#18181D",
    "SxSurfaceActiveColor": "#202027",
    "SxSurfaceSunkenColor": "#040405",
    "SxSurfaceGlassColor": "#B30E0E11",
    "SxSidebarColor": "#050507",
    "SxSidebarHoverColor": "#101014",
    "SxSidebarActiveColor": "#16161B",
    "SxBorderColor": "#232329",
    "SxBorderSubtleColor": "#15151A",
    "SxBorderStrongColor": "#30303A",
    "SxRoyalGoldColor": "#F0D68F",
    "SxRoyalGoldDimColor": "#8A6D2E",
    "SxRoyalGoldSoftColor": "#292212",
    "SxSuccessBgColor": "#121F18",
    "SxWarningBgColor": "#201A0E",
    "SxErrorBgColor": "#241413",
    "SxInfoBgColor": "#121A21",
    "SxUserBubbleColor": "#201E33",
    "SxAssistantBubbleColor": "#121215",
    "SxAssistantBubbleBorderColor": "#232329",
    "SxOverlayBgColor": "#CC070709",
    "SxScrimColor": "#C2040406",
})

# Light / System: jasne powierzchnie, ciemniejsze akcenty (kontrast tekstu ≥ 4,5:1).
LIGHT = {
    "SxBackgroundColor": "#FAFAFA",
    "SxSurfaceColor": "#FFFFFF",
    "SxSurfaceRaisedColor": "#FFFFFF",
    "SxSurfaceHoverColor": "#F1F1F3",
    "SxSurfaceActiveColor": "#E8E8EC",
    "SxSurfaceSunkenColor": "#F4F4F6",
    "SxSurfaceGlassColor": "#CCFFFFFF",
    "SxSidebarColor": "#F1F1F3",
    "SxSidebarHoverColor": "#E9E9ED",
    "SxSidebarActiveColor": "#E0E0E6",
    "SxBorderColor": "#DBDBE1",
    "SxBorderSubtleColor": "#EBEBEF",
    "SxBorderStrongColor": "#C2C2CC",
    "SxTextPrimaryColor": "#17171C",
    "SxTextSecondaryColor": "#52525E",
    "SxTextMutedColor": "#6B6B76",
    "SxTextDisabledColor": "#A0A0AA",
    "SxTextOnAccentColor": "#131318",
    "SxAccentCyanColor": "#5F4ECF",
    "SxAccentCyanDimColor": "#4F40B8",
    "SxAccentCyanSoftColor": "#ECEAFB",
    "SxAccentVioletColor": "#8E4E9E",
    "SxAccentVioletDimColor": "#753F84",
    "SxAccentVioletSoftColor": "#F1E9F7",
    "SxAccentBlueColor": "#3A64AA",
    "SxAccentBlueDimColor": "#32558F",
    "SxAccentBlueSoftColor": "#E7EEF8",
    "SxAccentTealColor": "#22708A",
    "SxAccentTealDimColor": "#1B5B71",
    "SxAccentTealSoftColor": "#E1F0F5",
    "SxAccentPinkColor": "#9E4462",
    "SxAccentPinkDimColor": "#8A3A55",
    "SxAccentPinkSoftColor": "#F8E6ED",
    "SxAccentLimeColor": "#5E7226",
    "SxAccentLimeDimColor": "#4C5C1E",
    "SxRoyalGoldColor": "#7A5B10",
    "SxRoyalGoldDimColor": "#5C440C",
    "SxRoyalGoldSoftColor": "#F6ECD2",
    "SxAccentGradStartColor": "#6D5BD8",
    "SxAccentGradEndColor": "#B04E7E",
    "SxSuccessColor": "#226A44",
    "SxSuccessDimColor": "#1A5236",
    "SxSuccessGradEndColor": "#3D9A68",
    "SxSuccessBgColor": "#DFF0E4",
    "SxWarningColor": "#8A5A14",
    "SxWarningDimColor": "#6E4A10",
    "SxWarningBgColor": "#F7EBD2",
    "SxErrorColor": "#B33830",
    "SxErrorDimColor": "#7E241E",
    "SxErrorGradEndColor": "#96322B",
    "SxErrorBgColor": "#F8E3E0",
    "SxInfoColor": "#2E5F80",
    "SxInfoBgColor": "#DCE9F3",
    "SxVoiceActiveColor": "#5F4ECF",
    "SxVoiceStandbyColor": "#8A5A14",
    "SxVoiceOffColor": "#6B6B76",
    "SxFocusRingColor": "#5F4ECF",
    "SxShadowColor": "#33203050",
    "SxShimmerColor": "#1A203050",
    "SxRevealColor": "#266F5EDD",
    "SxWhiteColor": "#FFFFFF",
    "SxTransparentColor": "#00000000",
    "SxUserBubbleColor": "#E9E7FB",
    "SxUserBubbleBorderColor": "#C5BFEF",
    "SxAssistantBubbleColor": "#F4F4F6",
    "SxAssistantBubbleBorderColor": "#EBEBEF",
    "SxAuroraAColor": "#6D5BD8",
    "SxAuroraBColor": "#22708A",
    "SxAuroraCColor": "#9E4462",
    "SxOverlayBgColor": "#E6FAFAFA",
    "SxScrimColor": "#8C232329",
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
