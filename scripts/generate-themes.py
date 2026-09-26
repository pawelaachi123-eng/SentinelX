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
    "SxBackgroundColor": "#0B0D14",
    "SxSurfaceColor": "#141824",
    "SxSurfaceRaisedColor": "#191E2C",
    "SxSurfaceHoverColor": "#1F2536",
    "SxSurfaceActiveColor": "#262D42",
    "SxSurfaceSunkenColor": "#0E1119",
    "SxSurfaceGlassColor": "#B3151A26",
    "SxSidebarColor": "#0D1017",
    "SxSidebarHoverColor": "#171C28",
    "SxSidebarActiveColor": "#1B2233",
    "SxBorderColor": "#2B3245",
    "SxBorderSubtleColor": "#1E2431",
    "SxBorderStrongColor": "#3C4661",
    "SxTextPrimaryColor": "#EDF1FA",
    "SxTextSecondaryColor": "#98A2BC",
    "SxTextMutedColor": "#7B87A3",
    "SxTextDisabledColor": "#414A5E",
    "SxTextOnAccentColor": "#04121A",
    "SxAccentCyanColor": "#22D3EE",
    "SxAccentCyanDimColor": "#0E7490",
    "SxAccentCyanSoftColor": "#164E63",
    "SxAccentVioletColor": "#A78BFA",
    "SxAccentVioletDimColor": "#6D28D9",
    "SxAccentVioletSoftColor": "#3B2A6B",
    "SxAccentBlueColor": "#60A5FA",
    "SxAccentBlueDimColor": "#1D4ED8",
    "SxAccentBlueSoftColor": "#1E3A8A",
    "SxAccentTealColor": "#2DD4BF",
    "SxAccentTealDimColor": "#0F766E",
    "SxAccentTealSoftColor": "#134E4A",
    "SxAccentPinkColor": "#F472B6",
    "SxAccentPinkDimColor": "#BE185D",
    "SxAccentPinkSoftColor": "#5B1D3C",
    "SxAccentLimeColor": "#A3E635",
    "SxAccentLimeDimColor": "#4D7C0F",
    # Królewskie złoto (herb, korona, wordmark): osobna rodzina, żeby semantyka warning została bursztynem.
    "SxRoyalGoldColor": "#E7C25C",
    "SxRoyalGoldDimColor": "#8F6B1F",
    "SxRoyalGoldSoftColor": "#3A2E12",
    "SxAccentGradStartColor": "#155E75",
    "SxAccentGradEndColor": "#6D28D9",
    "SxSuccessColor": "#34D399",
    "SxSuccessDimColor": "#065F46",
    "SxSuccessGradEndColor": "#047857",
    "SxSuccessBgColor": "#0E3B2E",
    "SxWarningColor": "#FBBF24",
    "SxWarningDimColor": "#B45309",
    "SxWarningBgColor": "#3D2A0B",
    "SxErrorColor": "#FB7185",
    "SxErrorDimColor": "#BE123C",
    "SxErrorGradEndColor": "#F43F5E",
    "SxErrorBgColor": "#3E1420",
    "SxInfoColor": "#7DD3FC",
    "SxInfoBgColor": "#12314A",
    "SxVoiceActiveColor": "#22D3EE",
    "SxVoiceStandbyColor": "#FBBF24",
    "SxVoiceOffColor": "#7B87A3",
    "SxFocusRingColor": "#67E8F9",
    "SxShadowColor": "#80000000",
    "SxShimmerColor": "#22FFFFFF",
    "SxRevealColor": "#2E22D3EE",
    "SxWhiteColor": "#FFFFFF",
    "SxTransparentColor": "#00000000",
    "SxUserBubbleColor": "#1B3A57",
    "SxUserBubbleBorderColor": "#3B82F6",
    "SxAssistantBubbleColor": "#171C2A",
    "SxAssistantBubbleBorderColor": "#2B3245",
    "SxAuroraAColor": "#22D3EE",
    "SxAuroraBColor": "#A78BFA",
    "SxAuroraCColor": "#F472B6",
    "SxOverlayBgColor": "#CC0B0D14",
    "SxScrimColor": "#B3070910",
}

# Deep Dark: ta sama paleta akcentów, ciemniejsze powierzchnie (mniej poświaty, więcej kontrastu krawędzi).
DEEP_DARK = dict(DARK, **{
    "SxBackgroundColor": "#05070C",
    "SxSurfaceColor": "#0C1017",
    "SxSurfaceRaisedColor": "#11161F",
    "SxSurfaceHoverColor": "#161C28",
    "SxSurfaceActiveColor": "#1C2331",
    "SxSurfaceSunkenColor": "#070A10",
    "SxSurfaceGlassColor": "#B30B0F16",
    "SxSidebarColor": "#070910",
    "SxSidebarHoverColor": "#101520",
    "SxSidebarActiveColor": "#141A28",
    "SxBorderColor": "#232A3A",
    "SxBorderSubtleColor": "#161C27",
    "SxBorderStrongColor": "#333D54",
    "SxSuccessBgColor": "#0A2C22",
    "SxWarningBgColor": "#30210A",
    "SxRoyalGoldColor": "#F0CE74",
    "SxRoyalGoldDimColor": "#7E5E1A",
    "SxRoyalGoldSoftColor": "#2A2110",
    "SxErrorBgColor": "#31101A",
    "SxInfoBgColor": "#0E2739",
    "SxUserBubbleColor": "#16304A",
    "SxAssistantBubbleColor": "#10141D",
    "SxAssistantBubbleBorderColor": "#232A3A",
    "SxOverlayBgColor": "#CC05070C",
    "SxScrimColor": "#C203050A",
})

# Light / System: jasne powierzchnie, ciemniejsze akcenty (kontrast tekstu ≥ 4,5:1).
LIGHT = {
    "SxBackgroundColor": "#EEF2F9",
    "SxSurfaceColor": "#FFFFFF",
    "SxSurfaceRaisedColor": "#FFFFFF",
    "SxSurfaceHoverColor": "#E7EDF8",
    "SxSurfaceActiveColor": "#DCE5F2",
    "SxSurfaceSunkenColor": "#F4F7FC",
    "SxSurfaceGlassColor": "#CCFFFFFF",
    "SxSidebarColor": "#E6ECF6",
    "SxSidebarHoverColor": "#DCE4F1",
    "SxSidebarActiveColor": "#D2E6F2",
    "SxBorderColor": "#B4C0D4",
    "SxBorderSubtleColor": "#D3DCE9",
    "SxBorderStrongColor": "#9AA8C0",
    "SxTextPrimaryColor": "#131F33",
    "SxTextSecondaryColor": "#45566F",
    "SxTextMutedColor": "#5C6C86",
    "SxTextDisabledColor": "#94A1B4",
    "SxTextOnAccentColor": "#0A0F16",
    "SxAccentCyanColor": "#0E7490",
    "SxAccentCyanDimColor": "#155E75",
    "SxAccentCyanSoftColor": "#E1F3F9",
    "SxAccentVioletColor": "#6D28D9",
    "SxAccentVioletDimColor": "#5B21B6",
    "SxAccentVioletSoftColor": "#E8E1FB",
    "SxAccentBlueColor": "#1D4ED8",
    "SxAccentBlueDimColor": "#1E40AF",
    "SxAccentBlueSoftColor": "#DCE7FB",
    "SxAccentTealColor": "#0F766E",
    "SxAccentTealDimColor": "#115E59",
    "SxAccentTealSoftColor": "#D8F2EE",
    "SxAccentPinkColor": "#BE185D",
    "SxAccentPinkDimColor": "#9D174D",
    "SxAccentPinkSoftColor": "#FBDDEA",
    "SxAccentLimeColor": "#4D7C0F",
    "SxAccentLimeDimColor": "#3F6212",
    "SxAccentGradStartColor": "#0E7490",
    "SxAccentGradEndColor": "#5B21B6",
    "SxSuccessColor": "#047857",
    "SxSuccessDimColor": "#064E3B",
    "SxSuccessGradEndColor": "#047857",
    "SxSuccessBgColor": "#DCF3E9",
    "SxWarningColor": "#92400E",
    "SxWarningDimColor": "#B45309",
    "SxWarningBgColor": "#FBEED4",
    "SxRoyalGoldColor": "#7A5B10",
    "SxRoyalGoldDimColor": "#5C440C",
    "SxRoyalGoldSoftColor": "#F6ECD2",
    "SxErrorColor": "#BE123C",
    "SxErrorDimColor": "#7F1D1D",
    "SxErrorGradEndColor": "#9F1239",
    "SxErrorBgColor": "#FBDEE4",
    "SxInfoColor": "#0369A1",
    "SxInfoBgColor": "#DCEBFA",
    "SxVoiceActiveColor": "#0E7490",
    "SxVoiceStandbyColor": "#92400E",
    "SxVoiceOffColor": "#5C6C86",
    "SxFocusRingColor": "#0369A1",
    "SxShadowColor": "#33203050",
    "SxShimmerColor": "#1A203050",
    "SxRevealColor": "#260891B2",
    "SxWhiteColor": "#FFFFFF",
    "SxTransparentColor": "#00000000",
    "SxUserBubbleColor": "#DCE9F8",
    "SxUserBubbleBorderColor": "#9CC0EA",
    "SxAssistantBubbleColor": "#F5F8FC",
    "SxAssistantBubbleBorderColor": "#D3DCE9",
    "SxAuroraAColor": "#0891B2",
    "SxAuroraBColor": "#6D28D9",
    "SxAuroraCColor": "#BE185D",
    "SxOverlayBgColor": "#E6EEF2F9",
    "SxScrimColor": "#8C1B2436",
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
