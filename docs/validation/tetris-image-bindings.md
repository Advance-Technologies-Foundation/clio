# Tetris image-binding repair validation

Date: 2026-10-09. Scope: source repair in `krylov/tetris-image-bindings`, based on project POC commit `35d74accd3a28242610965f3b6db2ef59050dbcc`. No package publication, cluster deployment, existing application mutation, or cross-environment installation was performed.

## Intent and implementation

Image bindings must preserve content bytes and reference identities using native Creatio metadata. The existing runtime map now maps Blob 13 to Binary, Image 14 to Image, and ImageLookup 16 to ImageLookup. The existing workspace file converter accepts both Binary and Image content, preserving Blob path encoding. The reference payload and display-value behavior stay on the existing path.

Regression coverage verifies native descriptors and decoded SVG bytes for Blob/Image, ImageLookup IDs/display values, DB-first create plus section-reference upsert wire metadata, and the MCP wrapper's runtime Image descriptor. Unsupported-column tests now use Collection 17 instead of mislabeling ImageLookup 16 as an unsupported Blob. An MCP E2E test was added for real runtime SysImage metadata and local artifact bytes; it requires a configured environment and was compiled but not executed in this source-only task.

CLI help, command docs/index, MCP descriptions and prompts explain content versus references. Existing incorrectly typed artifacts require regeneration with all intended rows and localizations; this source fix does not rewrite them automatically.

## Checks

- Initial focused binding command run: 56 passed.
- Initial Command, ProcessModel and McpServer module run: 11,569 passed, 15 skipped.
- Final Command, ProcessModel and McpServer module run, including the explicit upsert regression: **11,570 passed, 15 skipped, 0 failed** (1 minute 35 seconds).
- `git diff --check`: passed.
- Existing unrelated build warnings include CS9107 in BusinessRuleTool/RestartTool, CLIO001 in MobileDiffApplyValidator, and older test warnings. No new diagnostic was observed in the changed files.

Module command:

```powershell
dotnet test clio.tests/clio.tests.csproj --filter 'TestCategory=Unit&(Module=Command|Module=ProcessModel|Module=McpServer)' --no-restore --logger 'console;verbosity=minimal'
```

ClioRing compatibility reviewed, no Ring-consumed contract changed: searches of `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, and `clio-ring/ClioRing.Desktop/actions.json` found no binding-tool consumption. No tool names, arguments, envelopes, or stage events changed.

## Coordinated knowledge change

Knowledge branch `krylov/tetris-image-guidance`, commit `094f247bb723f238db32735fc79655b7898ab4fc`, adds an image/FSM branch and candidate bundle version 1.15.111. Its producer suite passed 282 tests:

```powershell
dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj -c Release --no-build
```

The candidate remains unpublished. Guidance identifies the observed defective release and immutable source evidence, distinguishes local and DB-first payloads, and does not claim a fixed released Clio version or verified cross-environment portability.

## Workspace-force investigation

No MCP `force` option was added. `WorkspaceCreator.Create` calls template copying with directory overwrite disabled, but `FileSystem.CopyDirectory` still overwrites matching files; an existing runner AGENTS.md is therefore not preserved. The runner integration owner was told to initialize before its scaffold, or create a temporary empty workspace and copy only missing files for partial recovery. This avoids expanding this binding repair into filesystem semantics.

## Remaining acceptance and KISS check

Build/release availability, owned-target runtime E2E, regeneration of defective retained artifacts, and second-environment byte/reference readback belong to the later integration acceptance. This patch uses the existing type map, converter and binding tools; it adds no new protocol, deployment path or recovery service.
