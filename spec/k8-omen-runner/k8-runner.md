# GitHub Actions runner on omen (k3s)

Self-hosted GitHub Actions runner pool for this repo, hosted in the `omen` k3s cluster via [Actions Runner Controller (ARC)](https://github.com/actions/actions-runner-controller). Runs alongside the existing Windows self-hosted runner (`TS1-MRKT-WEB01`).

## What's deployed

| Component | Chart | Release | Namespace |
|---|---|---|---|
| ARC controller | `oci://ghcr.io/actions/actions-runner-controller-charts/gha-runner-scale-set-controller` | `arc` | `arc-systems` |
| Runner scale set | `oci://ghcr.io/actions/actions-runner-controller-charts/gha-runner-scale-set` | `clio-runners` | `arc-runners` |
| PAT secret | — | `github-pat` | `arc-runners` |

Scale-set values:

| Key | Value |
|---|---|
| `githubConfigUrl` | `https://github.com/Advance-Technologies-Foundation/clio` |
| `githubConfigSecret` | `github-pat` |
| `runnerScaleSetName` | `clio-runners` |
| `minRunners` | `0` |
| `maxRunners` | `2` |

Runner pods are created on demand. Idle state = no pods in `arc-runners`, listener pod `clio-runners-*-listener` stays running in `arc-systems`. GitHub UI shows the scale set as **Offline** while idle — that is expected.

## How to target the runner

In a workflow file, set `runs-on` to the scale-set name:

```yaml
jobs:
  build:
    runs-on: clio-runners
```

The Windows runner is targeted by its labels:

```yaml
jobs:
  package:
    runs-on: [self-hosted, Windows]
```

Both in the same workflow (matrix or separate jobs) is fine — see "Combining with Windows" below.

Each ARC pod is ephemeral: fresh checkout, no leftover state. The Windows runner is persistent and keeps state between jobs.

## Running a .NET build in a container

Recommended pattern for clio: use the .NET SDK container image directly, so the runner pool stays generic.

```yaml
# .github/workflows/build.yml
name: Build
on:
  push:
    branches: [master, main]
  pull_request:
  workflow_dispatch:

jobs:
  build:
    runs-on: clio-runners
    container:
      image: mcr.microsoft.com/dotnet/sdk:10.0
    steps:
      - uses: actions/checkout@v4

      - uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: nuget-${{ hashFiles('**/*.csproj', '**/Directory.Packages.props') }}
          restore-keys: nuget-

      - run: dotnet restore clio.slnx
      - run: dotnet build clio.slnx --no-restore -c Release
      - run: dotnet test clio.slnx --no-build -c Release --logger "trx;LogFileName=results.trx" --results-directory ./test-results

      - if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: ./test-results/**/*.trx
```

How this works: GitHub Actions on a self-hosted runner accepts the `container:` directive. The ARC runner pod has containerd access through the host; it pulls `mcr.microsoft.com/dotnet/sdk:10.0` once (cached by containerd afterwards), starts a job container, and executes the steps inside it. The runner pod itself does not need any .NET tooling.

Trade-off vs `actions/setup-dotnet`: container pins the exact SDK image and isolates the build; `setup-dotnet` downloads the SDK fresh per job (~20–30 s) but avoids pulling a 1+ GB image. The container approach is the cleaner choice for a consistent SDK and matches how the rest of the team likely runs `dotnet` locally.

`mcr.microsoft.com/dotnet/sdk:10.0` is a preview SDK until .NET 10 GA. If a `global.json` pins an older SDK band, the build inside the container will fail with "SDK not found" — adjust the image tag (`9.0`, `8.0`) or `global.json` to match.

## Combining with the Windows runner

### Matrix across both OSes

```yaml
jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        include:
          - name: linux
            runs-on: clio-runners
            container: mcr.microsoft.com/dotnet/sdk:10.0
          - name: windows
            runs-on: [self-hosted, Windows]
            container: null
    runs-on: ${{ matrix.runs-on }}
    container: ${{ matrix.container }}
    steps:
      - uses: actions/checkout@v4
      - run: dotnet build clio.slnx -c Release
```

### Pipeline: build on Linux, package on Windows

```yaml
jobs:
  build:
    runs-on: clio-runners
    container:
      image: mcr.microsoft.com/dotnet/sdk:10.0
    steps:
      - uses: actions/checkout@v4
      - run: dotnet publish clio/clio.csproj -c Release -o out
      - uses: actions/upload-artifact@v4
        with: { name: clio-bin, path: out/ }

  package-windows:
    needs: build
    runs-on: [self-hosted, Windows]
    steps:
      - uses: actions/download-artifact@v4
        with: { name: clio-bin, path: out/ }
      - run: powershell -File ./scripts/build-msi.ps1
```

Caveat: a `container:` directive only works on the Linux pool. The Windows runner runs steps directly on the host.

## Authentication

The runner authenticates to GitHub using a classic Personal Access Token with `repo` scope, stored in the `github-pat` secret:

```bash
kubectl -n arc-runners get secret github-pat -o jsonpath='{.data.github_token}' | base64 -d
```

### Rotate the PAT

```bash
# Generate a new classic PAT at github.com/settings/tokens with scope: repo
NEW_PAT=ghp_xxxxxxxxxxxxxxxxxxxx

kubectl -n arc-runners create secret generic github-pat \
  --from-literal=github_token="$NEW_PAT" \
  --dry-run=client -o yaml | kubectl apply -f -

# Restart the controller so the listener picks up the new token
kubectl -n arc-systems rollout restart deploy/arc-gha-rs-controller
```

## Operations

Check controller and listener health:

```bash
kubectl get pods -n arc-systems
# expected:
#   arc-gha-rs-controller-*        Running
#   clio-runners-*-listener        Running
```

Watch runner pods come and go during a job:

```bash
kubectl get pods -n arc-runners -w
```

Change the max number of concurrent runners:

```bash
helm upgrade clio-runners \
  oci://ghcr.io/actions/actions-runner-controller-charts/gha-runner-scale-set \
  -n arc-runners \
  --reuse-values \
  --set maxRunners=4
```

Tail listener logs (useful when the scale set won't connect to GitHub):

```bash
kubectl logs -n arc-systems -l app.kubernetes.io/component=runner-scale-set-listener -f
```

## Uninstall

```bash
helm uninstall clio-runners -n arc-runners
helm uninstall arc -n arc-systems
kubectl delete ns arc-runners arc-systems
kubectl delete crd $(kubectl get crd -o name | grep actions.github.com)
```

## Reference

- ARC docs: https://github.com/actions/actions-runner-controller
- Scale set Helm chart values: https://github.com/actions/actions-runner-controller/blob/master/charts/gha-runner-scale-set/values.yaml
- Runner labels for this repo: `clio-runners` (Linux/k8s), `[self-hosted, Windows, X64]` (Windows VM)
