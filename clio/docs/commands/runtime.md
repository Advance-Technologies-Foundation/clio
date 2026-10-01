# runtime

Experimental; disabled by default. Enable with `clio experimental --name runtime --enable`. This also enables `install-operator`.

Build images or create and inspect an operator-managed Creatio instance in an explicit Kubernetes context.

```sh
clio runtime build --context rancher-desktop --from "C:\Builds\Creatio.zip"
clio runtime create my-dev --context rancher-desktop --namespace creatio-runtimes --image creatio-dev:my-build
clio runtime status my-dev --context rancher-desktop --namespace creatio-runtimes
clio runtime list --context remote-cluster --namespace development
```

`build` is currently a Windows + Rancher Moby prototype. It requires `rdctl` and a ZIP on a local drive shared with Rancher's WSL VM. It runs the operator's pinned preparation image, detects .NET 8/10 from the ZIP runtime configuration, and builds base, database and attachable Dev images directly in Rancher's Docker store. It never pushes images or installs a registry. Internet access is needed for the preparation image, base layers and tool dependencies. Temporary build containers and staging volumes are removed; built images and normal Docker layer caches remain. Use the printed Dev image reference with `create`. This build path cannot target a remote cluster; `create`, `list` and `status` can.

`create` requires a name and tagged `--image`. Names must start with a lowercase letter, contain only lowercase letters, digits and hyphens, end with a letter or digit, and be at most 55 characters (reserving room for operator resource suffixes). The image and matching database image must be available to the selected cluster and compatible with the operator. Clio creates the namespace if absent and submits a `CreatioInstance` using template database provisioning; the operator performs asynchronous restoration. An existing runtime with that name is never overwritten. Use `status` until Ready before attaching.

`--context` is mandatory except for `detach`, which uses the receipt; Clio never changes current-context. `--namespace` defaults to `creatio-runtimes`. Runtime identity is context + namespace + name, so equal names in different clusters remain distinct. `status` requires a name; `list` lists only the selected namespace. Creation success means accepted, not ready.

After provisioning:

```sh
clio runtime attach my-dev --context rancher-desktop --namespace creatio-runtimes --workspace /path/to/workspace --ssh-alias my-dev --mode develop
```

The existing attachment receipt pins the cluster, namespace and resource UID. Detaching stops synchronization, not the runtime. This host-side command requires kubectl access to the operator CRDs; it does not require ClioGate. Host-side MCP usage is documented below. It does not yet automatically register Creatio login credentials in Clio appsettings.

Before attachment, enable FSM and export packages, authorize the host SSH key and configure the SSH alias. This prototype does not automate those preparation steps. The local profile exposes Creatio and SSH; external HTTPS MCP routing is not configured by this profile.

## Attach and detach

```sh
clio runtime attach my-dev --workspace ./workspace --ssh-alias my-dev --context rancher-desktop
clio runtime detach --workspace ./workspace
```

Attach requires name, workspace, SSH alias and explicit context. Namespace defaults to `creatio-runtimes`; provider defaults to `operator-kubernetes` and mode to `develop`. Detach uses the saved attachment receipt and requires only workspace. See [attachment details](#attach) and [detachment details](#detach). The former standalone `attach` and `detach` verbs are removed.

## attach

Experimental; enable with `clio experimental --name runtime --enable`.

Attach an existing local Clio workspace to an existing Creatio operator runtime. Run this command on the agent's machine. It does not deploy Creatio and does not require ClioGate.

```shell
clio runtime attach dev-instance --workspace ./workspace --ssh-alias dev-instance --context my-cluster --namespace creatio-dev-instance --mode develop
```

| Argument | Required | Default | Purpose |
|---|---|---|---|
| environment | Yes | | CreatioInstance resource name, not a Clio appsettings alias |
| --workspace | Yes | | Existing workspace created by `clio createw` |
| --ssh-alias | Yes | | Existing OpenSSH Host alias with key authentication and a trusted host key |
| --provider | No | operator-kubernetes | Runtime discovery provider |
| --context | Yes | | Cluster containing the runtime |
| --namespace | No | creatio-runtimes | Select the namespace if a name is ambiguous |
| --mode | No | develop | Only develop is currently supported |

Install Mutagen 0.18 or later, OpenSSH and kubectl on the local machine and make them available on PATH. Configure kubectl authorization and SSH before attaching. The SSH alias must identify the selected instance. The operator must provide an attachable Dev image with Python, SSH, and `/opt/creatio-attachment/attachment.json`. FSM must already be enabled, Creatio restarted, and packages exported. The command checks FSM configuration; it does not toggle it or compile Creatio.

Example OpenSSH configuration (use the endpoint and trusted host key supplied by your operator):

```sshconfig
Host dev-instance
    HostName cluster.example.com
    Port 42000
    User root
    IdentityFile ~/.ssh/creatio-dev
    IdentitiesOnly yes
    StrictHostKeyChecking yes
    BatchMode yes
```

Verify `ssh dev-instance` works before attaching. Do not disable host-key checking to work around a mismatch.

The same operator provider works with local Rancher Desktop and remote clusters. Other provisioning systems require their own provider; Windows/IIS is not implemented.

The workspace, including `packages` and `.clio/workspaceSettings.json`, is synchronized continuously in both directions using Mutagen `two-way-safe`. Git metadata, agent configuration (`.codex`, `.claude`, `.mcp.json`), attachment receipts/markers and `bin`/`obj` are excluded. Build on the runtime. Logs and unrelated runtime configuration stay remote and can be inspected through SSH. Existing package directories or another workspace's links are never overwritten.

Attachment information is recorded in `.clio/attachment.local.json`. Keep this machine-specific file and its `.lock` file out of source control. Run the same command again after adding packages or to resume synchronization. Unresolved conflicts cause failure and preserve both copies; inspect `mutagen sync list --long` and resolve them explicitly.

The command prints the provider's Clio MCP URL. Agent MCP configuration, credentials, package installation/activation, and build/debug operations are not modified automatically in this slice. An FSM link makes source visible; it does not register a new package in the Creatio database.

Use [detach](#detach) to stop synchronization without deleting the runtime or workspace files.

## detach

Experimental; enable with `clio experimental --name runtime --enable`.

Stop a local workspace attachment while preserving its runtime, database, and local and remote workspace files.

```shell
clio runtime detach --workspace ./workspace
```

`--workspace` is required and must identify the original Clio workspace. Run this on the same host and with the same Mutagen user/data directory used for attach. Mutagen, OpenSSH, kubectl and access to the original Kubernetes context are required.

Detach pauses the owned session, preserves the runtime FSM links and terminates that session. Pending or conflicting changes remain in their respective copies; detach does not choose a winner or guarantee that pending changes were delivered. Other workspaces and Mutagen sessions are not changed. The receipt remains marked inactive for reattachment. If cleanup fails, it remains active so the operation can be retried. A replacement instance with the same name is never modified.

No runtime, database, files, SSH credentials or agent configuration is deleted. This command does not require ClioGate. See [attach](#attach) for provider support and prerequisites.

## Images

List built distributions from the selected cluster's operator, whether or not instances use them:

```sh
clio runtime images --context rancher-desktop
clio runtime images --context rancher-desktop --json
clio runtime images --context omen --json
```

The default is a table; `--json` returns an array with product, version, runtime, tag, exact Dev/Prod image references, database image references, template readiness and deployability. Incomplete distributions remain visible with `deployable: false`. Empty inventories return `[]` in JSON mode. Nothing is built, downloaded or deployed.

Requires Kubernetes service-proxy access to `creatio-operator:8080` in `creatio-system` (override with `--operator-namespace`). When dashboard authentication is enabled, Clio reuses the credentials configured in the operator Deployment; permission to read that Deployment and its referenced credential Secret is required. Credentials are kept in memory and never printed. The same flow applies to local and remote contexts and does not change current-context. No ClioGate or remote MCP tool is required.

## MCP on the developer host

Enable the feature and explicitly start a host-capable stdio server:

```sh
clio experimental --name runtime --enable
clio mcp-server --runtime-host
```

Tools: `runtime-images`, `runtime-list`, `runtime-status`, `runtime-create`, `runtime-build`, `runtime-attach`, `runtime-detach`, and `install-operator`. Discover their contracts with `get-tool-contract`. Read operations use `clio-run`; mutation operations use `clio-run-destructive`. Responses use the standard structured exit-code and execution-log envelope; `runtime-images` includes the JSON catalogue in its output.

Every path and SSH alias refers to the MCP server machine. Tools reject calls without the startup opt-in, inside Kubernetes/.NET containers, and under credential passthrough. The runtime feature alone does not authorize host operations. No new prompts or resources are needed: tool descriptions and contracts carry the prerequisites. These adapters reuse CLI validation and services. They execute in-process because attachment state and long-lived Mutagen synchronization belong to the developer host; operations are not moved to short-lived MCP workers. Stopping a client request does not roll back provisioning or image builds.
