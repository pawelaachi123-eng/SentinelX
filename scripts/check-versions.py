#!/usr/bin/env python3
"""Fail CI when release/runtime/Android versions drift or move backwards."""
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def parse_version(value: str, label: str) -> tuple[int, int, int]:
    if not re.fullmatch(r"\d+\.\d+\.\d+", value):
        fail(f"{label} must be numeric SemVer (major.minor.patch), got {value!r}")
    return tuple(int(part) for part in value.split("."))  # type: ignore[return-value]


try:
    project = ET.parse(ROOT / "SENTINEL-X.csproj")
except (ET.ParseError, OSError) as exc:
    fail(f"cannot read SENTINEL-X.csproj: {exc}")
versions = [element.text.strip() for element in project.iter("Version") if element.text and element.text.strip()]
if len(versions) != 1:
    fail(f"expected exactly one <Version> in SENTINEL-X.csproj, found {len(versions)}")
version = versions[0]
current_version_tuple = parse_version(version, "SENTINEL-X.csproj <Version>")

try:
    policy = json.loads((ROOT / "docs/versioning.json").read_text(encoding="utf-8"))
    last_published = policy["lastPublishedVersion"]
    last_android_code = int(policy["lastPublishedAndroidVersionCode"])
except (OSError, json.JSONDecodeError, KeyError, TypeError, ValueError) as exc:
    fail(f"cannot read docs/versioning.json release baseline: {exc}")
last_version_tuple = parse_version(last_published, "lastPublishedVersion")
if current_version_tuple <= last_version_tuple:
    fail(f"current version {version} must be newer than last published {last_published}")

app_constants = (ROOT / "Core/AppConstants.cs").read_text(encoding="utf-8")
match = re.search(r'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"\s*;', app_constants)
if not match:
    fail("Core/AppConstants.cs does not declare AppConstants.Version")
if match.group(1) != version:
    fail(f"AppConstants.Version is {match.group(1)!r}, but SENTINEL-X.csproj is {version!r}")

gradle = (ROOT / "phone-android/app/build.gradle").read_text(encoding="utf-8")
code_match = re.search(r"(?m)^\s*versionCode\s+(\d+)\s*$", gradle)
if not code_match:
    fail("phone-android/app/build.gradle must declare an integer versionCode")
version_code = int(code_match.group(1))
if version_code <= last_android_code:
    fail(f"Android versionCode {version_code} must exceed last published code {last_android_code}")
if not re.search(r"(?m)^\s*versionName\s+desktopVersionName\s*$", gradle):
    fail("Android versionName must derive from the desktop project version")
if "rootProject.file('../SENTINEL-X.csproj')" not in gradle or "<Version>([^<]+)" not in gradle:
    fail("Gradle must read versionName from SENTINEL-X.csproj")

installer = (ROOT / "installer/SentinelX.iss").read_text(encoding="utf-8")
installer_match = re.search(r'(?m)^\s*#define\s+AppVersion\s+"([^"]+)"\s*$', installer)
if not installer_match or installer_match.group(1) != version:
    fail("Inno Setup fallback AppVersion must match SENTINEL-X.csproj (CI may still override it explicitly)")

print(
    f"PASS: desktop/runtime/installer/Android versionName = {version}; "
    f"Android versionCode = {version_code} (> published {last_android_code})"
)
