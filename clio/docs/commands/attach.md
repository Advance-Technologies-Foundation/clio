# attach

Attach an existing local Clio workspace to an existing Creatio operator runtime. Run this command on the agent's machine. It does not deploy Creatio and does not require ClioGate.

```shell
clio attach dev-instance --workspace ./workspace --ssh-alias dev-instance --context my-cluster --namespace creatio-dev-instance --mode develop
```

| Argument | Required | Default | Purpose |
|---|---|---|---|
| environment | Yes | | CreatioInstance resource name, not a Clio appsettings alias |
| --workspace | Yes | | Existing workspace created by `clio createw` |
| --ssh-alias | Yes | | Existing OpenSSH Host alias with key authentication and a trusted host key |
| --provider | No | operator-kubernetes | Runtime discovery provider |
| --context | No | Current kubectl context | Cluster containing the runtime |
| --namespace | No | Search all namespaces | Select the namespace if a name is ambiguous |
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

Use [detach](detach.md) to stop synchronization without deleting the runtime or workspace files.
