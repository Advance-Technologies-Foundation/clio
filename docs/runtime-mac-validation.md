# Runtime commands: Mac validation before merge

This branch is experimental and must remain unmerged until the Mac results are reviewed. Run these commands from the branch checkout with .NET 10 SDK installed. They use the branch binary rather than an installed global Clio tool.

## Scope and prerequisites

- `kubectl`, OpenSSH and Mutagen 0.18+ must be on PATH for attachment.
- Local bootstrap requires Rancher Desktop with Kubernetes, Moby/dockerd and Traefik enabled. Use a disposable cluster; bootstrap installs shared PostgreSQL and Redis and creates the operator resources.
- The bundled operator is Linux AMD64 only. Local bootstrap on Apple Silicon is **not supported by this pin**. Read/list/create/attach against a compatible remote AMD64 cluster can still be tested from an Apple Silicon host.
- `runtime build` is a Windows + Rancher WSL prototype and is explicitly unsupported on macOS. Do not treat that expected refusal as proof that Mac image building works.
- The bootstrap profile uses HTTP localhost ingress and disables in-cluster registry builds. It does not recreate a separately configured HTTPS installation.

## Build and enable

```sh
dotnet build clio/clio.csproj -f net10.0
clio_branch() { dotnet "$PWD/clio/bin/Debug/net10.0/clio.dll" "$@"; }
clio_branch experimental --name runtime --enable
```

Use an isolated Clio home if preferred: set `CLIO_HOME` to a new directory before enabling the flag. The feature is disabled by default.

## Bootstrap (Intel Mac only)

```sh
clio_branch install-operator --target rancher-desktop --context rancher-desktop
kubectl --context rancher-desktop -n creatio-system get deployment creatio-operator -o jsonpath='{.spec.template.spec.containers[0].image}'
```

The output must exactly match `image` in `clio/tpl/operator/rancher-desktop/provenance.json`. Open `http://creatio-operator.localhost`, confirm dashboard and infrastructure status, then repeat bootstrap to verify that credentials and data are retained. Do not run bootstrap over an independently managed operator installation.

## Inventory and runtime lifecycle

Replace `test-cluster` with the intended context. Verify the current context is unchanged afterwards.

```sh
clio_branch runtime images --context test-cluster
clio_branch runtime images --context test-cluster --json
clio_branch runtime list --context test-cluster --namespace creatio-runtimes
```

Check that table and JSON describe the same distributions, including exact image references and incomplete distributions. On a disposable target, use a deployable Dev image from that output:

```sh
clio_branch runtime create mac-test --context test-cluster --namespace creatio-runtimes --image REGISTRY/creatio-dev:TAG
clio_branch runtime status mac-test --context test-cluster --namespace creatio-runtimes
```

Creation is asynchronous; verify readiness and open Creatio. Before attachment, prepare an existing Clio workspace, enable FSM/export packages, and configure a trusted SSH alias for the selected instance as described in [runtime](../clio/docs/commands/runtime.md).

```sh
clio_branch runtime attach mac-test --context test-cluster --namespace creatio-runtimes --workspace /absolute/path/workspace --ssh-alias mac-test
clio_branch runtime detach --workspace /absolute/path/workspace
```

Verify two-way synchronization with a disposable source file, reattachment, conflict handling, and that detach preserves local files, remote files and the runtime. Remove the disposable instance through the operator after testing.

## MCP and automated checks

```sh
dotnet test clio.tests/clio.tests.csproj -f net10.0 --filter 'FullyQualifiedName~Runtime|FullyQualifiedName~OperatorInstaller|FullyQualifiedName~FeatureToggle'
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter 'FullyQualifiedName~RuntimeTools'
clio_branch mcp-server --runtime-host
```

Confirm discovery of runtime tools with the feature enabled; without `--runtime-host`, host operations must refuse execution. With the feature disabled, tools must be absent. The live catalogue tests are opt-in and assume two developer-specific contexts; the default run skips those four cases.

Record Mac architecture, Rancher version/engine, branch SHA, operator digest, commands tested and sanitized failures in the PR. Never include passwords, kubeconfig credentials or tokens.
