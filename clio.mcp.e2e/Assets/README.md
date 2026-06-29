# Test assets

## `ClioMcpE2EFixture.gz`

A minimal, empty Creatio package used as the default install target by
`InstallApplicationToolE2ETests.InstallApplication_Should_Create_Report_File` when
`McpE2E:Sandbox:ApplicationPackagePath` is not configured (so the success path is
self-contained on any reachable sandbox).

**Provenance — regenerate with clio (no stand needed):**

```bash
clio new-pkg ClioMcpE2EFixture          # scaffolds the package folder + descriptor.json
clio compress ClioMcpE2EFixture -d ClioMcpE2EFixture.gz
```

It contains only the standard package skeleton (`descriptor.json` + empty
`Schemas/Data/Resources/...` placeholders) — no schemas, no data — so installing it is
a fast, side-effect-free smoke of the `install-application` path.

**Integrity:** `sha256 = 9a8226054b7c9f83f57af9d0249614ff0de411d7b5a9b18856de3311c5bc9627`
(verify with `shasum -a 256 ClioMcpE2EFixture.gz`).
