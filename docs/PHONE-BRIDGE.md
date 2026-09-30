# Sentinel Phone + Bridge — current implementation

This is an **opt-in local-network companion**, not a cloud remote-control service. The PC listener is off by default. In the main Sentinel window, open **Ustawienia → Sentinel Bridge · Telefon ↔ PC** and start it explicitly. It binds only to the PC's individual RFC1918 IPv4 addresses on TCP port `43179`—never `0.0.0.0` or a public address—and does not add a firewall rule, router port-forward, VPN, proxy, or cloud relay. Do not expose or port-forward this port.

## Pairing

1. Start Bridge on the PC. Its settings card shows private IP endpoint(s), a one-time 12-character code (10-minute lifetime), and the SHA-256 fingerprint of a persistent self-signed TLS certificate.
2. Open `phone-android/` in Android Studio. Enter the exact private IPv4, port, fingerprint, one-time code, and a phone label; select **Sparuj przez TLS**. Verify the fingerprint against the PC display before pairing.
3. The PC accepts at most five phones; a pairing code is single-use, attempts are rate-limited, and the PC stores only a SHA-256 hash of each random 256-bit phone token. The phone stores its token encrypted with a non-exportable Android Keystore AES-GCM key. PC token metadata and its TLS private key are protected with current-user Windows DPAPI.
4. Revoke every paired phone using the explicit confirmation button on the PC. If a phone is lost, revoke promptly. Clearing the Android app's local key alone does not revoke the server token.

The TLS protocol uses certificate pinning, per-request fresh nonces, a 120-second clock window, bounded JSON messages, eight simultaneous connections and 30 authenticated commands/minute/device. Non-private source addresses are rejected. These controls reduce exposure; they do not protect a compromised PC/phone, malicious paired client, or hostile local administrator.

## Skills currently wired

- `PC_STATUS`: on-demand CPU/RAM and GPU utilization readouts. No background polling.
- `PC_LAUNCH_APP`: only Sentinel's known-app allowlist; Sentinel's local-network policy still applies.
- `PC_LOCK`: Windows lock request, returned as **UNVERIFIED** because the bridge cannot read back the lock screen.
- `PC_DOWNLOAD_STATUS`: last stable download filename, if the existing in-process watcher observed one.
- `TVControlSkill`: requires an already configured official Home Assistant integration and an explicit paired entity alias. It supports power, mute, volume, HDMI 1, YouTube, and brightness for supported `light` entities. Unsupported, ambiguous, or unverified results are returned honestly.
- `PC_GPU_TEMP` explicitly reports unavailable; the current monitor has no trustworthy GPU-temperature sensor.
- Skills declare names, intents, allowed parameters and permission metadata in `PhoneSkillRegistry`. The executor has no shell, arbitrary path, arbitrary script or administrator endpoint. Skill results distinguish `SUCCESS`, `UNVERIFIED`, `FAILED`, `PARTIAL`, and `UNSUPPORTED`; phone-initiated actions are recorded in the existing privacy-gated action history without pairing tokens.

### User-defined macros

Create `%LOCALAPPDATA%\\SentinelX\\PhoneBridge\\macros.json` (or the active `AppPaths.Root\\PhoneBridge\\macros.json`) to define exact phrase aliases and short sequences of pre-approved reversible smart-device actions and allowlisted app launches. The file is read-only from the remote endpoint; it is not executable code. Limits: 20 macros, 8 phrases/macro, 5 steps/macro, 32 KiB total. Before the first action, every step's intent, parameter schema/value bounds, app allowlist entry, and paired-entity type are checked; dynamic Home Assistant state (for example, whether an input exists) is checked immediately before each individual action. Execution stops on the first non-verified result, and prior steps are not rolled back.

Example (edit aliases/levels to match devices you explicitly paired):

```json
{
  "macros": [
    {
      "name": "Film",
      "phrases": ["tryb film", "movie mode"],
      "steps": [
        { "intent": "TV_POWER_ON", "parameters": { "target": "tv" } },
        { "intent": "TV_HDMI_1", "parameters": { "target": "tv" } },
        { "intent": "TV_VOLUME_SET", "parameters": { "target": "tv", "value": "25" } },
        { "intent": "LIGHT_SET_BRIGHTNESS", "parameters": { "target": "salon", "value": "20" } }
      ]
    }
  ]
}
```

The Android command `tryb film` is sent as text over the pinned local TLS channel and matched **exactly** against the user's configured phrases; no LLM is used. Only listed safe intents can appear in a macro. There is not yet a macro editor in the UI.

## Android companion behavior

The lightweight Android project has manual pairing, an explicit speech button, natural-language mappings for selected PC/TV intents, a home-screen widget that opens the app to run a shortcut, and optional Wake-on-LAN after the user enters a PC MAC. The WOL packet is sent only to the local IPv4 broadcast; delivery is not proof the PC woke. Wake-on-LAN must first be enabled in firmware, network adapter settings and the network. The app asks Android's speech-recognition provider for offline recognition, but Android/provider availability controls whether recognition is actually offline; it does not listen for a background wake word. Phone IR hardware is checked through Android's `ConsumerIrManager`; no IR codes are sent because there is no configured device profile or verified code set.

## Not implemented yet

This is not full phone control. Screenshot capture/delivery, PC↔phone file transfer, remote close/shutdown/restart, user-configurable Wake-on-LAN confirmation/verification, GPU temperature, channel navigation, TV auto-discovery beyond the configured Home Assistant instance, IR profile learning, arbitrary custom skill scripts, and an editable macro UI are not available. Close/shutdown/restart commands are refused rather than executed without the required local approval. No IR codes are guessed. iOS support is not included. The Android app has source but has not been built into an APK; the Windows bridge and regression suite have not been compiled or run in this environment because the .NET SDK is unavailable.
