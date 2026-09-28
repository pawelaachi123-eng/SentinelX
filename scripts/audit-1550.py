#!/usr/bin/env python3
"""AUDYT 1550 — sprawdzenie, ile funkcji z master listy jest realnie w kodzie.

Po co: żeby werdykt „ile z 1550 zrobione” dało się powtórzyć jednym poleceniem,
a nie opierał się na czyjejś deklaracji. Skrypt liczy WYZWALACZE POLECEŃ
(Starts/text is/case/StartsWith oraz wzorce Regex @"^…") w każdym module obsługi,
przypisuje moduły do sekcji master listy i wypisuje tabelę.

Wejście opcjonalne: docs/LISTA-1550.md — jeśli plik istnieje, skrypt czyta numerowane
pozycje („1. …”, „2. …”) i tworzy docs/AUDYT-1550-POZYCJE.md: listę kontrolną
z heurystycznym dopasowaniem do zaimplementowanych poleceń. Bez tego pliku nie da się
uczciwie powiedzieć, czy pozycja nr 723 jest zrobiona — bo nie wiadomo, co to za pozycja.

Uruchomienie:  python3 scripts/audit-1550.py
Wynik:         tabela na ekranie + docs/AUDYT-1550-AUTO.md (+ opcjonalnie POZYCJE)
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Moduł obsługi → sekcja master listy (1–20 + bonus). Kolejność = kolejność w tabeli.
MODULES = [
    ("Core/Runtime/RuntimeCommands.cs", "1", "Rdzeń", "1–45"),
    ("ModelToolbox.cs", "2", "Modele lokalne (Ollama)", "46–120"),
    ("PrivacyToolbox.cs", "3", "Prywatność i dane", "121–185"),
    ("DeveloperToolbox.cs", "4", "Kodowanie", "186–340"),
    ("CodeInsightsService.cs", "4", "Kodowanie (analiza kodu)", "186–340"),
    ("UtilityToolbox.cs", "4", "Kodowanie (tekst, hasła, liczby)", "186–340"),
    ("ProductivityToolbox.cs", "13", "Produktywność i finanse", "941–1020"),
    ("LifeToolbox.cs", "14", "Komunikacja + zdrowie", "1021–1070, 1201–1250"),
    ("AnalysisToolbox.cs", "15", "Analiza danych", "1071–1140"),
    ("JarvisToolkit.cs", "19", "Sterowanie systemem (0.96)", "1301–1400 (część)"),
    ("RoutineCommands.cs", "11", "Automatyzacja (rutyny)", "801–880 (część)"),
    ("ArchitectureToolbox.cs", "5", "Architektura", "341–420"),
    ("FullStackToolbox.cs", "6", "Full-stack (generatory offline)", "421–520"),
    ("KnowledgeToolbox.cs", "8", "Wiedza i nauka", "591–670"),
    ("ResearchToolbox.cs", "12", "Research (offline, bez sieci)", "881–940"),
    ("SmartHomeToolbox.cs", "18", "Smart home (planowanie)", "1251–1300"),
    ("GoalToolbox.cs", "20", "Agentic (cele i ryzyko)", "1401–1500"),
    ("MediaVisionToolbox.cs", "9-10", "Głos i wizja (media)", "671–800"),
    ("KnowledgeIndexService.cs", "8", "Indeks plików tekstowych (RAM)", "591–670 (dopełnienie)"),
    ("KnowledgeRagService.cs", "8", "RAG (wektory z lokalnej Ollamy)", "591–670 (dopełnienie)"),
    ("WebAccessService.cs", "12", "WiFi i szukanie w sieci", "881–940 (dopełnienie)"),
    ("GameDevToolbox.cs", "—", "Game Dev (Roblox) — nowy obszar", "poza 20 sekcjami listy"),
    ("gamedev/ (pełna gra + Blender)", "—", "Mega Obby: 120 etapów z kodu + modele/animacje", "poza 20 sekcjami listy"),
    ("gamedev/ (GameForge)", "—", "Generator gier: 6 gatunków z jednej linijki GameSpec", "poza 20 sekcjami listy"),
    ("gamedev/ (różnorodność)", "—", "9 gatunków, 3. jajko, losowane eventy, bogatsze nagrody", "poza 20 sekcjami listy"),
]

# Sekcje bez modułu obsługi poleceń — z jawnym powodem, żeby brak był widoczny.
MISSING = [
    ("20", "Agentic (reszta: planowanie przez model, autonomia z budżetem)", "1401–1500", "GoalToolbox pokrywa szablony; modelowe planowanie celów nadal nie istnieje"),
]

TRIGGER_STARTS = re.compile(r"Starts\(\s*(?:text|command)\s*,(.*?)\)\s*[;{]", re.S)
TRIGGER_IS = re.compile(r'\b(?:text|command)\s+is\s+((?:"[^"]*"(?:\s+or\s+)?)+)')
TRIGGER_CASE = re.compile(r'case\s+"([^"]{2,40})"\s*:')
TRIGGER_STARTSWITH = re.compile(r'StartsWith\(\s*"([^"]{2,40})"')
TRIGGER_REGEX = re.compile(r'@"\^([^"]{4,90})"')
TRIGGER_IS_HELPER = re.compile(r'\b(?:Extra)?Is\(\s*(?:norm|en|flat|text)\s*,\s*"([^"]{2,40})"\s*\)')
LITERAL = re.compile(r'"([^"]{2,40})"')


def triggers(path: pathlib.Path) -> tuple[set[str], set[str]]:
    """Wyzwalacze poleceń w pliku: literały + wzorce Regex zakotwiczone na ^."""
    source = path.read_text(encoding="utf-8", errors="replace")
    literals: set[str] = set()
    for group in TRIGGER_STARTS.findall(source):
        literals |= set(LITERAL.findall(group))
    for group in TRIGGER_IS.findall(source):
        literals |= set(LITERAL.findall(group))
    literals |= set(TRIGGER_CASE.findall(source))
    literals |= set(TRIGGER_STARTSWITH.findall(source))
    literals = {x for x in literals if not x.startswith(("//", "/*", "`"))}
    literals |= set(TRIGGER_IS_HELPER.findall(source))
    return literals, set(TRIGGER_REGEX.findall(source))


def catalog_counts() -> dict[str, int]:
    palette = ROOT / "Core/CommandCatalog.cs"
    intent = ROOT / "Core/IntentCatalog.cs"
    return {
        "paleta": len(re.findall(r'new\("', palette.read_text(encoding="utf-8"))) if palette.exists() else 0,
        "frazy": len(re.findall(r'"([^"]+)"', intent.read_text(encoding="utf-8"))) if intent.exists() else 0,
    }


def audit() -> tuple[list[str], int]:
    lines: list[str] = []
    total = 0
    lines.append("| Sekcja | Pozycje | Moduł | Wyzwalacze poleceń | Wzorce |")
    lines.append("|---|---|---|---|---|")
    for path, section, label, positions in MODULES:
        file = ROOT / path
        if not file.exists():
            lines.append(f"| {section}. {label} | {positions} | `{path}` | **BRAK PLIKU** | — |")
            continue
        literals, patterns = triggers(file)
        total += len(literals) + len(patterns)
        lines.append(f"| {section}. {label} | {positions} | `{path}` | {len(literals)} | {len(patterns)} |")
    for section, label, positions, reason in MISSING:
        lines.append(f"| {section}. {label} | {positions} | — | 0 ({reason}) | 0 |")
    return lines, total


def checklist() -> str | None:
    """Jeśli lista 1550 jest w repo, zrób z niej listę kontrolną z heurystyką dopasowania."""
    source = ROOT / "docs/LISTA-1550.md"
    if not source.exists():
        return None
    items = re.findall(r"^\s*(\d{1,4})[.)]\s+(.+?)\s*$", source.read_text(encoding="utf-8"), re.M)
    if not items:
        return None
    implemented: set[str] = set()
    for path, *_ in MODULES:
        file = ROOT / path
        if not file.exists():
            continue
        literals, patterns = triggers(file)
        implemented |= {x.lower() for x in literals}
        for pattern in patterns:
            implemented |= {w for w in re.findall(r"[a-ząćęłńóśźż]{4,}", pattern.lower())}
    out = ["# AUDYT 1550 — pozycja po pozycji (lista kontrolna)", "",
           "Heurystyka: słowa kluczowe pozycji porównane z wyzwalaczami poleceń w kodzie.",
           "`PRAWDOPODOBNIE JEST` nie jest dowodem — dowodem jest polecenie i test w CI.", "",
           "| # | Pozycja | Dopasowanie |", "|---|---|---|"]
    hits = 0
    for number, text in items:
        words = [w for w in re.findall(r"[a-ząćęłńóśźż]{4,}", text.lower())]
        matched = [w for w in words if w in implemented]
        if matched:
            hits += 1
            out.append(f"| {number} | {text} | PRAWDOPODOBNIE JEST ({', '.join(matched[:4])}) |")
        else:
            out.append(f"| {number} | {text} | DO SPRAWDZENIA (brak dopasowania) |")
    out.append("")
    out.append(f"Pozycji w pliku: **{len(items)}**, z jakimkolwiek dopasowaniem: **{hits}**.")
    (ROOT / "docs/AUDYT-1550-POZYCJE.md").write_text("\n".join(out) + "\n", encoding="utf-8")
    return f"docs/AUDYT-1550-POZYCJE.md ({len(items)} pozycji, dopasowanych {hits})"


def main() -> int:
    lines, total = audit()
    counts = catalog_counts()
    report = ["# AUDYT 1550 — część automatyczna", "",
              "Wygenerowane przez `python3 scripts/audit-1550.py`. Liczby pochodzą z kodu:",
              "wyzwalacze poleceń (`Starts`, `text is`, `case`, `StartsWith`) oraz wzorce `Regex @\"^…\"`.", "",
              *lines, "",
              f"**Suma wyzwalaczy w modułach obsługi:** {total} · "
              f"**paleta `//`:** {counts['paleta']} wpisów · **rozumienie języka:** {counts['frazy']} fraz", "",
              "Werdykt (ludzki, z listą braków per sekcja): [AUDYT-1550.md](AUDYT-1550.md).", ""]
    (ROOT / "docs/AUDYT-1550-AUTO.md").write_text("\n".join(report) + "\n", encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSuma wyzwalaczy: {total} · paleta: {counts['paleta']} · frazy: {counts['frazy']}")
    print("Zapisane: docs/AUDYT-1550-AUTO.md")
    extra = checklist()
    print(("Lista kontrolna: " + extra) if extra else
          "Brak docs/LISTA-1550.md — mapowanie pozycja-po-pozycji niemożliwe (nie wiem, co jest w pozycji nr 723).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
