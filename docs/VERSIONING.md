# Release versioning policy

- `SENTINEL-X.csproj` `<Version>` is the product SemVer authority. `Core/AppConstants.Version`, the Inno Setup fallback, and Android `versionName` must match it; CI checks this with `scripts/check-versions.py`.
- Android `versionCode` is a separate installer sequence. It must be strictly greater than the code in the last published APK.
- `docs/versioning.json` records the last **published** desktop version and Android code, not the current candidate. At this foundation change the last published release remains `0.98.0` / `98`; the unpublished candidate is `1.0.0` / `100`.
- When preparing the next release after a publication, advance both the candidate version and Android code and update the published baseline in the same release-preparation change. Never lower either sequence or relabel an existing published artifact.
- A protocol/API version is independent from the product version. Phone Link remains API v1; make additive changes within v1 and increment its own protocol number for breaking wire changes.

Run `python scripts/check-versions.py` locally before changing release metadata. The Windows, desktop, Android, and release workflows run the same check.
