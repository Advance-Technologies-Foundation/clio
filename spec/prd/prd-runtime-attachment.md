# Workspace attachment

Agents keep their local Clio workspaces and attach existing Kubernetes Creatio runtimes for development. The runtime provider owns deployment and endpoints. Clio owns the workspace attachment and continuous two-way-safe Mutagen synchronization over SSH.

First delivery supports the Creatio operator through the selected Kubernetes context, including local and remote clusters. Windows/IIS providers are out of scope. Mutagen, OpenSSH and kubectl are local prerequisites. Multiple workspaces must be supported without sharing writable package ownership accidentally.

Attach discovers the runtime, pins its SSH identity through Kubernetes, prepares an isolated remote workspace, starts synchronization and exposes runtime connection information. Detach stops only attachment-owned synchronization; it never deletes the runtime, database, or either copy of workspace files. Configuration failures remain diagnosable and cleanup is repeatable.
