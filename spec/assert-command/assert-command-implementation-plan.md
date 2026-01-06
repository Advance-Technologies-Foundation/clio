# Implementation Plan: `assert` Command (Agent-Friendly Validation CLI)

## Goals

- Provide a **single, deterministic assertion mechanism** for AI agents and humans
- Eliminate agent-side reasoning (no kubectl, sql, fs inspection by agents)
- Keep CLI **flat, explicit, and composable**
- Separate **existence**, **connectivity**, and **capability** checks
- Return **structured output** with precise failure attribution

---

## Non-Goals

- No auto-fixing or mutation of state
- No hidden checks
- No interactive prompts
- No environment-specific heuristics leaking into CLI semantics

---

## High-Level Architecture

```
assert
 ├── k8
 │    ├── db
 │    ├── redis
 │    └── shared k8 client
 └── fs
      └── acl (Windows)
```

Each scope implements:
- **Discovery**
- **Validation phases**
- **Structured result emission**

---

## CLI Grammar (Final)

### Kubernetes

```bash
assert k8 \
  --db postgres,mssql \
  --db-min 1 \
  --db-connect \
  --db-check version \
  --redis \
  --redis-connect \
  --redis-ping
```

### Filesystem (Windows)

```bash
assert fs \
  --path "C:\inetpub\wwwroot\app\data" \
  --user "IIS APPPOOL\MyAppPool" \
  --perm full
```

---

## Flag Semantics (Contract)

### Common Rules

- Flags without values → boolean
- Flags with values → consume next token
- No implicit side effects
- Each flag answers **one question**

---

### `assert k8`

#### `--db <engines>`
- Asserts DB presence only
- Engines: `postgres`, `mssql`
- Detection via **authoritative labels and workload kind only**
- No image-name inspection
- No Helm metadata dependency

Detection rules:
- Namespace: `clio-infrastructure`
- Workload kind:
  - Postgres → `StatefulSet`
  - MSSQL → `StatefulSet`
- Label selector:
  - Postgres → `app=clio-postgres`
  - MSSQL → `app=clio-mssql`
- At least one Pod exists and is not permanently failed

Service objects and ports are **not** required for presence detection.

#### `--db-min <n>`
- Minimum number of DB engines that must exist
- Default: `1`

#### `--db-connect`
- TCP connect succeeds to at least one resolved DB
- Uses provided credentials
- No SQL

#### `--db-check version`
- Engine-specific version check
- Internal mapping:
  - Postgres → `SELECT version()`
  - MSSQL → `SERVERPROPERTY('ProductVersion')`

---

#### `--redis`
- Redis presence only
- Detection via **authoritative labels and workload kind only**
- No port-based detection
- No image-name inspection

Detection rules:
- Namespace: `clio-infrastructure`
- Workload kind: `Deployment`
- Label selector: `app=clio-redis`
- At least one Pod exists and is not permanently failed

Service objects and ports are **not** required for presence detection.

#### `--redis-connect`
- TCP connect succeeds

#### `--redis-ping`
- `PING` → `PONG`

---

### `assert fs`

#### `--path <path>`
- Path exists

#### `--user <identity>`
- Windows principal exists
- Supports:
  - Local users
  - Domain users
  - `IIS APPPOOL\*`

#### `--perm <level>`
- Required permission level:
  - `read`
  - `write`
  - `modify`
  - `full`
- Windows ACL evaluation rules:
  - Inheritance applies
  - Deny overrides allow
  - Effective permissions evaluated

---

---

## Kubernetes Context Assertion (Phase 0 – Mandatory)

Before any Kubernetes resource checks are executed, `assert k8` **must validate the active kubectl context**.  
This prevents accidental validation against the wrong cluster on developer machines with multiple contexts.

### Default Behavior

When running:

```bash
assert k8
```

The tool must:

1. Resolve the current kubectl context
2. Verify the Kubernetes API server is reachable
3. Capture and report context metadata in output

Minimum checks:
- `kubectl config current-context`
- API call to `/version` or equivalent client check

Example success output:

```json
{
  "status": "pass",
  "context": {
    "name": "dev-cluster",
    "cluster": "dev",
    "server": "https://10.0.0.1"
  }
}
```

If the context cannot be resolved or the API is unreachable, **execution must stop immediately**.

---

### Context Assertion Flags (Optional but Recommended)

These flags **assert expectations** about the active context.  
They must **not** modify kubeconfig or switch contexts.

```bash
--context <name>           # exact context name match
--context-regex <pattern>  # regex match on context name
--cluster <name>           # cluster name match
--namespace <ns>           # default namespace assertion
```

Examples:

```bash
assert k8 --context dev
```

```bash
assert k8 --context-regex '^dev-.*'
```

```bash
assert k8 --cluster my-dev-cluster --namespace app
```

---

### Failure Examples

Wrong context:

```json
{
  "status": "fail",
  "failedAt": "k8-context",
  "reason": "current context 'prod-cluster' does not match expected 'dev'"
}
```

Unreachable cluster:

```json
{
  "status": "fail",
  "failedAt": "k8-context",
  "reason": "API server unreachable"
}
```

---

---

## Dynamic Service Port Resolution (Mandatory)

`assert` must **never assume default ports** for databases or Redis.

All connectivity checks must resolve ports **dynamically from Kubernetes Service objects**, exactly as the application runtime does.

### Rules

- Ports are discovered from `Service.spec.ports`
- Connections must use:
  - `port` (service port), not `targetPort`
- `targetPort` is informational only
- Hardcoded defaults (5432, 1433, 6379) are **not allowed** in assert logic

This design allows developers to:
- Avoid port collisions with locally installed databases
- Freely customize manifests without breaking assertions

---

### Service Selection

Depending on execution environment:

| Execution Location | Service Used |
|-------------------|-------------|
| Inside cluster | `<service>-internal` |
| Outside cluster | `<service>-lb` |

Fail if the required service does not exist.

---

### Port Resolution Algorithm

1. Read `Service.spec.ports`
2. If no ports exist → fail
3. If one port exists → use it
4. If multiple ports exist:
  - Use the first port
  - Future extension: explicit port name flag

---

## Execution Phases

### Phase Order (K8)

1. Discover cluster context
2. Resolve requested DB engines
3. Enforce `--db-min`
4. Connectivity checks (`--db-connect`)
5. Capability checks (`--db-check`)
6. Redis checks (presence → connect → ping)

### Phase Order (FS)

1. Path existence
2. Identity resolution
3. ACL evaluation

---

## Error Attribution

Each failure must specify:

```json
{
  "status": "fail",
  "scope": "k8|fs",
  "failedAt": "<phase>",
  "reason": "<human-readable>",
  "details": {}
}
```

Examples:
- `db-discovery`
- `db-connect`
- `db-check`
- `redis-ping`
- `fs-path`
- `fs-user`
- `fs-perm`

---

## Exit Codes

| Code | Meaning |
|----|--------|
| 0 | Assertion passed |
| 1 | Assertion failed |
| 2 | Invalid invocation / spec |

---

## Output Contract

### Success

```json
{
  "status": "pass",
  "resolved": {
    "db": ["postgres"],
    "redis": true
  }
}
```

### Failure

```json
{
  "status": "fail",
  "failedAt": "db-connect",
  "engine": "postgres",
  "reason": "timeout"
}
```

---

## Internal Design Guidelines

- Normalize flags → internal assertion graph
- No flag implies behavior beyond its name
- All checks must be:
  - Idempotent
  - Read-only
  - Time-bounded
- No retries unless explicitly added later

---

## Testing Strategy

### Unit
- Flag parsing
- Assertion graph normalization
- Engine detection heuristics

### Integration
- Real K8 cluster (kind / k3s)
- Postgres + MSSQL containers
- Redis auth/no-auth cases
- Windows ACL scenarios

---

## Documentation (Required)

- `assert --help` must list **exact semantics**
- Each flag documented as:
  - What it checks
  - What it does NOT check
- Include 3–5 canonical examples
- No prose-only descriptions

---

## Future Extensions (Not Implement Now)

- Linux filesystem perms
- `--service` generic abstraction
- Credential providers
- JSON output schema versioning

---

## Final Rule

> **If a check runs, it must be spelled out in the CLI.**

No exceptions.


---

## Namespace Contract

All Kubernetes assertions are strictly scoped to the namespace:

```
clio-infrastructure
```

- Namespace must exist
- No cross-namespace discovery is permitted
- Missing namespace results in immediate failure

---
