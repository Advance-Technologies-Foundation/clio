# Test plan: clio-ring companion integration

## Happy path first

1. Publish Ring for `win-x64` to a temporary release directory.
2. Serve a generated manifest and ZIP from a local HTTP fixture through an injected downloader.
3. Run install, status, launch probe, update, and uninstall under a temporary local-app-data root.
4. Confirm checksum validation, current pointer, process entry point, and cleanup.

## Regression

- Disabled feature remains hidden/unreachable.
- Existing clio commands and normal build do not load Ring/Avalonia assemblies.
- Reinstall is idempotent; downgrade is refused unless explicitly requested.
- Bad checksum, non-HTTPS production URL, unsupported RID/schema, corrupt ZIP, and ZIP traversal fail safely.
- Uninstall does not touch clio configuration or environments.
- Ring's existing unit suite remains green under the monorepo dependency versions.

## Release checks

- NativeAOT `win-x64` publish succeeds.
- ZIP contains the entry point and runtime assets.
- SHA-256 file matches the ZIP.
- Manifest refers to the exact versioned GitHub asset.
