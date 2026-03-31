# Build Docker Image

## Purpose

`build-docker-image` builds a Docker image either from a Creatio `.NET 8+` distribution, from a bundled database-backup payload, or from the bundled standalone `base` template. It supports bundled `base`, `dev`, `prod`, and `db` templates, or a custom template directory, and can optionally save the image to a tar file and push it to a registry.

The command can run through either `docker` or `nerdctl`. You can force either CLI per invocation with `--use-docker` or `--use-nerdctl`; otherwise clio detects the first available runtime by probing `docker info` and then `nerdctl info`.

`.NET Framework` distributions are permanently unsupported for templates that package Creatio files. Bundled template `db` instead expects a `db` directory in the source payload and packages only that directory. Template `base` does not require `--from`.

## Usage

```bash
clio build-docker-image --template <name-or-path> [options]
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--from` | Required except for `base` | Path to a Creatio ZIP archive or extracted application directory. Every non-`base` template currently requires it. Bundled `db` expects a `db` folder in that source. |
| `--template` | Yes | Bundled template name (`base`, `dev`, `prod`, `db`) or custom template directory path |
| `--output-path` | No | Optional tar file path used by `docker save` |
| `--vscode-version` | No | Optional code-server version for bundled `dev` images; clio caches the matching archive locally and stages it into the Docker build context. Default: `4.112.0` |
| `--base-image` | No | For template `base`, the image reference to build. For bundled `dev` and `prod`, the local base image reference that clio uses as the parent image. Default: `creatio-base:8.0-v1` |
| `--registry` | No | Optional registry or repository prefix used by `docker push`. If template `base` already builds a fully qualified `--base-image`, that fully qualified image remains the effective push target. Clio now runs a registry preflight check before the expensive image build starts. |
| `--use-docker` | No | Force the command to use `docker` for this invocation |
| `--use-nerdctl` | No | Force the command to use `nerdctl` for this invocation; clio adds `--namespace k8s.io` automatically |

## Container Image CLI Selection

Clio resolves the effective container image CLI in this order:

1. `--use-docker`
2. `--use-nerdctl`
3. successful `docker info`
4. successful `nerdctl info`
5. fail with an error if both probes fail

When `nerdctl` is used, clio runs every image command with:

```text
--namespace k8s.io
```

For bundled-template base-image preflight under `nerdctl`, clio accepts required images if they already exist in either namespace:

- `k8s.io`
- `buildkit`

## Registry Authentication

`build-docker-image` does not currently accept registry username/password options.

If your registry requires authentication, log in with the same container CLI that clio will use:

```bash
docker login <registry-host>
nerdctl login <registry-host>
```

When `--registry` is set, clio now probes the registry before the build starts. If the registry responds with `401 Unauthorized`, clio fails early and tells you to run the matching `login` command instead of spending time building an image that cannot be pushed.

## Supported Sources

### ZIP archive

- The archive is extracted into a temporary folder before build.
- The command then detects the actual application root inside the extracted content.

### Extracted directory

- The directory is used directly.
- If the provided folder is a wrapper directory, the command searches within it for the Creatio application root.

## Runtime Validation

Validation depends on the template:

- bundled `db` requires a `db` directory in the source ZIP or directory
- `dev`, `prod`, and custom templates that package Creatio files accept only Creatio `.NET 8+` distributions

Expected `.NET 8+` markers for application-packaging templates:
- `Terrasoft.WebHost.dll`
- `Terrasoft.WebHost.dll.config`

If the source looks like a `.NET Framework` deployment, for example `Web.config` exists without `Terrasoft.WebHost.dll`, the command fails before invoking Docker.

## Image Naming

The image reference is generated automatically.

- Repository: `creatio-<template-name>`
- Tag: sanitized source archive or directory name

OCI labels:

- `dev`, `prod`, and custom Creatio templates:
  `org.creatio.database-source=<original source archive-or-directory name>`
- bundled `db`:
  `org.creatio.capability.db=true`
  `org.creatio.capability.db-source=<zip file name without .zip or folder name>`

Examples:
- Source `8.3.3_StudioNet8.zip` with template `dev` becomes `creatio-dev:8.3.3_studionet8`
- Source folder `release-build` with template `prod` becomes `creatio-prod:release-build`
- Source `8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU.zip` with template `db` becomes `creatio-db:8.3.4.1971_studionet8_softkey_postgresql_enu`

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

- `base`: Shared bundled base image. Builds `creatio-base:8.0-v1` by default, or the image reference passed in `--base-image`
- `dev`: Development-oriented image that consumes a local base image, with supervisor, SSH, and code-server
- `prod`: Production-oriented image that consumes a local base image and supervises only the Creatio app process
- `db`: Database-backup image based on `busybox:1.36.1`. Clio copies only the source `db/` payload into `/db` inside the image and adds OCI labels `org.creatio.capability.db=true` and `org.creatio.capability.db-source=<source-name>`

Base image behavior:

- bundled `dev` and `prod` use local base image `creatio-base:8.0-v1` by default
- use `--base-image` to point bundled `dev` or `prod` at a different local image
- template `base` is the explicit way to build the shared base image
- clio does not auto-build the base image when you build `dev` or `prod`
- successful bundled `base` builds are cached as local image archives under the clio settings folder
- bundled `dev` and `prod` can restore that cached archive automatically when the local base image is missing
- when `nerdctl` is used, clio accepts required base/source images from either `k8s.io` or `buildkit`
- when a required image exists only in `k8s.io`, clio also syncs it into `buildkit` so BuildKit can resolve `FROM <base-image>` without a registry round-trip
- bundled `dev` uses a local cached code-server archive instead of downloading from GitHub during `docker build`
- `--vscode-version` selects which `code-server-<version>-linux-amd64.tar.gz` archive clio fetches and caches for bundled `dev`

Bundled templates are copied to the local clio settings folder next to `infrastructure`:

- Windows: `%LOCALAPPDATA%\creatio\clio\docker-templates`
- macOS/Linux: `~/.local/creatio/clio/docker-templates`

## Local Caches And Safe Cleanup

Clio stores reusable Docker build assets under the clio settings root:

- Windows: `%LOCALAPPDATA%\creatio\clio`
- macOS/Linux: `~/.local/creatio/clio`

Important subfolders:

- `docker-templates/`
  Local copy of the bundled `base`, `dev`, `prod`, and `db` templates.
  Safe to delete. Clio recreates it automatically the next time you build a bundled template.

- `docker-assets/code-server/<version>/`
  Cached `code-server-<version>-linux-amd64.tar.gz` archives used by bundled `dev`.
  Safe to delete. Clio will download the requested version again on the next `dev` build.

- `docker-image-cache/`
  Cached tar archives for bundled `base` images.
  Safe to delete. The only impact is that clio will no longer be able to auto-restore a missing local base image from that cache until you build `--template base` again or otherwise reload the base image.

Temporary working folders are created under:

- Windows: `%LOCALAPPDATA%\Temp\clio\build-docker-image`
- macOS/Linux: `$TMPDIR/clio/build-docker-image` or `/tmp/clio/build-docker-image`

These temporary folders are also safe to delete when no `clio build-docker-image` process is running.

### Custom templates

Pass a template directory path to `--template`.

Requirements:
- The directory must exist
- The directory must contain a `Dockerfile`

## Build Flow

The command performs these steps:

1. Resolve the bundled or custom template
2. For templates other than `base`, validate the source path
3. Build a temporary Docker context
4. For bundled `db`, locate the source `db` directory and copy it into the Docker build context as `db/`
5. For `dev`, `prod`, and custom Creatio templates, exclude `db` directories from the extracted payload and staged Docker context, and write `.dockerignore` rules for `db` and `source/db`
6. Normalize staged `*.sh` files to Unix LF line endings for Linux container compatibility
   For bundled `dev`, ensure the requested code-server archive is available in the local clio cache and copy it into the Docker build context as `code-server.tar.gz`
7. Resolve the effective container image CLI from explicit flags or runtime probing
8. Run the CLI version check
9. For bundled `dev` and `prod`, verify that the selected base image is available locally
   If it is missing and a cached base image archive exists, restore it first
   When `nerdctl` is used, clio accepts the image from either `k8s.io` or `buildkit`
   If it exists only in `k8s.io`, clio also mirrors it into `buildkit` before `build`
10. If `--registry` is set, run registry push preflight before the expensive image build
   Probe `GET /v2/` to confirm the registry is reachable
   Probe `POST /v2/<repository>/blobs/uploads/` to confirm upload initiation for the exact target repository
   Fail early when the registry is unreachable, rejects upload initiation, or requires authentication
11. Run image build with `--pull=false` so cached base images are reused instead of forcing a refresh
12. Optionally run image save
13. Optionally run image tag and image push
   Clio now prints explicit `Tagging Docker image for registry push: ...` and `Pushing Docker image to registry: ...` lines before those actions
14. Clean up temporary files

## Why `#5 [internal] load build context` can be slow

The numbered lines in the container build output, for example:

```text
#5 [internal] load build context
```

come from Docker/BuildKit. They are not the same thing as the numbered clio build flow above.

That BuildKit step uploads the prepared Docker context from the host machine into the builder. For bundled `dev`, the context typically contains:

- the extracted Creatio application under `source/`
- the cached `code-server.tar.gz`
- the staged Dockerfile, entrypoint, supervisor config, and `.dockerignore`

On Rancher Desktop with `nerdctl`, this upload crosses the Windows host to the WSL/containerd builder boundary, so large payloads are very noticeable. In the last successful `dev` build, the `load build context` step transferred about `1.99 GB`, which is why it took a long time before the first real `RUN`/`COPY` steps appeared.

What helps:

- `prod` is usually faster than `dev` because it does not stage the extra `code-server.tar.gz`
- keeping `db` out of the source tree avoids uploading unnecessary database files
- reusing the cached code-server archive avoids re-downloading it, but it still must be copied into the build context for bundled `dev`
- the new nerdctl base-image fix avoids the old `base-rootfs.tar` workaround, so this step is now dominated by your actual app payload plus `code-server`, not by a duplicated base image tar

## Examples

### Build the default shared base image

```bash
clio build-docker-image --template base
```

### Build a custom-named shared base image

```bash
clio build-docker-image --template base --base-image "ghcr.io/acme/creatio-base:dotnet10-vpn"
```

### Build a local development image from a ZIP archive

```bash
clio build-docker-image --from "C:\Creatio\8.3.3_StudioNet8.zip" --template dev
```

### Build bundled dev from a custom base image

```bash
clio build-docker-image --from "/opt/builds/creatio-net8" --template dev --base-image "ghcr.io/acme/creatio-base:dotnet10-vpn"
```

### Build a production image from an extracted directory

```bash
clio build-docker-image --from "/opt/builds/creatio-net8" --template prod
```

### Build a database-backup image from a ZIP archive

```bash
clio build-docker-image --from "C:\Creatio\8.3.4_StudioNet8.zip" --template db
```

### Force nerdctl for one build

```bash
clio build-docker-image --from "/opt/builds/creatio-net8" --template prod --use-nerdctl
```

### Force docker for one build

```bash
clio build-docker-image --from "/opt/builds/creatio-net8" --template prod --use-docker
```

### Build bundled dev image with a specific cached code-server version

```bash
clio build-docker-image --from "/opt/builds/creatio-net8" --template dev --vscode-version "4.113.1"
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

Bundled `db` images instead include:

```text
org.creatio.capability.db=true
org.creatio.capability.db-source=8.3.4.1671_StudioNet8_Softkey_PostgreSQL_ENU
```

## Common Errors

### Container image CLI is not installed

If clio reports that the selected container image CLI is not installed or not available in `PATH`, install that CLI and verify that the executable can be started from the same shell.

### Unknown bundled template

If the bundled template name is not found, use one of the shipped names (`base`, `dev`, `prod`, `db`) or pass a valid custom template directory.

### Custom template missing Dockerfile

Custom templates must contain a `Dockerfile`. Add one before running the command.

### .NET Framework source

If the source looks like a `.NET Framework` Creatio deployment, the command stops immediately. Use a `.NET 8+` distribution instead.

### Missing `db` directory for bundled `db`

If you use `--template db`, the source ZIP or directory must contain a `db` folder, or you can point `--from` directly at the `db` folder itself.

## Related Commands

- [`deploy-creatio`](./deploy-creatio.md) for deploying Creatio locally or to infrastructure
- [`create-k8-files`](./CreateK8FilesCommand.md) for generating infrastructure deployment manifests
- [`deploy-infrastructure`](./DeployInfrastructureCommand.md) for deploying local Kubernetes infrastructure
