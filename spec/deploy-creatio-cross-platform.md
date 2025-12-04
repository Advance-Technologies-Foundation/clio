# Analysis and Improvement of deploy-creatio Command

## Current State

### Main File: `CreatioInstallerService.cs`

The `deploy-creatio` command (aliases: `dc`, `ic`, `install-creation`) currently:

**Mandatory uses IIS for deployment:**
```csharp
int createSiteResult = dbRestoreResult switch {
    0 => CreateIISSite(unzippedDirectory, options).GetAwaiter().GetResult(),
    _ => ExitWithErrorMessage("Database restore failed")
};
```

**Issues with current implementation:**
1. ❌ Tightly coupled to IIS (Windows only)
2. ❌ Doesn't work on macOS and Linux (cannot create IIS site)
3. ❌ No way to explicitly disable IIS on Windows
4. ❌ No automatic OS detection
5. ❌ No direct launch via `dotnet Terrasoft.WebHost.dll` on other platforms

### Deployment Architecture:

```
Execute()
  ├─ Unpack ZIP file
  ├─ Check and create DB (MSSQL or PostgreSQL)
  ├─ Initialize infrastructure (Redis, pgAdmin, PostgreSQL in K8s)
  ├─ Start application (IIS on Windows or dotnet on macOS/Linux)
  ├─ Update ConnectionString
  └─ Register application
```

## Required Improvements

### 1. Automatic Platform Detection

System should automatically detect OS and select appropriate deployment method:

```csharp
public enum DeploymentPlatform
{
    Windows,      // IIS
    macOS,        // dotnet run / Terrasoft.WebHost.dll
    Linux         // dotnet run / Terrasoft.WebHost.dll
}

private DeploymentPlatform DetectPlatform()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return DeploymentPlatform.Windows;
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return DeploymentPlatform.macOS;
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        return DeploymentPlatform.Linux;
    
    throw new PlatformNotSupportedException("Unknown platform");
}
```

### 2. New Command Parameters

```csharp
[Verb("deploy-creatio", ...)]
public class PfInstallerOptions : EnvironmentNameOptions
{
    // Existing parameters...
    
    /// <summary>
    /// Deployment method (auto, iis, dotnet)
    /// </summary>
    [Option("deployment", Required = false, Default = "auto", 
        HelpText = "Deployment method: auto|iis|dotnet")]
    public string DeploymentMethod { get; set; }
    
    /// <summary>
    /// Explicitly disable IIS even on Windows
    /// </summary>
    [Option("no-iis", Required = false, Default = false,
        HelpText = "Don't use IIS on Windows (use dotnet run instead)")]
    public bool NoIIS { get; set; }
    
    /// <summary>
    /// Path for application deployment
    /// </summary>
    [Option("app-path", Required = false,
        HelpText = "Application installation path")]
    public string AppPath { get; set; }
    
    /// <summary>
    /// Use SSL/HTTPS for application
    /// </summary>
    [Option("use-https", Required = false, Default = false,
        HelpText = "Use HTTPS (requires certificate for dotnet)")]
    public bool UseHttps { get; set; }
    
    /// <summary>
    /// Path to SSL certificate (.pem or .pfx)
    /// </summary>
    [Option("cert-path", Required = false,
        HelpText = "Path to SSL certificate file (.pem or .pfx)")]
    public string CertificatePath { get; set; }
    
    /// <summary>
    /// Password for SSL certificate (if required)
    /// </summary>
    [Option("cert-password", Required = false,
        HelpText = "Password for SSL certificate")]
    public string CertificatePassword { get; set; }
    
    /// <summary>
    /// Automatically run application after deployment
    /// </summary>
    [Option("auto-run", Required = false, Default = true,
        HelpText = "Automatically run application after deployment")]
    public bool AutoRun { get; set; }
}
```

### 3. Deployment Strategy Interface and Implementation

```csharp
/// <summary>
/// Interface for application deployment strategy
/// </summary>
public interface IDeploymentStrategy
{
    /// <summary>
    /// Check if this strategy is applicable on current platform
    /// </summary>
    bool CanDeploy();
    
    /// <summary>
    /// Deploy application
    /// </summary>
    Task<int> Deploy(DirectoryInfo appDirectory, PfInstallerOptions options);
    
    /// <summary>
    /// Get application URL
    /// </summary>
    string GetApplicationUrl(PfInstallerOptions options);
    
    /// <summary>
    /// Get strategy description
    /// </summary>
    string GetDescription();
}

// Concrete implementations:
public class IISDeploymentStrategy : IDeploymentStrategy { }
public class DotNetDeploymentStrategy : IDeploymentStrategy { }
```

### 4. New Execute() Architecture

```csharp
public override int Execute(PfInstallerOptions options)
{
    // Initialization
    ValidateOptions(options);
    DirectoryInfo unzippedDirectory = PrepareApplication(options);
    
    // Select deployment strategy
    IDeploymentStrategy deploymentStrategy = SelectDeploymentStrategy(options);
    
    _logger.WriteInfo($"Selected deployment strategy: {deploymentStrategy.GetDescription()}");
    _logger.WriteInfo($"Platform: {RuntimeInformation.OSDescription}");
    
    // Database preparation (same for all platforms)
    int dbRestoreResult = PrepareDatabase(unzippedDirectory, options);
    if (dbRestoreResult != 0)
        return ExitWithErrorMessage("Database preparation failed");
    
    // Application deployment (depends on strategy)
    int deployResult = deploymentStrategy
        .Deploy(unzippedDirectory, options)
        .GetAwaiter()
        .GetResult();
    
    if (deployResult != 0)
        return ExitWithErrorMessage("Application deployment failed");
    
    // Post-deployment operations
    string appUrl = deploymentStrategy.GetApplicationUrl(options);
    
    int updateConnectionStringResult = UpdateConnectionString(unzippedDirectory, options)
        .GetAwaiter()
        .GetResult();
    
    if (updateConnectionStringResult != 0)
        return ExitWithErrorMessage("Failed to update ConnectionString");
    
    // Register in clio
    RegisterApplication(options, appUrl);
    
    return 0;
}

private IDeploymentStrategy SelectDeploymentStrategy(PfInstallerOptions options)
{
    // If explicitly disabled, don't use IIS
    if (options.NoIIS && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        _logger.WriteInfo("IIS explicitly disabled, using dotnet run");
        return _serviceProvider.GetRequiredService<DotNetDeploymentStrategy>();
    }
    
    // If method explicitly specified
    if (!string.IsNullOrEmpty(options.DeploymentMethod) && options.DeploymentMethod != "auto")
    {
        return SelectStrategyByName(options.DeploymentMethod);
    }
    
    // Auto-detection by OS
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return _serviceProvider.GetRequiredService<IISDeploymentStrategy>();
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return _serviceProvider.GetRequiredService<DotNetDeploymentStrategy>();
    
    throw new PlatformNotSupportedException("Current platform is not supported");
}

private IDeploymentStrategy SelectStrategyByName(string methodName)
{
    return methodName.ToLower() switch
    {
        "iis" => _serviceProvider.GetRequiredService<IISDeploymentStrategy>(),
        "dotnet" => _serviceProvider.GetRequiredService<DotNetDeploymentStrategy>(),
        _ => throw new ArgumentException($"Unknown deployment method: {methodName}")
    };
}
```

## Usage Examples

### Example 1: Automatic Selection (recommended)
```bash
# On Windows → uses IIS
clio deploy-creatio --ZipFile creatio.zip

# On macOS → uses dotnet run
clio deploy-creatio --ZipFile creatio.zip

# On Linux → uses dotnet run
clio deploy-creatio --ZipFile creatio.zip
```

### Example 2: Explicitly specify Windows without IIS (dotnet)
```bash
clio deploy-creatio --ZipFile creatio.zip --no-iis
# or
clio deploy-creatio --ZipFile creatio.zip --deployment dotnet
```

### Example 3: Full parameter specification
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName my-app \
  --SitePort 8080 \
  --deployment dotnet \
  --app-path /opt/creatio \
  --db pg \
  --platform net6
```

### Example 4: With HTTPS/SSL Certificate
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName my-app \
  --SitePort 443 \
  --deployment dotnet \
  --use-https \
  --cert-path /etc/ssl/certs/my-app.pfx \
  --cert-password "certificate-password"
```

### Example 5: Without Automatic Startup (for debugging)
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName dev-app \
  --no-iis \
  --auto-run false
```

### Example 6: IIS Deployment on Windows (explicit)
```bash
clio deploy-creatio \
  --ZipFile creatio.zip \
  --SiteName prod-app \
  --SitePort 80 \
  --deployment iis \
  --db mssql
```

## Deployment Strategy Implementation

### 1. IIS Strategy (Windows only)
**Condition**: Windows OS, `--deployment iis` or automatically on Windows (if not `--no-iis`)

**Actions**:
- Create IIS AppPool
- Copy files to `%iis-clio-root-path%\{siteName}`
- Create IIS website
- Configure HTTP/HTTPS bindings
- Start application through IIS
- Register in clio

### 2. DotNet Strategy (macOS, Linux, Windows)
**Condition**: `--deployment dotnet` or automatically on macOS/Linux

**Actions**:
- Copy application files to `{AppPath}` (default: `~/creatio/{SiteName}`)
- Create `appsettings.json` configuration with parameters:
  - Port: `{SitePort}`
  - ConnectionString: from DB config
  - HTTPS (optional): certificate and key
- Create systemd service (Linux) or launchd (macOS) for auto-launch:
  ```
  [Unit]
  Description=Creatio Application {SiteName}
  After=network.target
  
  [Service]
  Type=simple
  User=creatio
  WorkingDirectory=/opt/creatio/{SiteName}
  ExecStart=/usr/bin/dotnet Terrasoft.WebHost.dll
  Restart=on-failure
  RestartSec=10
  Environment="ASPNETCORE_URLS=http://0.0.0.0:{SitePort}"
  Environment="ASPNETCORE_ENVIRONMENT=Production"
  
  [Install]
  WantedBy=multi-user.target
  ```
- Start service: `systemctl start creatio-{SiteName}` (Linux)
- Verify application available at `http://localhost:{SitePort}`
- Register in clio

**Features**:
- On macOS: uses `launchctl` for service management
- On Linux: uses `systemd` for service management
- On Windows (with --no-iis): runs process via console or background task
- Automatic restart on failure

## Infrastructure (same for all platforms)

Uses Kubernetes in local cluster (via Docker Desktop on macOS/Windows or minikube on Linux):

```yaml
Namespace: creatio
Services:
  ├─ PostgreSQL (StatefulSet + Service)
  ├─ Redis (Deployment + Service)
  └─ pgAdmin (Deployment + Service)
```

**Infrastructure commands** (executed before application deployment):
```bash
kubectl apply -f clio-namespace.yaml
kubectl apply -f postgres/
kubectl apply -f redis/
kubectl apply -f pgadmin/
```

**Environment variables** for connection:
```
DATABASE_HOST=postgres.creatio.svc.cluster.local
DATABASE_PORT=5432
REDIS_HOST=redis.creatio.svc.cluster.local
REDIS_PORT=6379
```

## Platform Compatibility Matrix

| Strategy | Windows | macOS | Linux | Requirements |
|----------|---------|-------|-------|-----------|
| IIS      | ✅      | ❌    | ❌    | Windows + IIS installed |
| DotNet   | ✅*     | ✅    | ✅    | .NET 6+ SDK |

*On Windows with `--no-iis` flag

## deploy-creatio Command Execution Flow

```
1. Validate parameters
   ├─ Check ZIP file
   ├─ Check ports (1-65535)
   └─ Check paths
   
2. Determine platform and strategy
   ├─ Detect OS (Windows/macOS/Linux)
   ├─ Check flags (--no-iis, --deployment)
   └─ Select deployment strategy
   
3. Prepare application
   ├─ Unpack ZIP
   ├─ Copy files to target directory
   └─ Initialize directory structure
   
4. Prepare infrastructure (K8s)
   ├─ Create namespace
   ├─ Deploy PostgreSQL
   ├─ Deploy Redis
   └─ Deploy pgAdmin
   
5. Prepare database
   ├─ Run migrations
   ├─ Initialize schemas
   └─ Create system data
   
6. Deploy application (depends on strategy)
   ├─ [IIS] Create AppPool, site, bindings
   └─ [DotNet] Create service, copy configs
   
7. Configure application
   ├─ Update ConnectionString
   ├─ Set HTTPS (if needed)
   └─ Update application parameters
   
8. Start application
   ├─ Start service
   ├─ Check availability (health check)
   └─ Wait for readiness
   
9. Register in clio
   ├─ Add environment
   ├─ Configure connection parameters
   └─ Check connection
   
10. Completion
    ├─ Output URL for access
    ├─ Open browser (if --auto-run)
    └─ Output status
```

## Files to Create/Modify

### New files:
1. `Common/DeploymentStrategies/IDeploymentStrategy.cs` - strategy interface
2. `Common/DeploymentStrategies/IISDeploymentStrategy.cs` - IIS deployment (Windows)
3. `Common/DeploymentStrategies/DotNetDeploymentStrategy.cs` - dotnet deployment (macOS/Linux/Windows)
4. `Common/DeploymentStrategies/DeploymentStrategyFactory.cs` - factory for strategy selection
5. `Common/SystemServices/ISystemServiceManager.cs` - interface for service management
6. `Common/SystemServices/LinuxSystemServiceManager.cs` - systemd service management
7. `Common/SystemServices/MacOSSystemServiceManager.cs` - launchd service management
8. `Common/SystemServices/WindowsSystemServiceManager.cs` - Windows service management

### Modifications:
1. `Command/CreatioInstallCommand/InstallerCommand.cs` - add new parameters
2. `Command/CreatioInstallCommand/CreatioInstallerService.cs` - rewrite Execute() using strategies
3. `BindingsModule.cs` - register strategies and service managers in DI container
4. `Commands.md` - update deploy-creatio command documentation

## Advantages of New Architecture

✅ **Cross-platform support** - works on Windows (IIS/dotnet), macOS (dotnet), Linux (dotnet)  
✅ **Flexibility** - can select deployment method explicitly or use automatic selection  
✅ **Extensibility** - Strategy pattern allows easy addition of new deployment methods  
✅ **Simplicity** - single command for all platforms  
✅ **Security** - explicit SSL/HTTPS certificate management  
✅ **Automation** - systemd (Linux) and launchd (macOS) integration for auto-launch  
✅ **Visibility** - detailed logging of all operations  
✅ **Reliability** - automatic restart on application failure  

## Command Line Parameters (Reference)

| Parameter | Flag | Type | Required | Default | Description |
|----------|------|------|----------|---------|---------|
| ZIP file | `--ZipFile` | string | Yes | - | Path to application ZIP archive |
| Site name | `--SiteName` | string | No | - | Application name (will ask if not specified) |
| Port | `--SitePort` | int | No | 40000-40100 | Application port (will ask if not specified) |
| Deployment method | `--deployment` | string | No | auto | auto\|iis\|dotnet |
| No IIS | `--no-iis` | bool | No | false | Don't use IIS on Windows |
| App path | `--app-path` | string | No | ~/creatio/{SiteName} | Application installation directory |
| Use HTTPS | `--use-https` | bool | No | false | Use HTTPS instead of HTTP |
| Certificate path | `--cert-path` | string | No | - | Path to SSL certificate (.pem or .pfx) |
| Certificate password | `--cert-password` | string | No | - | SSL certificate password |
| Auto-run | `--auto-run` | bool | No | true | Run application after deployment |
| Database type | `--db` | string | No | pg | pg\|mssql |
| Platform | `--platform` | string | No | - | net6\|netframework |
| Silent mode | `--silent` | bool | No | false | Don't show interactive prompts |
| Product | `--product` | string | No | - | Product short name (s\|semse\|bcj) |

## Implementation Phases

### Phase 1: Basic Support (MVP)
- [ ] `IDeploymentStrategy` interface
- [ ] `IISDeploymentStrategy` (refactor current code)
- [ ] `DotNetDeploymentStrategy` (basic version)
- [ ] New parameters in `PfInstallerOptions`
- [ ] Strategy selection logic in `Execute()`

### Phase 2: Enhancement and Documentation
- [ ] Update `Commands.md`
- [ ] Unit tests for strategies
- [ ] Integration tests for cross-platform support
- [ ] Usage examples for different scenarios
