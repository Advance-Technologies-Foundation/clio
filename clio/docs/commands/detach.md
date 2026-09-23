# detach

Stop a local workspace attachment while preserving its runtime, database, and local and remote workspace files.

```shell
clio detach --workspace ./workspace
```

`--workspace` is required and must identify the original Clio workspace. Run this on the same host and with the same Mutagen user/data directory used for attach. Mutagen, OpenSSH, kubectl and access to the original Kubernetes context are required.

Detach pauses the owned session, preserves the runtime FSM links and terminates that session. Pending or conflicting changes remain in their respective copies; detach does not choose a winner or guarantee that pending changes were delivered. Other workspaces and Mutagen sessions are not changed. The receipt remains marked inactive for reattachment. If cleanup fails, it remains active so the operation can be retried. A replacement instance with the same name is never modified.

No runtime, database, files, SSH credentials or agent configuration is deleted. This command does not require ClioGate. See [attach](attach.md) for provider support and prerequisites.
