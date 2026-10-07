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
assert project.findtext(".//TargetFramework") == "net10.0-windows"
assert project.findtext(".//UseWindowsForms") != "true"
assert len(list((ROOT / "Views/Pages").glob("*Page.xaml"))) == 15
assert (ROOT / "Views/Pages/BasePage.xaml").exists(), "Missing Base UI"
# 0.96 · KUŹNIA: the logic is split by responsibility, and the "brain" and the tools never touch WPF.
assert not (ROOT / "CommandRouter.cs").exists(), "CommandRouter.cs belongs in Brain/Router/"
for required in ("Brain/Router/CommandRouter.cs", "Brain/Router/DecisionPreview.cs", "Tools/UtilityToolbox.cs", "Tools/ForgeTools.cs", "Testing/UiSmokeTestRunner.cs"):
    assert (ROOT / required).exists(), f"Missing {required}"
for folder in ("Brain", "Tools"):
    for path in (ROOT / folder).rglob("*.cs"):
        assert "using System.Windows" not in path.read_text(encoding="utf-8-sig"), f"WPF in {folder}/: {path}"
print("PASS: XML, resources, 15 views, thin code-behind, VM boundaries, target framework, no WinForms flag, Brain/Tools layout without WPF")
