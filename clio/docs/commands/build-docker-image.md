# Build Docker Image

## Purpose

`build-docker-image` builds a Docker image for a Creatio `.NET 8+` distribution from either a ZIP archive or an already extracted application directory. It supports bundled `dev` and `prod` templates, or a custom template directory, and can optionally save the image to a tar file and push it to a registry.

`.NET Framework` distributions are permanently unsupported for this command.

## Usage

```bash
clio build-docker-image --from <zip-or-folder> --template <name-or-path> [options]
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--from` | Yes | Path to a Creatio ZIP archive or extracted application directory |
| `--template` | Yes | Bundled template name (`dev`, `prod`) or custom template directory path |
| `--output-path` | No | Optional tar file path used by `docker save` |
| `--registry` | No | Optional registry or repository prefix used by `docker push` |

## Supported Sources

### ZIP archive

- The archive is extracted into a temporary folder before build.
- The command then detects the actual application root inside the extracted content.

### Extracted directory

- The directory is used directly.
- If the provided folder is a wrapper directory, the command searches within it for the Creatio application root.

## Runtime Validation

The command accepts only Creatio `.NET 8+` distributions.

Expected `.NET 8+` markers:
- `Terrasoft.WebHost.dll`
- `Terrasoft.WebHost.dll.config`

If the source looks like a `.NET Framework` deployment, for example `Web.config` exists without `Terrasoft.WebHost.dll`, the command fails before invoking Docker.

## Image Naming

The image reference is generated automatically.

- Repository: `creatio-<template-name>`
- Tag: sanitized source archive or directory name
- OCI label: `org.creatio.database-source=<original source archive-or-directory name>`

Examples:
- Source `8.3.3_StudioNet8.zip` with template `dev` becomes `creatio-dev:8.3.3_studionet8`
- Source folder `release-build` with template `prod` becomes `creatio-prod:release-build`

When `--registry` is provided, clio tags and pushes:

```text
<registry>/creatio-<template-name>:<tag>
```

Example:

```text
ghcr.io/acme/creatio-prod:8.3.3_studionet8
```

## Templates

### Bundled templates

- `dev`: Development-oriented image with supervisor, SSH, and code-server
- `prod`: Production-oriented image that supervises only the Creatio app process and is based on `.NET SDK 8.0` so in-app `dotnet build` workflows remain available

Bundled templates are copied to the local clio settings folder next to `infrastructure`:

- Windows: `%LOCALAPPDATA%\creatio\clio\docker-templates`
- macOS/Linux: `~/.local/creatio/clio/docker-templates`

### Custom templates

Pass a template directory path to `--template`.

Requirements:
- The directory must exist
- The directory must contain a `Dockerfile`

## Build Flow

The command performs these steps:

1. Validate the source path
2. Resolve the bundled or custom template
3. Build a temporary Docker context
4. Run `docker --version`
5. Run `docker build`
6. Optionally run `docker save`
7. Optionally run `docker tag` and `docker push`
8. Clean up temporary files

## Examples

### Build a local development image from a ZIP archive

```bash
clio build-docker-image --from "C:\Creatio\8.3.3_StudioNet8.zip" --template dev
```

### Build a production image from an extracted directory

```bash
clio build-docker-image --from "/opt/builds/creatio-net8" --template prod
```

### Build and export a tar file

```bash
clio build-docker-image \
  --from "/opt/builds/creatio-net8" \
  --template prod \
  --output-path "/tmp/creatio-prod.tar"
```

### Build, export, and push in one run

```bash
clio build-docker-image \
  --from "C:\Creatio\8.3.3_StudioNet8.zip" \
  --template prod \
  --output-path "C:\Images\creatio-prod.tar" \
  --registry "ghcr.io/acme"
```

### Use a custom template directory

```bash
clio build-docker-image \
  --from "/opt/builds/creatio-net8" \
  --template "/workspace/docker-templates/custom-prod"
```

## Output

On success, the command logs:

- Resolved source path
- Resolved template path
- Local image reference
- Optional tar output path
- Optional pushed registry image reference

The built image also includes an OCI label that preserves the original source payload name, for example:

```text
org.creatio.database-source=8.3.4.1671_StudioNet8_Softkey_PostgreSQL_ENU
```

## Common Errors

### Docker is not installed

If clio reports that Docker is not installed or not available in `PATH`, install Docker and verify that the `docker` executable can be started from the same shell.

### Unknown bundled template

If the bundled template name is not found, use one of the shipped names (`dev`, `prod`) or pass a valid custom template directory.

### Custom template missing Dockerfile

Custom templates must contain a `Dockerfile`. Add one before running the command.

### .NET Framework source

If the source looks like a `.NET Framework` Creatio deployment, the command stops immediately. Use a `.NET 8+` distribution instead.

## Related Commands

- [`deploy-creatio`](./deploy-creatio.md) for deploying Creatio locally or to infrastructure
- [`create-k8-files`](./CreateK8FilesCommand.md) for generating infrastructure deployment manifests
- [`deploy-infrastructure`](./DeployInfrastructureCommand.md) for deploying local Kubernetes infrastructure
