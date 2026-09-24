# install-operator

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

Open `http://creatio-operator.localhost`. Dashboard credentials are generated once in the Kubernetes Secret `creatio-system/creatio-operator-dashboard-admin`, keys `username` and `password`; read them using your Kubernetes client. Clio does not print credentials. Retries preserve this Secret and existing shared-infrastructure settings.

The command does not reset Rancher, wipe namespaces, switch current-context, or take over a different operator installation. A conflicting Nexus-managed policy requires an explicit migration. Failures leave resources available for inspection and retry. Operator readiness does not imply that every infrastructure component is ready; inspect the dashboard before creating a runtime.

This is a host-side bootstrap command, not a remote MCP tool. Runtime attachment remains a separate `attach` operation with its own context, namespace and SSH identity.
