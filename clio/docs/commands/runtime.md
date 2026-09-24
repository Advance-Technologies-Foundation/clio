# runtime

Build images or create and inspect an operator-managed Creatio instance in an explicit Kubernetes context.

```sh
clio runtime build --context rancher-desktop --from "C:\Builds\Creatio.zip"
clio runtime create my-dev --context rancher-desktop --namespace creatio-runtimes --image creatio-dev:my-build
clio runtime status my-dev --context rancher-desktop --namespace creatio-runtimes
clio runtime list --context remote-cluster --namespace development
```

`build` is currently a Windows + Rancher Moby prototype. It requires `rdctl` and a ZIP on a local drive shared with Rancher's WSL VM. It runs the operator's pinned preparation image, detects .NET 8/10 from the ZIP runtime configuration, and builds base, database and attachable Dev images directly in Rancher's Docker store. It never pushes images or installs a registry. Internet access is needed for the preparation image, base layers and tool dependencies. Temporary build containers and staging volumes are removed; built images and normal Docker layer caches remain. Use the printed Dev image reference with `create`. This build path cannot target a remote cluster; `create`, `list` and `status` can.

`create` requires a name and tagged `--image`. Names must start with a lowercase letter, contain only lowercase letters, digits and hyphens, end with a letter or digit, and be at most 55 characters (reserving room for operator resource suffixes). The image and matching database image must be available to the selected cluster and compatible with the operator. Clio creates the namespace if absent and submits a `CreatioInstance` using template database provisioning; the operator performs asynchronous restoration. An existing runtime with that name is never overwritten. Use `status` until Ready before attaching.

`--context` is mandatory for every operation; Clio never changes current-context. `--namespace` defaults to `creatio-runtimes`. Runtime identity is context + namespace + name, so equal names in different clusters remain distinct. `status` requires a name; `list` lists only the selected namespace. Creation success means accepted, not ready.

After provisioning:

```sh
clio attach my-dev --context rancher-desktop --namespace creatio-runtimes --workspace /path/to/workspace --ssh-alias my-dev --mode develop
```

The existing attachment receipt pins the cluster, namespace and resource UID. Detaching stops synchronization, not the runtime. This host-side command requires kubectl access to the operator CRDs; it does not require ClioGate. No remote MCP tool is added. It does not yet automatically register Creatio login credentials in Clio appsettings.

Before attachment, enable FSM and export packages, authorize the host SSH key and configure the SSH alias. This prototype does not automate those preparation steps. The local profile exposes Creatio and SSH; external HTTPS MCP routing is not configured by this profile.
