# Rancher operator bootstrap

Install the Creatio operator through Clio, then provision attachable runtimes in an explicitly selected Kubernetes context. Agents and workspace files remain on the developer's computer.

## Capabilities

- Bootstrap: `install-operator --target rancher-desktop` installs a pinned operator distribution; an explicit context overrides the `rancher-desktop` default. Command success means the deployment is ready. Separately verify the dashboard ingress responds during live acceptance testing.
- Local images: use Rancher's Kubernetes image store, without deploying Nexus or another registry. The operator owns runtime provisioning; local build tooling supplies images.
- Target identity: runtime deployment and attachment identify context, namespace and resource name. Attachment receipts additionally pin the resource UID.

## Constraints

No global kube-context change, automatic namespace wipe, credential output, or silent replacement of an existing operator installation. Existing shared infrastructure is preserved. A fresh installation creates the shared-infrastructure policy before the operator can apply its Nexus-enabled default. Image source is configurable. First tested engine is Rancher Moby; reject unsupported engines clearly.

## Non-goals

Windows/IIS, host agents inside images, a local registry, and implicit destructive takeover.
