# studio (experimental)

Deploy a portable AI Studio handoff through creatio-operator, or independently
check out its source code. Enable the existing `runtime` feature first:

```shell
clio experimental --name runtime --enable
clio studio deploy --profile studio-handoff.json --context rancher-desktop
clio studio checkout --profile studio-handoff.json --directory ./studio-sources
clio studio status --context rancher-desktop --namespace studio-portable --name portable-demo
```

`deploy` and `checkout` are completely independent. Deploy requires local Rancher
Desktop with the Moby engine, `rdctl`, `kubectl`, registry access, and the values
required by the handoff. It requires no Git, source checkout, Helm or image builds.
Checkout requires Git and access to the declared repositories, with no cluster
or deployment credentials. The installer is an administrative operation; use a
handoff from a trusted producer.

If the operator is absent, deploy uses Clio's bundled operator installer and the
handoff's optional pinned `operator.image`, which must match the image bundled
with this Clio release. A compatible existing controller is never implicitly
replaced with a different requested image. Clio-owned older controllers can be
upgraded using the existing installer; foreign operators receive migration guidance. Clio does not silently replace an
operator managed outside its installation profile.

## Missing inputs and retries

Deploy returns structured JSON with `state: InputRequired` and `missingInputs`
entries containing name, description and whether the value is secret. An agent
should ask the recipient for these values and put them into a JSON object file:

```json
{
  "loginPassword": "<recipient-selected password>",
  "modelBaseUrl": "<endpoint reachable from the cluster>",
  "modelApiKey": "<recipient credential>",
  "modelId": "<model exposed by that endpoint>"
}
```

```shell
clio studio deploy --profile studio-handoff.json --inputs recipient-inputs.json --context rancher-desktop
```

Protect this file and keep it out of source control. Values do not travel as
command-line arguments. Input precedence is recipient override, handoff value,
previously saved value, then an explicitly declared generator. Generated values
are stored in the installation's handoff Secret and reused on retries.
Missing inputs cause no cluster writes. Submission returns a revision; poll
`studio status` until Ready or a failure requiring attention. Kubernetes response
bodies are suppressed to avoid printing submitted credentials.

## Handoff contract

The JSON envelope uses `schema: creatio-studio-handoff/v1`, a lowercase `name`,
and independent `sources`, `inputs`, `operator` and `deployment` sections. The
maximum file size is 800 KiB. Every input is a scalar JSON value with optional
`value`, `required`, `secret`, `description` and `generate` fields. Supported
generators are `password` (32 random bytes, Base64), `uuid`, and `rsa-private-key`
(2048-bit PEM). `${inputName}` placeholders are expanded as data within the
deployment tree. An exact placeholder preserves its scalar JSON type.

The producer includes the full resolved source inventory, including the metarepo
and any additional repositories. Each source has a unique `name`, HTTPS or SSH
`url`, `branch`, full `commit` SHA and relative `path`. Checkout fetches the branch
and verifies the recorded commit belongs to it. Bare caches reside underneath
`.studio-repositories`; detached worktrees occupy the recorded paths. Rerunning
checkout preserves dirty files at a matching commit. A different commit or Git
repository at a target path fails without resetting it. Unsafe paths, credential-
bearing URLs and paths traversing symlinks/junctions are rejected.

`deployment` contains the target `namespace` and ordered `phases` of prepared
Kubernetes resources. The operator validates namespaces, ownership and supported
resource kinds; it pulls pinned images, applies resources, waits for migrations
and reports workload readiness. Optional operator-supported dependencies and
environment-provisioning permissions are declared explicitly. Preparation of
that artifact is a separate producer responsibility.

PVCs and the handoff Secret are retained after deletion of the Studio CR. There
is no automatic data migration, source build, CRM integration or uninstall in
these commands. Retained objects from a deleted installation are not silently
adopted by a newly created CR.

## Agent tools

The host MCP server exposes `studio-deploy(profile, context, inputs?)`,
`studio-checkout(profile, directory)` and
`studio-status(name, context, namespaceName)` behind the same runtime feature.
Start it with `clio mcp-server --runtime-host`. Container-hosted and credential-
passthrough servers cannot invoke these developer-host capabilities. All paths
refer to the server's host, not the agent's filesystem.



Use `$${NAME}` in a resource string to preserve the literal `${NAME}` (for example,
a shell variable inside a Job). `${name}` resolves a declared handoff input;
values supplied by the recipient are never interpreted a second time. Null or
blank values do not override saved generated credentials on retry.

Deploy checks the controller capability and rollout as well as the CRD. The
existing Clio operator installer upgrades an owned installation when needed;
foreign operators require explicit administrator migration with the matching
operator release. Studio status reports Kubernetes readiness, which is separate
from functional login, Twin and Builder acceptance.


KEDA bootstrap is opt-in only when the deployment declares `keda-2.20.2`.
Ordinary `install-operator` does not install or take ownership of KEDA's fixed
roles. A Studio deployment that requests KEDA refuses foreign dependency objects;
sharing an existing installation requires an explicit compatibility migration.

After saving the handoff Secret, updates patch only the CR specification and test
its UID, installation label and previous spec. Controller status updates do not
race this submission. A newly created CR already has the desired spec and needs
no redundant replace.
