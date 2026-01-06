# assert

## Purpose

Validates infrastructure and filesystem resources to ensure clio can discover and connect to required components. The assert command uses the same discovery logic that clio uses during normal operations, ensuring that if assert passes, clio operations will succeed.

## Usage

```bash
clio assert <scope> [options]
```

## Arguments

### Required Arguments

| Argument | Description                                    | Values  |
|----------|------------------------------------------------|---------|
| `scope`  | Type of resources to validate                  | k8, fs  |

### Kubernetes Options

#### Context Validation
| Argument          | Short | Default | Description                              | Example                         |
|-------------------|-------|---------|------------------------------------------|---------------------------------|
| `--context`       | -     | -       | Expected Kubernetes context name         | `--context dev-cluster`         |
| `--context-regex` | -     | -       | Regex pattern for context name           | `--context-regex "^dev-.*"`     |
| `--cluster`       | -     | -       | Expected Kubernetes cluster name         | `--cluster my-cluster`          |
| `--namespace`     | -     | -       | Expected Kubernetes namespace            | `--namespace default`           |

#### Database Assertions
| Argument       | Short | Default | Description                                      | Example                    |
|----------------|-------|---------|--------------------------------------------------|----------------------------|
| `--db`         | -     | -       | Database engines (comma-separated)               | `--db postgres,mssql`      |
| `--db-min`     | -     | 1       | Minimum number of engines required               | `--db-min 2`               |
| `--db-connect` | -     | false   | Validate TCP connectivity                        | `--db-connect`             |
| `--db-check`   | -     | -       | Capability check (version)                       | `--db-check version`       |

#### Redis Assertions
| Argument          | Short | Default | Description                        | Example              |
|-------------------|-------|---------|------------------------------------|----------------------|
| `--redis`         | -     | false   | Assert Redis presence              | `--redis`            |
| `--redis-connect` | -     | false   | Validate TCP connectivity          | `--redis-connect`    |
| `--redis-ping`    | -     | false   | Execute Redis PING command         | `--redis-ping`       |

### Filesystem Options (Windows Only)

| Argument  | Short | Default | Description                              | Example                                  |
|-----------|-------|---------|------------------------------------------|------------------------------------------|
| `--path`  | -     | -       | Filesystem path to validate              | `--path "C:\inetpub\wwwroot\app"`        |
| `--user`  | -     | -       | Windows user identity                    | `--user "IIS APPPOOL\MyApp"`             |
| `--perm`  | -     | -       | Permission level (read/write/modify/full)| `--perm full`                            |

## Examples

### Context Validation

Basic context check:
```bash
clio assert k8
```

Validate specific context:
```bash
clio assert k8 --context dev-cluster
```

Context with regex pattern:
```bash
clio assert k8 --context-regex "^dev-.*"
```

### Database Validation

Check if Postgres exists:
```bash
clio assert k8 --db postgres
```

Multiple databases:
```bash
clio assert k8 --db postgres,mssql --db-min 2
```

Database with connectivity:
```bash
clio assert k8 --db postgres --db-connect
```

Full database validation with version check:
```bash
clio assert k8 --db postgres --db-connect --db-check version
```

### Redis Validation

Check if Redis exists:
```bash
clio assert k8 --redis
```

Redis with connectivity:
```bash
clio assert k8 --redis --redis-connect
```

Redis with PING test:
```bash
clio assert k8 --redis --redis-connect --redis-ping
```

### Complete Infrastructure Validation

```bash
clio assert k8 \
  --context dev-cluster \
  --db postgres,mssql --db-connect --db-check version \
  --redis --redis-connect --redis-ping
```

### Filesystem Validation (Windows, Not Yet Implemented)

```bash
clio assert fs --path "C:\inetpub\wwwroot\app\data"
```

```bash
clio assert fs --path "C:\data" --user "IIS APPPOOL\MyApp" --perm full
```

## Output

### Exit Codes
- `0` - Assertion passed (all checks succeeded)
- `1` - Assertion failed (at least one check failed)  
- `2` - Invalid invocation (wrong parameters or syntax)

### Success Output

```json
{
  "status": "pass",
  "context": {
    "name": "rancher-desktop",
    "cluster": "rancher-desktop",
    "server": "https://127.0.0.1:6443",
    "namespace": "default"
  },
  "resolved": {
    "databases": [
      {
        "engine": "postgres",
        "name": "clio-postgres",
        "host": "localhost",
        "port": 5432,
        "version": "PostgreSQL 18.1"
      }
    ],
    "redis": {
      "name": "clio-redis",
      "host": "localhost",
      "port": 6379
    }
  }
}
```

### Failure Output

```json
{
  "status": "fail",
  "scope": "K8",
  "failedAt": "DbConnect",
  "reason": "Cannot connect to postgres database at localhost:5432",
  "details": {
    "engine": "postgres",
    "host": "localhost",
    "port": 5432
  },
  "context": {
    "name": "rancher-desktop",
    "cluster": "rancher-desktop",
    "server": "https://127.0.0.1:6443"
  }
}
```

## How It Works

### Discovery Method

The assert command uses label-based discovery matching clio's k8Commands implementation:

1. **StatefulSets/Deployments**: Finds by checking `spec.selector.matchLabels` (not metadata labels)
2. **Services**: Discovers by label selector: `app=clio-postgres`, `app=clio-mssql`, `app=clio-redis`
3. **Pods**: Validates at least one pod is Running and Ready
4. **Ports**: Dynamically resolved from `Service.spec.ports` (no hardcoded values)
5. **Credentials**: Retrieved from Kubernetes secrets (same as k8Commands)

### Validation Phases

#### Phase 0: Context Validation (Mandatory)
- Resolves current kubectl context
- Validates API server connectivity
- Checks context name/cluster/namespace if specified
- Verifies `clio-infrastructure` namespace exists

#### Phase 1: Database Discovery
- Finds StatefulSets with matching selector labels
- Checks pod readiness status
- Discovers services by label

#### Phase 2: Database Connectivity (Optional)
- Tests TCP connection to database
- Uses dynamically resolved host and port

#### Phase 3: Database Capability (Optional)
- Retrieves credentials from secrets
- Executes version query:
  - Postgres: `SELECT version()`
  - MSSQL: `SERVERPROPERTY('ProductVersion')`

#### Phase 4-6: Redis Assertions
Similar phased approach for Redis validation

### Detection Rules

**Postgres Database:**
- Namespace: `clio-infrastructure`
- Workload: StatefulSet with selector `app=clio-postgres`
- Service: Label `app=clio-postgres`
- Secret: `clio-postgres-secret` (keys: `POSTGRES_USER`, `POSTGRES_PASSWORD`)

**MSSQL Database:**
- Namespace: `clio-infrastructure`
- Workload: StatefulSet with selector `app=clio-mssql`
- Service: Label `app=clio-mssql`
- Secret: `clio-mssql-secret` (key: `MSSQL_SA_PASSWORD`, user: `sa`)

**Redis:**
- Namespace: `clio-infrastructure`
- Workload: Deployment with selector `app=clio-redis`
- Service: Label `app=clio-redis`

### Service Resolution

- Services can have any name (e.g., `postgres-service-lb`, `redis-service-internal`)
- Discovery uses label selectors, not name patterns
- LoadBalancer services accessed via `localhost` in local clusters
- ClusterIP services accessed via DNS name: `<service>.<namespace>.svc.cluster.local`

## Use Cases

### Pre-Deployment Validation
Verify infrastructure before installing Creatio:
```bash
clio assert k8 --db postgres --redis --redis-connect
```

### CI/CD Pipeline Health Checks
Automated validation in deployment pipelines:
```bash
clio assert k8 --db postgres,mssql --db-connect --db-check version --redis --redis-ping
if [ $? -eq 0 ]; then
  echo "Infrastructure ready for deployment"
  clio install ...
else
  echo "Infrastructure validation failed"
  exit 1
fi
```

### Troubleshooting
Diagnose connectivity issues:
```bash
# Check if resources exist
clio assert k8 --db postgres --redis

# Check connectivity
clio assert k8 --db postgres --db-connect --redis-connect

# Full validation
clio assert k8 --db postgres --db-connect --db-check version
```

### Release Readiness
Validate all services before release:
```bash
clio assert k8 \
  --context production \
  --db postgres,mssql --db-min 2 --db-connect --db-check version \
  --redis --redis-connect --redis-ping
```

## Troubleshooting

### Context Issues

**Problem**: "current context 'X' does not match expected 'Y'"

**Solution**: Check your kubectl context
```bash
kubectl config current-context
kubectl config use-context <desired-context>
```

### Namespace Not Found

**Problem**: "Required namespace 'clio-infrastructure' does not exist"

**Solution**: Deploy infrastructure first
```bash
clio deploy-infrastructure
```

### Database Not Discovered

**Problem**: "Found 0 database(s), expected at least 1"

**Solution**: Verify StatefulSet and labels
```bash
kubectl get statefulset -n clio-infrastructure
kubectl get statefulset clio-postgres -n clio-infrastructure -o yaml | grep -A5 selector
```

Ensure the StatefulSet has `spec.selector.matchLabels.app: clio-postgres`

### Connectivity Failures

**Problem**: "Cannot connect to postgres database at localhost:5432"

**Solution**: Check pod and service status
```bash
kubectl get pods -n clio-infrastructure
kubectl get services -n clio-infrastructure
kubectl port-forward -n clio-infrastructure svc/postgres-service-lb 5432:5432
```

### Authentication Failures

**Problem**: "password authentication failed for user 'postgres'"

**Solution**: Verify secrets exist and are correct
```bash
kubectl get secret clio-postgres-secret -n clio-infrastructure
kubectl get secret clio-postgres-secret -n clio-infrastructure -o jsonpath='{.data.POSTGRES_USER}' | base64 -d
```

## Notes

### Design Principles
- **No hidden checks**: All validations explicit via CLI flags
- **Deterministic**: Same input always produces same output
- **Read-only**: No mutations, purely validation
- **Time-bounded**: Configurable timeouts on all operations
- **Structured output**: JSON format with precise failure attribution

### Compatibility
- Uses identical discovery logic as clio's operational code
- If assert passes, clio operations will succeed
- Validates the same resources clio needs for database restore, connection strings, etc.

### Limitations
- Filesystem assertions (Windows) not yet implemented
- No retry logic (fails fast)
- Database version check requires valid credentials in secrets
- Assumes standard Kubernetes configurations

### Security
- Credentials never exposed in output
- Reads from Kubernetes secrets using same method as clio
- No credential caching or persistence
