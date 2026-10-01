# install-operator

Experimental; enable with `clio experimental --name runtime --enable`.

Install the Creatio operator into local Rancher Desktop without installing Nexus or another registry.

```sh
clio install-operator --target rancher-desktop
clio install-operator --target rancher-desktop --context rancher-desktop --image example.com/creatio-operator:version
```

Requires `kubectl`, `rdctl`, running Rancher Desktop with the Moby/dockerd engine, Traefik, and cluster-admin permissions. Does not require ClioGate or a registered Creatio environment.

| Option | Meaning |
|---|---|
| `--target` | Required installation profile: `rancher-desktop`. |
| `--context` | Kubernetes context; defaults to `rancher-desktop`, regardless of current-context. Must identify the local Rancher cluster. |
| `--image` | Optional operator image reference. Defaults to the immutable digest in the bundled distribution. |

The command installs CRDs, RBAC, an authenticated operator dashboard and ingress. It creates a shared policy before starting the controller: PostgreSQL 18.6 and Redis are managed when absent; existing external resources remain under their existing ownership. Nexus and MSSQL are external and are not installed. Operator-side registry builds are disabled in this profile; local images belong in Rancher's image store.

Open `http://creatio-operator.localhost`. New local installations use username `Supervisor` and password `Supervisor`, stored once in the Kubernetes Secret `creatio-system/creatio-operator-dashboard-admin`, keys `username` and `password`; read them using your Kubernetes client. Clio does not print credentials. Retries preserve this Secret and existing shared-infrastructure settings.

The command does not reset Rancher, wipe namespaces, switch current-context, or take over a different operator installation. A conflicting Nexus-managed policy requires an explicit migration. Failures leave resources available for inspection and retry. Operator readiness does not imply that every infrastructure component is ready; inspect the dashboard before creating a runtime.

This is a host-side bootstrap command. Runtime attachment remains a separate `runtime attach` operation with its own context, namespace and SSH identity.

The bundled operator is pinned by OCI digest in `tpl/operator/rancher-desktop/provenance.json`, together with its source revision and supported image platforms. The CRD schemas are from the same operator revision; the registry-free deployment configuration is a Clio-specific profile. This pin supports `linux/amd64` only. Apple Silicon Kubernetes nodes are not currently supported by this bundled image. See the [Mac validation checklist](../../../docs/runtime-mac-validation.md) before testing this experimental branch.

MCP: available as `install-operator` on an explicitly enabled developer-host server. See [runtime MCP setup](runtime.md#mcp-on-the-developer-host).
