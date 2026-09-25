#!/usr/bin/env python3
"""Fast portable checks, complementary to (not a substitute for) the Windows build."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
XAML_NS = "{http://schemas.microsoft.com/winfx/2006/xaml}"
P_NS = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
GEOMETRY = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}
NUMBER = re.compile(r"-?\d*\.?\d+(?:[eE][-+]?\d+)?")


def check_geometry(path: Path) -> None:
    """`Figures` is parsed by PathFigureCollection.Parse, which is *not* the Path.Data mini-language:
    the F0/F1 fill-rule token is rejected there, and a short argument list throws while the window is
    being loaded — an exception at startup, not at build. Both are cheap to catch here."""
    for node in ET.parse(path).iter():
        figures = node.attrib.get("Figures")
        if not figures:
            continue
        assert not re.match(r"F[01]\b", figures.strip()), f"Figures cannot carry the F0/F1 fill-rule token (use Path.Data): {path}"
        tokens = re.findall(r"[A-Za-z]|[^A-Za-z\s,]+", figures)
        index = 0
        while index < len(tokens):
            token = tokens[index]
            assert token.isalpha(), f"Number {token!r} before any command: {path}"
            command = token.upper()
            assert command in GEOMETRY, f"Unknown geometry command {command!r}: {path}"
            index += 1
            width = GEOMETRY[command]
            while width:
                args = tokens[index : index + width]
                assert len(args) == width and all(NUMBER.fullmatch(a) for a in args), \
                    f"{command} expects {width} numbers, found {args}: {path}"
                index += width
                if index >= len(tokens) or tokens[index].isalpha():
                    break


def check_trigger_names(path: Path) -> None:
    """Names in storyboards resolve against the *template* name scope, and WPF does not grant that
    scope to an `EventTrigger` declared in `ControlTemplate.Triggers` (nor to triggers inside a
    Style, which has no template parts at all). The damage only shows when the trigger fires:
    "name cannot be found in the name scope of ControlTemplate", thrown from the dispatcher.
    Animate state changes with `Trigger.EnterActions` / `ExitActions` inside `ControlTemplate.Triggers`."""
    addresses = ("Storyboard.TargetName", "TargetName", "SourceName")
    for node in ET.parse(path).iter():
        if node.tag.endswith("}Style"):
            triggers = node.find(P_NS + "Style.Triggers")
            for offender in ([] if triggers is None else [t for t in triggers.iter() if any(a in t.attrib for a in addresses)]):
                raise AssertionError(f"Style trigger addresses a template part by name: {path}")
        if node.tag.endswith("}ControlTemplate"):
            triggers = node.find(P_NS + "ControlTemplate.Triggers")
            if triggers is None:
                continue
            for trigger in triggers:
                if trigger.tag == P_NS + "EventTrigger" and any(a in element.attrib for element in trigger.iter() for a in addresses):
                    raise AssertionError(f"EventTrigger in ControlTemplate.Triggers cannot resolve TargetName: {path}")


keys = set()
files = list((ROOT / "Themes").glob("*.xaml")) + [ROOT / "App.xaml"]
for path in files:
    check_geometry(path)
    check_trigger_names(path)
    doc = ET.parse(path)
    local = set()
    for node in doc.iter():
        key = node.attrib.get(XAML_NS + "Key")
        if key:
            assert key not in local, f"Duplicate key {key}: {path}"
            local.add(key)
    keys |= local

for path in (ROOT / "Views").rglob("*.xaml"):
    check_geometry(path)
    check_trigger_names(path)
    document = ET.parse(path)
    for node in document.iter():
        if node.tag.endswith("}ProgressBar") and node.attrib.get("Value", "").startswith("{Binding"):
            assert "Mode=OneWay" in node.attrib["Value"], f"Read-only metrics need OneWay: {path}"
    source = path.read_text(encoding="utf-8")
    for resource in re.findall(r"\{(?:Static|Dynamic)Resource (Sx\w+|BoolToVisibility|StatusBrush|VoiceBrush|RiskBrush|ReadinessBrush)\}", source):
        assert resource in keys, f"Unknown resource {resource}: {path}"
    assert not re.search(r'="#[0-9a-fA-F]{6,8}"', source), f"Hard-coded view color: {path}"
    behind = Path(str(path) + ".cs")
    assert behind.exists(), f"Missing code-behind: {path}"
    assert len(behind.read_text(encoding="utf-8").splitlines()) < 20, f"Non-trivial code-behind: {behind}"

for path in (ROOT / "ViewModels").glob("*.cs"):
    source = path.read_text(encoding="utf-8")
    for forbidden in ("Process.Start(", "File.Read", "File.Write", "Registry.Current", "ManagementObjectSearcher"):
        assert forbidden not in source, f"System operation {forbidden} in {path}"

project = ET.parse(ROOT / "SENTINEL-X.csproj")
assert project.findtext(".//TargetFramework") == "net9.0-windows"
assert project.findtext(".//UseWindowsForms") != "true"
assert len(list((ROOT / "Views/Pages").glob("*Page.xaml"))) == 12
print("PASS: XML, resources, icon geometry, trigger name scopes, 12 views, thin code-behind, VM boundaries, target framework, no WinForms flag")
