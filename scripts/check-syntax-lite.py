#!/usr/bin/env python3
"""Fast, dependency-free sanity check for C# sources — a safety net, NOT a compiler.

It catches the mistakes that waste a whole Windows CI run: unbalanced (), [], {} (outside strings and
comments), unterminated strings/comments, a file moved without its types, two files in the same folder
declaring the same top-level type, and a tool listed in the catalogue that the toolbox does not handle.
"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
SKIP = {".git", "bin", "obj", "node_modules", "phone-android", ".github"}
PAIRS = {")": "(", "]": "[", "}": "{"}


def scan(source: str, path: Path) -> list[str]:
    errors: list[str] = []
    stack: list[tuple[str, int]] = []
    i, line, n = 0, 1, len(source)

    def fail(message: str) -> None:
        errors.append(f"{path.relative_to(ROOT)}:{line}: {message}")

    while i < n:
        c = source[i]
        two = source[i:i + 2]
        if c == "\n":
            line += 1
            i += 1
        elif two == "//":
            while i < n and source[i] != "\n":
                i += 1
        elif two == "/*":
            end = source.find("*/", i + 2)
            if end < 0:
                fail("unterminated /* comment")
                return errors
            line += source.count("\n", i, end)
            i = end + 2
        elif source.startswith('"""', i) or re.match(r'\$+"""', source[i:i + 8] or ""):
            m = re.match(r'(\$*)("{3,})', source[i:])
            quotes = m.group(2)
            end = source.find(quotes, i + len(m.group(0)))
            if end < 0:
                fail("unterminated raw string literal")
                return errors
            line += source.count("\n", i, end)
            i = end + len(quotes)
        elif c == "@" and source.startswith('@"', i) or source.startswith('$@"', i) or source.startswith('@$"', i):
            j = source.index('"', i) + 1
            while j < n:
                if source[j] == '"':
                    if source[j:j + 2] == '""':
                        j += 2
                        continue
                    break
                if source[j] == "\n":
                    line += 1
                j += 1
            else:
                fail("unterminated verbatim string")
                return errors
            i = j + 1
        elif c == '"' or source.startswith('$"', i):
            j = i + (2 if c == "$" else 1)
            interpolated = c == "$"
            depth = 0
            while j < n:
                d = source[j]
                if d == "\\" and not depth:
                    j += 2
                    continue
                if d == "\n" and not depth:
                    fail("string literal runs over the end of the line")
                    return errors
                if interpolated:
                    if d == "{":
                        if source[j:j + 2] == "{{" and not depth:
                            j += 2
                            continue
                        depth += 1
                    elif d == "}":
                        if source[j:j + 2] == "}}" and not depth:
                            j += 2
                            continue
                        depth = max(0, depth - 1)
                if d == '"' and not depth:
                    break
                j += 1
            else:
                fail("unterminated string literal")
                return errors
            i = j + 1
        elif c == "'":
            m = re.match(r"'(?:\\u[0-9a-fA-F]{4}|\\x[0-9a-fA-F]{1,4}|\\.|[^'\\\n])'", source[i:i + 12])
            if not m:
                fail("malformed character literal")
                return errors
            i += len(m.group(0))
        else:
            if c in "([{":
                stack.append((c, line))
            elif c in ")]}":
                if not stack or stack[-1][0] != PAIRS[c]:
                    opener = stack[-1] if stack else ("nothing", 0)
                    fail(f"'{c}' does not match {opener[0]!r} opened at line {opener[1]}")
                    return errors
                stack.pop()
            i += 1
    for opener, opened in stack:
        errors.append(f"{path.relative_to(ROOT)}:{opened}: '{opener}' is never closed")
    return errors


def sources():
    for path in sorted(ROOT.rglob("*.cs")):
        if not SKIP.intersection(path.relative_to(ROOT).parts):
            yield path


def main() -> int:
    errors: list[str] = []
    declared: dict[tuple[str, str], Path] = {}
    count = 0
    for path in sources():
        text = path.read_text(encoding="utf-8-sig")
        count += 1
        errors += scan(text, path)
        for match in re.finditer(r"^(?:public |internal )?(?:static |sealed |abstract |partial |readonly )*(?:class|record|struct|interface|enum) (\w+)", text, re.M):
            if "partial" in match.group(0):
                continue
            key = (path.parent.as_posix(), match.group(1))
            if key in declared and declared[key] != path:
                errors.append(f"{path.relative_to(ROOT)}: type {match.group(1)} is also declared in {declared[key].relative_to(ROOT)}")
            declared[key] = path

    # A moved/renamed file must not leave the source tree with a stale copy of the same file name.
    names: dict[str, list[Path]] = {}
    for path in sources():
        names.setdefault(path.name, []).append(path)
    for name, paths in names.items():
        if name in {"AssemblyInfo.cs", "MainWindow.xaml.cs"}:
            continue
        # The WinUI shell is a separate project with deliberate counterparts of a few
        # WPF files (App, ServiceLocator, IDesktopService, CommandPalette) — a shared
        # name only matters within the same project.
        groups: dict[bool, list] = {}
        for path in paths:
            groups.setdefault("SentinelX.WinUI" in path.relative_to(ROOT).parts, []).append(path)
        for same in groups.values():
            if len(same) > 1:
                errors.append(f"duplicate file name {name}: " + ", ".join(str(p.relative_to(ROOT)) for p in same))

    # Every tool of the Forge category must have a routing branch in Tools/ForgeTools.cs.
    catalog = (ROOT / "Core/ToolCatalog.cs").read_text(encoding="utf-8")
    forge = (ROOT / "Tools/ForgeTools.cs").read_text(encoding="utf-8")
    for preview in re.findall(r'"Kuźnia 0\.96", "[^"]+", "(?:[^"\\]|\\.)*",\s*"([^"]+)", true', catalog):
        stem = re.sub(r":$", "", preview)
        if stem not in forge:
            errors.append(f"Core/ToolCatalog.cs: Forge tool '{preview}' has no routing branch in Tools/ForgeTools.cs")

    if errors:
        print("FAIL:")
        print("\n".join(errors))
        return 1
    print(f"PASS: {count} C# files — balanced brackets, strings and comments, no duplicate types or file names, Forge tools routed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
