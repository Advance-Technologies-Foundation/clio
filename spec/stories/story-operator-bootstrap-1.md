# Registry-free Rancher bootstrap

Status: in-progress

Given a running Rancher Moby Kubernetes cluster, `clio install-operator --target rancher-desktop` installs the operator with no Nexus resources and prints the dashboard URL. A context override targets only that context. Invalid targets fail before mutation. Existing dashboard credentials and shared infrastructure survive retries. A conflicting Nexus-managed policy fails before changes. Rollout failure returns nonzero.

Implemented and live-validated: local ZIP preparation/build in Rancher Moby without a registry, explicit-context runtime creation with template database restore, and two-way workspace attachment over SSH. FSM preparation and SSH authorization remain explicit prerequisite steps. Automatic environment login registration and external HTTPS MCP routing are outside this prototype.
