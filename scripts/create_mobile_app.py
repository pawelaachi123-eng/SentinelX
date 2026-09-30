#!/usr/bin/env python3
"""Create a small, dependency-free Android app from one of Sentinel's reviewed templates."""
from __future__ import annotations

import argparse
import json
import shutil
import sys
import xml.sax.saxutils
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TEMPLATE_ROOT = ROOT / "scripts" / "mobile-app-templates"
TEMPLATES = ("counter", "notes", "checklist")
APP_LABEL_LIMIT = 32


def validate_app_name(value: str) -> str:
    name = value.strip()
    if not name or len(name) > APP_LABEL_LIMIT or not name[0].isalnum():
        raise ValueError("App name must start with a letter/number and contain 1–32 characters.")
    if any(not (character.isalnum() or character in " _-") for character in name):
        raise ValueError("App name may contain only letters, numbers, spaces, hyphens, and underscores.")
    return name


def create_app(template: str, app_name: str, output: Path) -> Path:
    if template not in TEMPLATES:
        raise ValueError(f"Unsupported template: {template}")
    label = validate_app_name(app_name)
    destination = output.expanduser().resolve()
    if destination.exists() and (not destination.is_dir() or any(destination.iterdir())):
        raise FileExistsError("Output directory must be empty; existing files will not be overwritten.")

    base = TEMPLATE_ROOT / "base"
    activity = TEMPLATE_ROOT / template / "MainActivity.kt"
    if not base.is_dir() or not activity.is_file() or activity.is_symlink():
        raise FileNotFoundError(f"Reviewed source template is missing: {template}")

    destination.mkdir(parents=True, exist_ok=True)
    for item in base.rglob("*"):
        if item.is_symlink():
            raise ValueError("Template source must not contain symlinks.")
        if item.is_dir():
            (destination / item.relative_to(base)).mkdir(parents=True, exist_ok=True)
        elif item.is_file():
            relative = item.relative_to(base)
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(item, target)

    gradle_file = destination / "app" / "build.gradle.kts"
    gradle_text = gradle_file.read_text(encoding="utf-8").replace("com.sentinelx.generated.__TEMPLATE__", f"com.sentinelx.generated.{template}")
    gradle_file.write_text(gradle_text, encoding="utf-8")
    activity_target = destination / "app" / "src" / "main" / "java" / "com" / "sentinelx" / "generated" / "MainActivity.kt"
    activity_target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(activity, activity_target)
    strings = destination / "app" / "src" / "main" / "res" / "values" / "strings.xml"
    strings.parent.mkdir(parents=True, exist_ok=True)
    strings.write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<resources>\n'
        f'    <string name="app_name">{xml.sax.saxutils.escape(label)}</string>\n'
        '</resources>\n',
        encoding="utf-8",
    )
    metadata = {
        "template": template,
        "appName": label,
        "applicationId": f"com.sentinelx.generated.{template}",
        "minSdk": 23,
        "compileSdk": 35,
        "networkPermission": False,
        "buildCommand": "gradle --no-daemon :app:assembleDebug",
        "signing": "Gradle debug key; local testing only, not Play Store release signing",
    }
    (destination / "sentinel-app.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    (destination / "README.md").write_text(
        f"# {label}\n\n"
        f"Generated from Sentinel's reviewed `{template}` Android template.\n\n"
        "Build with Java 17, Android SDK 35, Gradle 8.9, and `gradle --no-daemon :app:assembleDebug`.\n"
        "The debug APK is for local testing only; it is not Play-Store signed. The app requests no Internet permission.\n",
        encoding="utf-8",
    )
    return destination


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--template", required=True, choices=TEMPLATES)
    parser.add_argument("--app-name", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        path = create_app(args.template, args.app_name, args.output)
    except (ValueError, OSError) as error:
        print(f"Generation failed: {error}", file=sys.stderr)
        return 2
    print(f"Generated Android source project: {path}")
    print(f"Template: {args.template}; app label: {validate_app_name(args.app_name)}")
    print("No compiler or process was run. Build with Gradle or the documented GitHub Actions workflow.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
