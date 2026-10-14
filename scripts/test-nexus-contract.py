#!/usr/bin/env python3
"""Nexus host source-level contract checks; runtime behavior still needs real Windows/ESP32 tests."""
from pathlib import Path
import sys

src = Path(sys.argv[1]) / 'src'
checks = {
    'lib/api.js': ['isHost()', 'hostRequest("host.api.request"', 'path, method:', 'return r.json()'],
    'hooks/useSentinelEvents.js': ['window.chrome?.webview', 'host?.addEventListener("message", onHost)'],
    'components/CommandDock.jsx': ['sentinel:host-voice', 'if (isHost() || !v.speakReplies', 'hostRequest("command.submit"', 'return; }'],
    'App.js': ['useMicLevel(!isHost()', 'host.voice.result', 'wymaga integracji natywnej', 'useSentinelEvents'],
    'components/panels/BasePanel.jsx': ['base-discover', 'base-pair', 'api.post("/base/pair"', 'isHost()', 'SX4'],
    'components/panels/DevicesPanel.jsx': ['devices-native-pending', 'isHost()', 'SX4 / TLS', 'base.capabilities'],
}
for path, needles in checks.items():
    p = src / path
    if not p.is_file():
        raise SystemExit(f'FAIL: missing {p}')
    s = p.read_text(encoding='utf-8')
    for needle in needles:
        if needle not in s:
            raise SystemExit(f'FAIL: contract missing: {path}: {needle}')
print('PASS: native bridge usage, voice ownership, no fake Base or device API')
