# CreateK8FilesCommand

## Overview

The `CreateK8FilesCommand` generates Kubernetes deployment configuration files for Creatio infrastructure components with configurable resource limits. This command prepares YAML manifests for PostgreSQL, MSSQL, Redis, and supporting services with customizable CPU and memory allocations.

## Command Aliases

- `create-k8-files`
- `ck8f`

## Command Options

### PostgreSQL Resource Configuration

| Option                 | Description                       | Type     | Default | Required |
|------------------------|-----------------------------------|----------|---------|----------|
| `--pg-limit-memory`    | PostgreSQL memory limit           | `string` | `4Gi`   | No       |
| `--pg-limit-cpu`       | PostgreSQL CPU limit              | `string` | `2`     | No       |
| `--pg-request-memory`  | PostgreSQL memory request         | `string` | `2Gi`   | No       |
| `--pg-request-cpu`     | PostgreSQL CPU request            | `string` | `1`     | No       |

### MSSQL Resource Configuration

| Option                   | Description                     | Type     | Default | Required |
|--------------------------|---------------------------------|----------|---------|----------|
| `--mssql-limit-memory`   | MSSQL memory limit              | `string` | `4Gi`   | No       |
| `--mssql-limit-cpu`      | MSSQL CPU limit                 | `string` | `2`     | No       |
| `--mssql-request-memory` | MSSQL memory request            | `string` | `2Gi`   | No       |
| `--mssql-request-cpu`    | MSSQL CPU request               | `string` | `1`     | No       |

## Resource Format

### Memory Sizes
Specify memory in Kubernetes notation:
- `Gi` - Gibibytes (e.g., `4Gi`, `2Gi`, `512Mi`)
- `Mi` - Mebibytes (e.g., `512Mi`, `256Mi`)
- `G` - Gigabytes (e.g., `4G`, `2G`)
- `M` - Megabytes (e.g., `512M`, `256M`)

### CPU Values
Specify CPU as decimal numbers representing cores:
- `2` - 2 CPU cores
- `1` - 1 CPU core
- `0.5` - Half a CPU core
- `0.25` - Quarter of a CPU core

## Usage Examples

### Basic Usage with Default Resources

Generate files with default resource allocations (4Gi/2CPU limit, 2Gi/1CPU request for both databases):

```bash
clio create-k8-files
```

or using alias:

```bash
clio ck8f
```

### Custom PostgreSQL Resources for Production

Configure PostgreSQL with higher resources for production workload:

```bash
clio create-k8-files \
  --pg-limit-memory 8Gi \
  --pg-limit-cpu 4 \
  --pg-request-memory 4Gi \
  --pg-request-cpu 2
```

### Custom MSSQL Resources

Configure MSSQL with custom resource allocation:

```bash
clio create-k8-files \
  --mssql-limit-memory 8Gi \
  --mssql-limit-cpu 4 \
  --mssql-request-memory 4Gi \
  --mssql-request-cpu 2
```

### Configure Both Databases

Set custom resources for both PostgreSQL and MSSQL:

```bash
clio create-k8-files \
  --pg-limit-memory 8Gi --pg-limit-cpu 4 \
  --pg-request-memory 4Gi --pg-request-cpu 2 \
  --mssql-limit-memory 6Gi --mssql-limit-cpu 3 \
  --mssql-request-memory 3Gi --mssql-request-cpu 1.5
```

### Development Environment

Minimal resources for local development:

```bash
clio create-k8-files \
  --pg-limit-memory 2Gi --pg-limit-cpu 1 \
  --mssql-limit-memory 2Gi --mssql-limit-cpu 1
```

### High-Load Environment

Maximum resources for high-traffic production:

```bash
clio create-k8-files \
  --pg-limit-memory 16Gi --pg-limit-cpu 8 \
  --pg-request-memory 8Gi --pg-request-cpu 4 \
  --mssql-limit-memory 16Gi --mssql-limit-cpu 8 \
  --mssql-request-memory 8Gi --mssql-request-cpu 4
```

## Functionality

### What the Command Does

1. **Copies Template Files**: Copies Kubernetes YAML templates from the clio installation directory to your local infrastructure directory
2. **Variable Substitution**: Replaces resource placeholders (`{{PG_LIMIT_MEMORY}}`, etc.) with specified or default values
3. **Displays Configuration**: Shows the configured resource values for both databases
4. **Provides Guidance**: Displays important information about deployment and next steps

### Output Directory

Files are generated in:
- **Windows**: `%LOCALAPPDATA%\creatio\clio\infrastructure`
- **macOS/Linux**: `~/.local/creatio/clio/infrastructure`

### Generated Files Structure

```
infrastructure/
├── clio-namespace.yaml
├── clio-storage-class.yaml
├── postgres/
│   ├── postgres-secrets.yaml
│   ├── postgres-volumes.yaml
│   ├── postgres-services.yaml
│   └── postgres-stateful-set.yaml    # Resource limits applied here
├── mssql/
│   ├── mssql-secrets.yaml
│   ├── mssql-services.yaml
│   └── mssql-stateful-set.yaml       # Resource limits applied here
├── redis/
│   ├── redis-workload.yaml
│   └── redis-services.yaml
└── pgadmin/
    ├── pgadmin-secrets.yaml
    ├── pgadmin-volumes.yaml
    ├── pgadmin-services.yaml
    └── pgadmin-workload.yaml
```

## Resource Planning Guidelines

### Environment Sizing Recommendations

#### Development Environment
For local development and testing:
```bash
clio create-k8-files \
  --pg-limit-memory 2Gi --pg-limit-cpu 1 \
  --mssql-limit-memory 2Gi --mssql-limit-cpu 1
```

**Cluster Requirements**: 4GB RAM, 2 CPU cores minimum

#### Staging/QA Environment
For quality assurance and staging (default values):
```bash
clio create-k8-files
```

**Cluster Requirements**: 8GB RAM, 4 CPU cores minimum

#### Production Environment
For production workloads:
```bash
clio create-k8-files \
  --pg-limit-memory 8Gi --pg-limit-cpu 4 \
  --mssql-limit-memory 8Gi --mssql-limit-cpu 4
```

**Cluster Requirements**: 16GB RAM, 8 CPU cores minimum

#### High-Traffic Production
For high-load production environments:
```bash
clio create-k8-files \
  --pg-limit-memory 16Gi --pg-limit-cpu 8 \
  --mssql-limit-memory 16Gi --mssql-limit-cpu 8
```

**Cluster Requirements**: 32GB RAM, 16 CPU cores minimum

### Understanding Requests vs Limits

- **Requests**: Guaranteed resources reserved for the pod
  - Used for scheduling decisions
  - Pod won't start if cluster lacks requested resources
  
- **Limits**: Maximum resources the pod can use
  - Pod is throttled (CPU) or killed (memory) if exceeded
  - Prevents resource exhaustion

**Best Practice**: Set requests at 50% of limits for production workloads

## Deployment Workflow

### Recommended Workflow

1. **Generate Files**: Create infrastructure files with appropriate resources
2. **Review Configuration**: Inspect generated YAML files
3. **Deploy Infrastructure**: Apply configurations to Kubernetes
4. **Verify Deployment**: Check pod status and resource allocation

### Complete Example

```bash
# Step 1: Generate files with production resources
clio create-k8-files \
  --pg-limit-memory 8Gi --pg-limit-cpu 4 \
  --mssql-limit-memory 8Gi --mssql-limit-cpu 4

# Step 2: Review generated files (optional)
clio open-k8-files

# Step 3: Deploy infrastructure automatically
clio deploy-infrastructure

# Step 4: Verify deployment
kubectl get pods -n clio-infrastructure
kubectl top pods -n clio-infrastructure
```

## Integration with deploy-infrastructure

The `deploy-infrastructure` command automatically calls `create-k8-files` with default resource values. To customize resources:

**Option 1**: Run `create-k8-files` manually first
```bash
clio create-k8-files --pg-limit-memory 8Gi --pg-limit-cpu 4
clio deploy-infrastructure
```

**Option 2**: Edit generated files manually
```bash
clio create-k8-files
clio open-k8-files
# Edit YAML files manually
clio deploy-infrastructure
```

## When to Use

Use `create-k8-files` in these scenarios:

- **Initial Setup**: Preparing infrastructure for first-time Creatio deployment
- **Resource Optimization**: Adjusting database resources for performance tuning
- **Environment Migration**: Creating configurations for new environments
- **Capacity Planning**: Testing different resource allocations
- **Documentation**: Generating reference configurations for team
- **CI/CD Integration**: Automating infrastructure provisioning

## Prerequisites

- Clio installed and accessible in PATH
- Write permissions to user settings directory
- Understanding of Kubernetes resource management
- Knowledge of expected database workload

## Return Values

- **0**: Files generated successfully
- **1**: An error occurred (e.g., permission denied, disk full)

## Output

The command displays:

1. **Resource Configuration Summary**
   ```
   Resource Configuration:
     PostgreSQL: Memory Limit=4Gi, CPU Limit=2
                 Memory Request=2Gi, CPU Request=1
     MSSQL:      Memory Limit=4Gi, CPU Limit=2
                 Memory Request=2Gi, CPU Request=1
   ```

2. **Important Notices**
   - Location of generated files
   - Review recommendations
   - Kubernetes context warnings

3. **Service Information Table**
   - Available services (PostgreSQL, MSSQL, Redis, Email Listener)
   - Version information
   - Port mappings

4. **Deployment Instructions**
   - Manual deployment commands
   - Automated deployment reference

## Files to Review

After generation, review these critical files:

### postgres-stateful-set.yaml
Check resource limits in the PostgreSQL container spec:
```yaml
resources:
  limits:
    memory: "4Gi"
    cpu: "2"
  requests:
    memory: "2Gi"
    cpu: "1"
```

### mssql-stateful-set.yaml
Check resource limits in the MSSQL container spec:
```yaml
resources:
  limits:
    memory: "4Gi"
    cpu: "2"
  requests:
    memory: "2Gi"
    cpu: "1"
```

### postgres-volumes.yaml
Verify storage allocations:
```yaml
resources:
  requests:
    storage: 20Gi  # PostgreSQL data
```

### mssql-stateful-set.yaml
Verify storage allocations:
```yaml
resources:
  requests:
    storage: 20Gi  # MSSQL data
```

## Error Handling

The command includes error handling for:

- **Template files not found**: Missing clio installation files
- **Permission denied**: Insufficient write permissions
- **Disk space**: Insufficient storage for file generation
- **Invalid parameters**: Malformed memory or CPU values

## Related Commands

- [`deploy-infrastructure`](./DeployInfrastructureCommand.md): Deploys generated files to Kubernetes
- [`open-k8-files`](#): Opens infrastructure directory in file explorer
- [`delete-infrastructure`](./DeleteInfrastructureCommand.md): Removes deployed infrastructure

## Troubleshooting

### Common Issues

#### Issue: "Template files not found"
**Cause**: Clio installation is corrupted or incomplete

**Solution**:
```bash
# Reinstall clio
dotnet tool uninstall clio -g
dotnet tool install clio -g
```

#### Issue: Pods in "Pending" state after deployment
**Cause**: Insufficient cluster resources to meet requests

**Solution**: Reduce resource requests or increase cluster capacity
```bash
# Generate with lower requests
clio create-k8-files \
  --pg-request-memory 1Gi --pg-request-cpu 0.5 \
  --mssql-request-memory 1Gi --mssql-request-cpu 0.5
```

#### Issue: Pods killed with "OOMKilled" status
**Cause**: Memory limit too low for workload

**Solution**: Increase memory limits
```bash
clio create-k8-files \
  --pg-limit-memory 8Gi \
  --mssql-limit-memory 8Gi
```

#### Issue: CPU throttling in production
**Cause**: CPU limit too restrictive

**Solution**: Increase CPU limits
```bash
clio create-k8-files \
  --pg-limit-cpu 4 \
  --mssql-limit-cpu 4
```

### Verification

Verify generated files contain correct values:

**Windows**:
```powershell
Get-Content "$env:LOCALAPPDATA\creatio\clio\infrastructure\postgres\postgres-stateful-set.yaml" | Select-String -Pattern "memory:|cpu:"
```

**macOS/Linux**:
```bash
grep -A 2 "resources:" ~/.local/creatio/clio/infrastructure/postgres/postgres-stateful-set.yaml
```

## Best Practices

1. **Start Conservative**: Begin with default resources and scale up based on monitoring
2. **Monitor Usage**: Use `kubectl top pods` to track actual resource consumption
3. **Set Proper Ratios**: Keep requests at 50-70% of limits
4. **Match Workload**: Size resources based on expected database load
5. **Version Control**: Store generated configurations in Git for team collaboration
6. **Document Changes**: Keep notes on why specific resource values were chosen
7. **Test First**: Validate configurations in non-production before production deployment
8. **Regular Review**: Periodically review and adjust resources based on actual usage

## Security Considerations

- **Secrets Management**: Generated files include references to Kubernetes secrets
- **File Permissions**: Infrastructure directory contains sensitive configuration
- **Review Before Deploy**: Always inspect generated files before deployment
- **Access Control**: Limit access to infrastructure directory
- **Audit Trail**: Track changes to infrastructure configurations

## Technical Implementation

- **Command Class**: `CreateInfrastructureCommand`
- **Options Class**: `CreateInfrastructureOptions`
- **Base Class**: `Command<CreateInfrastructureOptions>`
- **Template Processing**: String replacement with Dictionary-based substitution
- **File System**: Uses `IFileSystem` abstraction for testability

## Performance Considerations

- **File Generation**: Fast, typically completes in < 1 second
- **Disk Usage**: ~500KB for generated infrastructure files
- **Network Impact**: None (local operation only)
- **CPU Usage**: Minimal (file I/O and string replacement)

## Version Compatibility

- **Kubernetes**: Tested with Kubernetes 1.24+
- **Databases**: PostgreSQL 16, MSSQL Server 2022
- **Clio**: Version 8.0.1.71+
- **Docker Desktop**: Compatible with local Kubernetes
- **Rancher Desktop**: Fully supported
- **Minikube**: Compatible

## Additional Resources

- [Kubernetes Resource Management](https://kubernetes.io/docs/concepts/configuration/manage-resources-containers/)
- [PostgreSQL Performance Tuning](https://www.postgresql.org/docs/current/runtime-config-resource.html)
- [MSSQL Memory Configuration](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/server-memory-server-configuration-options)
- [Creatio Installation Guide](https://academy.creatio.com/docs/user/on_site_deployment)
