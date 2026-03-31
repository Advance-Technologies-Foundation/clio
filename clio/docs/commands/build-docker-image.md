# build-docker-image

Build a Docker image for a Creatio .NET 8+ distribution or bundled database backup.

## Usage

```bash
clio build-docker-image [options]
```

## Description

Build a Docker image for a Creatio .NET 8+ distribution or bundled database backup.

## Examples

```bash
clio build-docker-image [options]
```

## Options

```bash
--from <VALUE>
    Path to a Creatio ZIP archive or extracted directory; required for every
    template except `base`. Bundled `db` expects a `db` folder in the source
--template <VALUE>
    Bundled template name (base, dev, prod, db) or template directory path.
    Required.
--vscode-version <VALUE>
    Optional code-server version for bundled dev images; clio caches the
    corresponding archive locally and injects it into the Docker build context
--base-image <VALUE>
    Optional base image reference. For template `base`, this is the image tag to
    build. For bundled `dev` and `prod`, this is the local base image clio uses
    instead of the default
--output-path <VALUE>
    Optional tar file path where the built image should be saved
--registry <VALUE>
    Optional registry or repository prefix used when pushing the image
--use-docker
    Use the docker CLI for this invocation, bypassing runtime CLI auto-detection
--use-nerdctl
    Use the nerdctl CLI for this invocation, bypassing runtime CLI auto-detection
```

- [Clio Command Reference](../../Commands.md#build-docker-image)
