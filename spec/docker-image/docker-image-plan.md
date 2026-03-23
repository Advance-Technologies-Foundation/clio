# Build Docker Image Command

## Summary

Implement the feature described in `docker-image.md` as a new CLI-only command. The command builds Docker images for Creatio `.NET 8+` distributions from either a ZIP archive or an extracted folder, using bundled or user-supplied template folders. `.NET Framework` is permanently unsupported. The implementation uses the local `docker` CLI for build, save, and push operations and does not expose this flow through MCP.

## Key Changes

- Add a new CLI verb and command pair for `build-docker-image`.
  - Required inputs: `--from`, `--template`.
  - Optional inputs: `--output-path`, `--registry`.
  - `--template` accepts either a bundled template name (`dev`, `prod`) or a custom template folder path.
  - `--from` accepts either a ZIP archive or an extracted directory.
- Scope the command explicitly to `.NET 8+` Creatio payloads.
  - Validate that the input represents a Docker-image-capable `.NET` Creatio build.
  - Reject `.NET Framework` payloads with a clear unsupported error.
  - Keep template and runtime selection extensible for future `.NET 10+` images.
- Define deterministic image naming for v1.
  - Local image name: `creatio-<template-name>`.
  - Tag: sanitized source archive or directory name.
  - ZIP input uses the archive file name without extension.
  - Directory input uses the directory name.
  - If `--registry` is provided, push `<registry>/creatio-<template-name>:<tag>`.
- Implement a template-backed build pipeline.
  - Add bundled template assets under a compiled template root.
  - Copy bundled templates to `${SettingsRepository.AppSettingsFolderPath}/docker-templates`, next to `infrastructure`.
  - Introduce a docker-template path provider or service to resolve bundled templates, custom template folders, and validation.
  - Template contract for v1: each template folder contains the files needed to build a `.NET 8+` Creatio WebHost image.
- Reuse existing install and deploy patterns for staging.
  - Resolve ZIP versus directory input.
  - Extract ZIPs to a temp folder.
  - Create a temp Docker build context.
  - Copy source payload and template files into that context.
  - Clean up temp folders on success and failure.
  - Execute `docker build`, then optionally `docker save` and `docker push` in the same invocation.
  - Log the generated local image ref, pushed image ref, and tar path when applicable.
- Keep MCP out of scope.
  - Do not add MCP tool, prompt, resource, or MCP tests.
  - Explicitly state in the change summary that MCP was reviewed and intentionally not added for this command.
- Update command documentation.
  - Add `clio/help/en/build-docker-image.txt`.
  - Add `clio/docs/commands/build-docker-image.md`.
  - Add the command to `clio/Commands.md`.
  - Keep text help and markdown docs aligned with the final flags, examples, `.NET 8+` scope, and combined save and push behavior.

## Public Interfaces

- New CLI command:
  - `clio build-docker-image --from <zip-or-folder> --template <name-or-path> [--output-path <tar>] [--registry <repo-prefix>]`
- New internal services and interfaces:
  - Docker template path provider service.
  - Docker image build orchestration service.
  - Optional source staging and context builder service.
  - Optional payload or runtime detector used to reject `.NET Framework` inputs early.

## Test Plan

- Command option parsing and validation.
  - Reject missing source.
  - Reject missing template.
  - Reject unknown bundled template name.
  - Reject non-existent custom template path.
  - Reject missing `docker` executable with a clear error.
- Runtime eligibility.
  - Accept `.NET 8` payloads.
  - Reject `.NET Framework` payloads with an explicit unsupported error.
  - Keep detection resilient for future `.NET 10+` payload naming and layout.
- Source handling.
  - ZIP input extracts and stages correctly.
  - Directory input stages without extraction.
  - Temp extraction and context folders are cleaned up on success and failure.
- Image reference behavior.
  - Local ref is auto-generated from template name plus sanitized source leaf.
  - Registry-qualified ref is generated correctly when `--registry` is provided.
- Docker execution flow.
  - Build only.
  - Build plus save.
  - Build plus push.
  - Build plus save plus push.
  - Fail fast when `docker build` fails and skip later steps.
- Template behavior.
  - Bundled `dev` template resolves correctly.
  - Bundled `prod` template resolves correctly.
  - Custom template path overrides bundled resolution.
- Documentation acceptance.
  - `clio/help/en/build-docker-image.txt` matches implemented behavior.
  - `clio/docs/commands/build-docker-image.md` matches implemented behavior.
  - `clio/Commands.md` includes the new command and states `.NET 8+` only.

## Assumptions And Defaults

- `.NET Framework` is permanently unsupported for this command.
- `.NET 8` is the initial supported runtime, and the structure should allow future `.NET 10+` templates without redesigning the command.
- `--registry` is a registry or repository prefix, not just a hostname.
- Auto-generated image refs are part of the v1 contract, so no explicit `--image` or `--tag` flags are added.
- Bundled templates are named `dev` and `prod` initially.
- The implementation uses the existing file-system and process abstractions rather than introducing a Docker SDK dependency.
