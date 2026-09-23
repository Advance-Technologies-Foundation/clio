# Runtime attachment boundary

Status: accepted for implementation following the architecture discussion.

Use a provider interface for discovery and remote preparation/cleanup, and a shared attachment service for dependency checks, receipts and Mutagen lifecycle. The operator Kubernetes provider reads CreatioInstance status and the image attachment descriptor. It does not deploy environments or infer private hostnames. Context and namespace are explicit optional CLI inputs.

Use existing SSH authentication supplied by the user through an SSH alias for this first slice. The alias is required explicitly; endpoint discovery verifies that the selected runtime is the intended target using its Kubernetes-provided host public key. Do not store a password or disable host-key checking. A later provider can provision keys without changing sync lifecycle.

Persist a receipt outside the synchronized package tree before creating sessions. Store the runtime UID, workspace path, remote path and unique session name. Use two-way-safe sync and flush before success. Never resolve conflicts by choosing a winner. Remote workspace files survive detach. Package links are only created for absent targets, and detachment preserves these links so the running application can continue to use its packages.

The CLI is a host-side bootstrap operation, not a remote MCP tool: running attach in a pod would attach the wrong machine. Existing Clio HTTP workspace-root support is a prerequisite for HTTP operations; do not bundle that independent prototype into this change. Connection information is written to the receipt; automatic agent MCP configuration follows once the attachment and ownership boundaries are validated.

This deliberately small first slice implements attach/detach and bidirectional package sync. Debugger and HTTP endpoints remain provider-owned and usable independently.
