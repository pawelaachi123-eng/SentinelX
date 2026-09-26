#!/usr/bin/env python3
"""Fast portable checks, complementary to (not a substitute for) the Windows build."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
XAML_NS = "{http://schemas.microsoft.com/winfx/2006/xaml}"
keys = set()
files = list((ROOT / "Themes").glob("*.xaml")) + [ROOT / "App.xaml"]
for path in files:
    doc = ET.parse(path)
    local = set()
    for node in doc.iter():
        key = node.attrib.get(XAML_NS + "Key")
        if key:
            assert key not in local, f"Duplicate key {key}: {path}"
            local.add(key)
    keys |= local

for path in (ROOT / "Views").rglob("*.xaml"):
    document = ET.parse(path)
    for node in document.iter():
        if node.tag.endswith("}ProgressBar") and node.attrib.get("Value", "").startswith("{Binding"):
            assert "Mode=OneWay" in node.attrib["Value"], f"Read-only metrics need OneWay: {path}"
    source = path.read_text(encoding="utf-8")
    for resource in re.findall(r"\{(?:Static|Dynamic)Resource (Sx\w+|BoolToVisibility|StatusBrush|VoiceBrush|RiskBrush|ReadinessBrush|StringNotEmptyToVisibility|SafePercent|FiniteToVisibility)\}", source):
        assert resource in keys, f"Unknown resource {resource}: {path}"
    assert not re.search(r'="#[0-9a-fA-F]{6,8}"', source), f"Hard-coded view color: {path}"
    behind = Path(str(path) + ".cs")
    assert behind.exists(), f"Missing code-behind: {path}"
    assert len(behind.read_text(encoding="utf-8").splitlines()) < 20, f"Non-trivial code-behind: {behind}"

for path in (ROOT / "ViewModels").glob("*.cs"):
    source = path.read_text(encoding="utf-8")
    for forbidden in ("Process.Start(", "File.Read", "File.Write", "Registry.Current", "ManagementObjectSearcher"):
        assert forbidden not in source, f"System operation {forbidden} in {path}"

# ---------------------------------------------------------------------------
# Bramki pułapek XAML (0.93 · NOWOCZESNE GUI): klasy błędów, których kompilator
# XAML nie wybacza, a których nie da się tu zbudować (brak .NET w środowisku dev).
# Każda reguła odpowiada realnemu błędowi z CI albo realnej wadzie runtime.
# ---------------------------------------------------------------------------
WPF_NS = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
xaml_files = list((ROOT / "Themes").glob("*.xaml")) + [ROOT / "App.xaml"] + list((ROOT / "Views").rglob("*.xaml"))
attached = set()
for utility in sorted((ROOT / "Utilities").glob("*.cs")):
    text = utility.read_text(encoding="utf-8")
    # RegisterAttached("Nazwa", typeof(TYP), typeof(Właściciel), …) → para (właściciel, nazwa)
    attached |= {(owner, prop) for prop, owner in re.findall(
        r'RegisterAttached\(\s*"(\w+)"\s*,\s*typeof\([^)]*\)\s*,\s*typeof\((\w+)\)', text, re.S)}

for path in xaml_files:
    source = path.read_text(encoding="utf-8")
    doc = ET.parse(path)
    parents = {child: node for node in doc.iter() for child in node}

    def local(tag):
        return tag.split("}")[-1]

    def inside_template(node):
        while node is not None:
            if local(node.tag) == "ControlTemplate":
                return True
            node = parents.get(node)
        return False

    def inside_datatemplate(node):
        while node is not None:
            if local(node.tag) == "DataTemplate":
                return True
            node = parents.get(node)
        return False

    names = {}
    for node in doc.iter():
        # 1. BeginStoryboard nie ma właściwości TargetName (MC3072) — celowanie
        #    robi Storyboard.TargetName w animacji albo styl samej części szablonu.
        if local(node.tag) == "BeginStoryboard":
            assert "TargetName" not in node.attrib, f"BeginStoryboard has no TargetName property: {path}"
        # 2. Setter TargetName istnieje tylko w szablonie kontrolki.
        if local(node.tag) == "Setter" and "TargetName" in node.attrib:
            assert inside_template(node), f"Setter TargetName outside ControlTemplate: {path}"
        # 3. Style raz: atrybut albo element podrzędny, nigdy oba (MC3003).
        if "Style" in node.attrib and any(local(child.tag) == local(node.tag) + ".Style" for child in node):
            assert False, f"Style set twice (attribute and property element): {path}"
        # 4. Powtórzone x:Name w jednym zakresie nazw (szablon i DataTemplate
        #    mają własny zakres, więc liczy się tylko główny zakres pliku).
        name = node.attrib.get(XAML_NS + "Name")
        if name and not inside_datatemplate(node) and not inside_template(node):
            assert name not in names, f"Duplicate x:Name {name}: {path}"
            names[name] = node
    # 5. StringFormat zaczynające się od „{" wymaga ucieczki „{}" (MC1000).
    for bad in re.findall(r'(?<![A-Za-z])StringFormat=(?:["\'])?\{(?!})[^,}"\']{0,20}', source):
        assert False, f"Unescaped StringFormat '{bad}': {path}"
    # 6. Właściwości dołączone Utilities muszą istnieć (literówka = błąd kompilacji).
    prefixes = re.findall(r'xmlns:(\w+)="clr-namespace:SentinelX\.Utilities"', source)
    for prefix in prefixes:
        for owner, prop in re.findall(rf"\b{prefix}:(\w+)\.(\w+)=", source):
            assert (owner, prop) in attached, f"Unknown attached property {prefix}:{owner}.{prop}: {path}"

project = ET.parse(ROOT / "SENTINEL-X.csproj")
assert project.findtext(".//TargetFramework") == "net9.0-windows"
assert project.findtext(".//UseWindowsForms") != "true"
assert len(list((ROOT / "Views/Pages").glob("*Page.xaml"))) == 12
print("PASS: XML, resources, 12 views, thin code-behind, VM boundaries, target framework, no WinForms flag, XAML pitfalls")
