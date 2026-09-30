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

# ---------------------------------------------------------------------------
# 7. Kolejność zasobów (0.93 · NOWOCZESNE GUI). StaticResource widzi wyłącznie
#    to, co już istnieje w chwili parsowania: słowniki scalone PÓŹNIEJ w App.xaml
#    oraz klucze wpisane PONIŻEJ miejsca użycia w tym samym pliku są niewidoczne.
#    Efekt to XamlParseException podczas ładowania App.xaml — czyli PRZED
#    OnStartup, więc smoke nie zdąży utworzyć katalogu wyników ani zapisać
#    FAILED.txt (przebieg CI 36235263621: pusty krok, brak artefaktów).
#    Duplikat stylu domyślnego (Style bez x:Key, ten sam TargetType w jednym
#    słowniku) = ArgumentException „Item has already been added" przy ładowaniu.
# ---------------------------------------------------------------------------
def local_name(tag):
    return tag.split("}")[-1]


def implicit_key(node):
    """Klucz stylu domyślnego: typeof(TargetType), czyli „implicit:Border”."""
    if local_name(node.tag) != "Style" or node.attrib.get(XAML_NS + "Key"):
        return None
    target = node.attrib.get("TargetType", "")
    return "implicit:" + target.replace("{x:Type ", "").replace("}", "").split(":")[-1] if target else None


def ordered_resources(path, available):
    """Dokłada klucze z `path` w kolejności dokumentu, sprawdzając StaticResource."""
    root = ET.parse(path).getroot()
    parents = {child: node for node in root.iter() for child in node}
    local_implicit = set()
    seen = set(available)
    for node in root.iter():
        for attribute, value in node.attrib.items():
            if not isinstance(value, str):
                continue
            for key in re.findall(r"\{StaticResource ([^{}]+)\}", value):
                assert key.strip() in seen, f"StaticResource {{{key.strip()}}} used before it is available: {path}"
            for target in re.findall(r"\{StaticResource \{x:Type ([\w:.]+)\}\}", value):
                assert "implicit:" + target.split(":")[-1] in seen, f"Default style for {target} used before it is available: {path}"
            if local_name(attribute) == "StaticResource":
                assert value in seen, f"StaticResource {value} used before it is available: {path}"
        key = node.attrib.get(XAML_NS + "Key")
        if key:
            seen.add(key)
        implicit = implicit_key(node)
        if implicit and node in parents and local_name(parents[node].tag) == "ResourceDictionary":
            # Cień między plikami jest legalny (późniejszy słownik wygrywa),
            # duplikat w TYM SAMYM słowniku kończy się ArgumentException.
            assert implicit not in local_implicit, f"Duplicate default style {implicit}: {path}"
            local_implicit.add(implicit)
            seen.add(implicit)
    return seen


merge_order = [Path(source).stem for source in
               re.findall(r'Source="Themes/([^"]+)"', (ROOT / "App.xaml").read_text(encoding="utf-8"))]
assert merge_order, "App.xaml must merge Themes dictionaries"
application_keys = set()
for theme in merge_order:
    application_keys = ordered_resources(ROOT / "Themes" / f"{theme}.xaml", application_keys)
application_keys |= {node.attrib[XAML_NS + "Key"] for node in ET.parse(ROOT / "App.xaml").iter()
                     if node.attrib.get(XAML_NS + "Key")}
for path in (ROOT / "Views").rglob("*.xaml"):
    ordered_resources(path, application_keys)

# ---------------------------------------------------------------------------
# 8. Kształt transformacji animowanych elementów (0.93). Storyboard adresuje
#    „(TransformGroup.Children)[N].(Typ.Właściwość)", więc animowany element musi
#    mieć RenderTransform z dokładnie tym dzieckiem pod tym indeksem. Bez tego
#    Begin rzuca InvalidOperationException („Cannot resolve all property references")
#    w środku uchwytu Loaded/MouseEnter — jako fault dyspozytora, nie błąd wiązania.
#    Konwencja: [0] ScaleTransform, [1] TranslateTransform, [2] RotateTransform.
# ---------------------------------------------------------------------------
def storyboard_shapes(dictionary):
    shapes = {}
    for board in ET.parse(dictionary).getroot():
        key = board.attrib.get(XAML_NS + "Key")
        if not key or local_name(board.tag) != "Storyboard":
            continue
        need = set()
        for node in board.iter():
            for index, kind in re.findall(r"Children\)\[(\d+)\]\.\((\w+)\.",
                                          node.attrib.get("Storyboard.TargetProperty", "")):
                need.add((int(index), kind))
        shapes[key] = need
    return shapes


def transform_shape(node):
    """Dzieci TransformGroup z RenderTransform: setter stylu albo element szablonu."""
    for group in node.iter(WPF_NS + "TransformGroup"):
        return [local_name(child.tag) for child in group]
    return []


shapes = storyboard_shapes(ROOT / "Themes" / "Animations.xaml")
for path in xaml_files:
    doc = ET.parse(path)
    parents = {child: node for node in doc.iter() for child in node}
    for begin in doc.iter(WPF_NS + "BeginStoryboard"):
        match = re.match(r"\{StaticResource (\w+)\}", begin.attrib.get("Storyboard", ""))
        need = shapes.get(match.group(1)) if match else None
        if not need:
            continue
        # Animowany element wyznacza najbliższy styl: styl kontrolki albo styl części szablonu.
        node = parents.get(begin)
        while node is not None and local_name(node.tag) != "Style":
            node = parents.get(node)
        assert node is not None, f"BeginStoryboard outside any Style: {path}"
        shape = transform_shape(node)
        owner = parents.get(node)
        if not shape and owner is not None and local_name(owner.tag).endswith(".Style"):
            shape = transform_shape(parents[owner])          # <Część.Style> → transform części
        if not shape:                                        # ControlTemplate.Triggers → kontrolka
            scope = owner
            while scope is not None and local_name(scope.tag) != "ControlTemplate":
                scope = parents.get(scope)
            holder = parents.get(scope) if scope is not None else None
            while holder is not None and local_name(holder.tag) != "Style":
                holder = parents.get(holder)
            if holder is not None:
                shape = transform_shape(holder)
        for index, kind in sorted(need):
            assert index < len(shape) and shape[index] == kind, (
                f"{match.group(1)} needs Children[{index}]={kind}, element styled in {path} has {shape or 'no RenderTransform'}")

# Storyboardy grane z kodu (Utilities/Motion.cs) muszą wystarczyć konwencją
# TransformGroup[Scale, Translate], którą buduje Motion.EnsureTransform.
code_boards = set()
for utility in (ROOT / "Utilities").glob("*.cs"):
    code_boards |= set(re.findall(r'"(Sx\w+)"', utility.read_text(encoding="utf-8")))
for key in sorted(code_boards & set(shapes)):
    assert all(index < 2 for index, _ in shapes[key]), f"{key} is played from code but needs a transform Motion does not build"

project = ET.parse(ROOT / "SENTINEL-X.csproj")
assert project.findtext(".//TargetFramework") == "net9.0-windows"
assert project.findtext(".//UseWindowsForms") != "true"
assert len(list((ROOT / "Views/Pages").glob("*Page.xaml"))) == 13
print("PASS: XML, resources, resource order, animation paths, 13 views, thin code-behind, VM boundaries, target framework, no WinForms flag, XAML pitfalls")
