# Set up Creatio instance for working with Clio

**VERSION:** 8.3  
**LEVEL:** ADVANCED  
**Document Version:** 13.08.2025 - Improved

## Prerequisites and System Requirements

### Before You Begin
Before you set up Creatio instance for working with Clio, install Clio.  
Instructions: [Install Clio](https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/development-tools/clio/install-clio).

### System Requirements
- **Operating System:** Windows 10/11 (version 1903 or later) or Windows Server 2019+
- **RAM:** Minimum 16GB (32GB recommended for production)
- **Disk Space:** Minimum 50GB free space
- **Network:** Internet connection for downloading dependencies

### Required Dependencies
- **WSL 2:** Windows Subsystem for Linux version 2
- **Rancher Desktop:** Latest stable version (v1.9.0 or later recommended)
- **Visual Studio Code:** Latest version
- **PowerShell:** Version 5.1 or PowerShell 7+

---

## Overview

You can connect Clio to an already deployed Creatio instance or deploy a Creatio instance from scratch using Clio.

## Connect Clio to an Already Deployed Creatio Instance

### Steps
1. **Open a terminal** (Windows PowerShell, Command Prompt, or any terminal) as administrator.

2. **Register the environment** by running:
   ```bash
   clio reg-web-app SomeEnvironmentName -u SomeCreatioURL -l SomeLogin -p SomePassword
   ```
   Where:
   - `SomeEnvironmentName` is the name of your Creatio environment
   - `SomeCreatioURL` is the URL of already deployed Creatio instance  
   - `SomeLogin` is the user login to the Creatio instance
   - `SomePassword` is the user password to the Creatio instance

3. **Verify the connection:**
   ```bash
   clio hc -e SomeEnvironmentName --WebApp "true" --WebHost "true"
   ```
   
   **Command explanation:**
   - `hc` - Health check command (more comprehensive than ping)
   - `-e` - Name of registered environment
   - `--WebApp "true"` - Checks WebAppLoader health endpoint
   - `--WebHost "true"` - Checks WebHost health endpoint
   
   **Expected output:**
   ```
   [INF] - Checking WebAppLoader https://yoursite.com/api/HealthCheck/Ping ...
   [INF] -         WebAppLoader - OK
   [INF] - Checking WebHost https://yoursite.com/0/api/HealthCheck/Ping ...
   [INF] -         WebHost - OK
   ```

---

## Deploy a Creatio Instance Using Clio

Clio lets you deploy Creatio on .NET and .NET Framework platforms and use MS SQL and PostgreSQL databases.

### 1. Install Additional Components

#### 1.1 Install Linux on Windows (Windows Only)
1. **Install WSL 2** following the official guide: [How to install Linux on Windows with WSL](https://docs.microsoft.com/en-us/windows/wsl/install)

2. **Configure WSL settings** by creating/editing `.wslconfig` file:
   ```ini
   # File: C:\Users\YourUsername\.wslconfig
   [wsl2]
   memory=8GB # Limits VM memory in WSL 2 to 8 GB
   processors=4 # Makes the WSL VM use 4 virtual processors
   ```

3. **Restart WSL** after configuration:
   ```pwsh
   wsl --shutdown
   wsl
   ```

#### 1.2 Install Rancher Desktop
1. **Download and install** Rancher Desktop from the [official SUSE website](https://rancherdesktop.io/)
2. **Configure Rancher Desktop:**
   - Use the latest stable Kubernetes version
   - Select **dockerd (moby)** as the Container Engine
   - Enable Kubernetes

3. **Verify installation:**
   ```bash
   kubectl version --client
   docker version
   ```

#### 1.3 Enable Required Windows Components (Windows Only)
1. **Check current Windows features:**
   ```bash
   clio check-windows-features
   ```
   
   This command will show you which components are installed and which are missing.

2. **Enable required components using clio manage-windows-features:**

   **Important:** The `manage-windows-features` command requires specific mode flags:
   
   ```bash
   # Check what features are missing
   clio manage-windows-features --Check
   # or shorter alias
   clio mwf -c
   ```
   
   ```bash
   # Install missing required features
   clio manage-windows-features --Install
   # or shorter alias  
   clio mwf -i
   ```

   **Command explanation:**
   - `manage-windows-features` (alias: `mwf`) - Manages IIS and .NET Framework features required for Creatio
   - `--Check` or `-c` - Shows status of all required features
   - `--Install` or `-i` - Installs missing required features
   - `--Uninstall` or `-u` - Uninstalls the required features
   - Uses DISM API with progress indicators
   - Requires administrator privileges

   **Features managed by this command (IIS/.NET Framework features):**
   - Static Content
   - Default Document  
   - HTTP Errors
   - HTTP Redirection
   - ASP
   - ISAPI Extensions & Filters
   - WebSocket Protocol
   - WCF HTTP/NonHTTP Activation
   - ASP.NET 4.8
   - WCF Services (HTTP, MSMQ, Named Pipe, TCP Activation)
   - HTTP Logging & Tools
   - Basic Authentication
   - Request Filtering
   - IP Security
   - Dynamic HTTP Compression

   **Note:** This command manages IIS/.NET features, not Docker/WSL/Hyper-V features. For containerized deployment, you still need to enable WSL/Hyper-V separately.

3. **Verify installation:**
   ```bash
   clio check-windows-features
   ```
   
   **Expected output when all components are installed:**
   ```
   [INF] - All required components installed
   ```

4. **Restart your computer** if prompted by the system.

### 2. Set Up a Local Kubernetes Cluster

#### 2.1 Verify Kubernetes Installation
```bash
kubectl cluster-info
```

**Expected output:**
```
Kubernetes control plane is running at https://127.0.0.1:6443
CoreDNS is running at https://127.0.0.1:6443/api/v1/namespaces/kube-system/services/kube-dns:dns/proxy
```

#### 2.2 Generate Kubernetes Manifests
```bash
clio create-k8-files
```

This creates the `C:\Users\SomeWindowsUser\AppData\Local\creatio\clio\infrastructure` directory with pre-configured services:
- Email Listener
- MS SQL server  
- PostgreSQL server
- Redis server
- pgAdmin GUI

#### 2.3 Navigate to Infrastructure Directory
```bash
# Open the infrastructure directory in Windows Explorer
clio open-k8-files
# or using alias
clio cfg-k8f
```

**Command explanation:**
- `open-k8-files` (aliases: `cfg-k8f`, `cfg-k8s`) - Opens the infrastructure folder in Windows Explorer
- Navigates to: `C:\Users\SomeWindowsUser\AppData\Local\creatio\clio\infrastructure`
- Windows-only command (uses explorer.exe)

**Expected files in infrastructure directory:**
```ini
├── clio-namespace.yaml
├── clio-storage-class.yaml
├── email-listener
│   ├── email-listener-services.yaml
│   └── email-listener-workload.yaml
├── mssql
│   ├── mssq-secrets.yaml
│   ├── mssql-services.yaml
│   ├── mssql-stateful-set.yaml
│   └── mssql-volumes.yaml
├── pgadmin
│   ├── pgadmin-secrets.yaml
│   ├── pgadmin-services.yaml
│   ├── pgadmin-volumes.yaml
│   └── pgadmin-workload.yaml
├── postgres
│   ├── postgres-secrets.yaml
│   ├── postgres-services.yaml
│   ├── postgres-stateful-set.yaml
│   └── postgres-volumes.yaml
└── redis
    ├── redis-services.yaml
    └── redis-workload.yaml
```

#### 2.4 Verify Kubernetes Context
**Important:** Ensure your kubectl context is set to Rancher Desktop:
```bash
kubectl config current-context
```

**Expected output:** `rancher-desktop`

If not set correctly:
```bash
kubectl config use-context rancher-desktop
```

### 3. Configure Resources (Optional)

You can modify CPU and storage allocations based on your system capabilities:

#### 3.1 PostgreSQL Configuration
Navigate to `postgres/postgres-stateful-set.yaml` and modify:

**Storage Configuration:**
```yaml
# In postgres-stateful-set.yaml
spec:
  volumeClaimTemplates:
  - metadata:
      name: "postgres-data"
    spec:
      resources:
        requests:
          storage: "10Gi"  # Default: 40Gi, adjust as needed
```

**Volumes Configuration:**
In `postgres/postgres-volumes.yaml`:
```yaml
# postgres-data-pv
spec:
  capacity:
    storage: "10Gi"  # Must match StatefulSet request
```

#### 3.2 MSSQL Configuration (If Using SQL Server)
Navigate to `mssql/mssql-stateful-set.yaml` and modify similarly:
```yaml
spec:
  volumeClaimTemplates:
  - metadata:
      name: "mssql-data"
    spec:
      resources:
        requests:
          storage: "10Gi"
```

### 4. Deploy Infrastructure Components

#### 4.1 Apply Kubernetes Manifests
**Run commands in sequence:**
```bash
# Create namespace
kubectl apply -f clio-namespace.yaml

# Verify namespace creation
kubectl get namespace clio-infrastructure
```

```bash
# Create storage class
kubectl apply -f clio-storage-class.yaml

# Verify storage class
kubectl get storageclass clio-storage
```

```bash
# Deploy Redis
kubectl apply -f .\redis

# Deploy PostgreSQL
kubectl apply -f .\postgres

# Deploy pgAdmin
kubectl apply -f .\pgadmin
```

#### 4.2 Verify Deployments
**Check deployment status:**
```bash
kubectl get pods -n clio-infrastructure
```

**Expected output (all pods should be Running):**
```
NAME                            READY   STATUS    RESTARTS   AGE
clio-pgadmin-xxx               1/1     Running   0          2m
clio-redis-xxx                 1/1     Running   0          2m
clio-postgres-0                1/1     Running   0          3m
```

**Check services:**
```bash
kubectl get services -n clio-infrastructure
```

**If pods are not running, troubleshoot:**
```bash
# Check pod details
kubectl describe pod <pod-name> -n clio-infrastructure

# Check logs
kubectl logs <pod-name> -n clio-infrastructure
```

#### 4.3 Verify Rancher Desktop Dashboard
1. **Open Rancher Desktop**
2. **Click "Cluster Dashboard"** to open the management interface
3. **Navigate to Workloads → Deployments**
4. **Verify Active status** for:
   - `clio-pgadmin`
   - `clio-redis`
5. **Navigate to StatefulSets**
6. **Verify Active status** for:
   - `clio-postgres`

### 5. Verify PostgreSQL Setup (pgAdmin GUI)

#### 5.1 Access pgAdmin
1. **Find pgAdmin credentials** in `pgadmin/pgadmin-secrets.yaml`:
   - **Email:** `root@creatio.com`
   - **Password:** `root`

2. **Access pgAdmin interface:**
   - **URL:** http://localhost:1080
   - **Login** with the credentials above

#### 5.2 Connect to PostgreSQL Server
1. **In pgAdmin, click** "Servers" → "PostgreSQL"
2. **Enter connection details:**
   - **Password:** `root` (same as pgAdmin password)
3. **Verify connection successful** - you should see the Activity tab

#### 5.3 Enable Template Databases
1. **Navigate to** File → Preferences
2. **Go to** Browser → Display
3. **Select** "Show template databases?" checkbox
4. **Click Save and Refresh**

### 6. Set Up Redis Server

#### 6.1 Install Redis Extension
For a graphical interface, you can install:
- **"Redis Client for Visual Studio Code"** extension (if using VS Code)
- **RedisInsight** - Standalone Redis GUI client
- **Another Redis GUI client** of your choice

Clio works with Redis without requiring any GUI client.

#### 6.2 Connect to Redis
1. **In VS Code, open Redis extension**
2. **Click "Create Connection"**
3. **Configure connection:**
   - **Name:** `Redis-Clio`
   - **Host:** `127.0.0.1`
   - **Port:** `6379`
   - **Leave other settings as default**
4. **Save connection**

#### 6.3 Verify Redis Connection
1. **Open Redis terminal** in VS Code
2. **Test database selection:**
   ```redis
   select 0
   ```
   **Expected response:** `OK`

### 7. Deploy Creatio Instance

Before deployment, ensure:

#### 7.1 Prerequisites
1. **Find IIS root directory path** in `appsettings.json`:
   ```json
   "iis-clio-root-path": "C:\\inetpub\\wwwroot\\clio"
   ```

2. **Create IIS root directory** if it doesn't exist:
   ```bash
   mkdir "C:\inetpub\wwwroot\clio"
   ```

3. **Set permissions** - ensure `IIS_IUSRS` has Full Control on the directory

4. **Obtain Creatio setup archive** from Creatio support (PostgreSQL version recommended)

5. **Save archive** to the products directory specified in `appsettings.json`:
   ```json
   "creatio-products": "C:\\CreatioProductBuild"
   ```

#### 7.2 Method 1: Deploy via File Explorer
1. **Navigate** to the directory containing Creatio setup zip file
2. **Right-click** the zip file → **"clio: deploy Creatio"**
3. **Fill deployment parameters:**
   - **Site name:** `DevEnv` (or your preferred name)
   - **Site port:** `40015` (recommended range: 40000-40100)
4. **Wait for deployment** (may take 15-30 minutes)

**If context menu missing:**
```bash
clio register
```

#### 7.3 Method 2: Deploy via Terminal
1. **Open terminal as administrator** (PowerShell, Command Prompt, or any terminal)
2. **Run the deployment command:**
   ```bash
   clio deploy-creatio --SiteName "DevEnv" --SitePort 40015 --ZipFile "C:\CreatioProductBuild\CreatioSetup.zip"
   ```

   **Command parameters:**
   - `--SiteName` - Name for the Creatio site/instance
   - `--SitePort` - Port number for accessing the site (recommended: 40000-40100)
   - `--ZipFile` - Full path to the Creatio setup archive

   **Optional --silent parameter:**
   ```bash
   # For automated/unattended deployment (no user prompts)
   clio deploy-creatio --SiteName "DevEnv" --SitePort 40015 --ZipFile "C:\CreatioProductBuild\CreatioSetup.zip" --silent
   ```

   **--silent option explanation:**
   - **Purpose:** Runs deployment in silent/unattended mode
   - **Behavior:** Skips user prompts and confirmations during deployment
   - **Database:** Uses default values for database configuration
   - **Use cases:** Automated deployments, CI/CD pipelines, scripted installations
   - **Benefits:** No user interaction required, consistent deployments

   **When to use --silent:**
   - ✅ Automated deployment scripts
   - ✅ CI/CD pipeline deployments  
   - ✅ Batch deployments of multiple instances
   - ✅ When you want to use default database settings
   - ❌ First-time deployments where you want to review settings
   - ❌ When you need to customize database configuration

#### 7.4 Verify Deployment
**Expected results:**
- Creatio instance deployed successfully
- Browser automatically opens to Creatio login page
- You can log in with your Creatio credentials

**Verify with clio:**
```bash
clio reg-web-app DevEnv -u http://localhost:40015 -l Supervisor -p Supervisr
clio hc -e DevEnv --WebApp "true" --WebHost "true"
```

**Expected healthcheck output:**
```
[INF] - Checking WebAppLoader http://localhost:40015/api/HealthCheck/Ping ...
[INF] -         WebAppLoader - OK
[INF] - Checking WebHost http://localhost:40015/0/api/HealthCheck/Ping ...
[INF] -         WebHost - OK
```

---

## Troubleshooting

### Common Issues and Solutions

#### Kubernetes Pods Not Starting
**Problem:** Pods stuck in `Pending` or `CrashLoopBackOff` state

**Solution:**
```bash
# Check pod status and details
kubectl get pods -n clio-infrastructure
kubectl describe pod <pod-name> -n clio-infrastructure

# Check pod logs for specific issues
kubectl logs -n clio-infrastructure clio-postgres-0
kubectl logs -n clio-infrastructure <redis-pod-name>
kubectl logs -n clio-infrastructure <pgadmin-pod-name>

# For pods that are restarting, check previous logs
kubectl logs -n clio-infrastructure clio-postgres-0 --previous

# Check available resources
kubectl top nodes

# Check storage issues
kubectl get pv,pvc -n clio-infrastructure
```

**Common log commands by service:**
```bash
# PostgreSQL logs
kubectl logs -n clio-infrastructure clio-postgres-0

# Redis logs (get pod name first)
kubectl get pods -n clio-infrastructure | grep redis
kubectl logs -n clio-infrastructure clio-redis-<pod-id>

# pgAdmin logs (get pod name first)  
kubectl get pods -n clio-infrastructure | grep pgadmin
kubectl logs -n clio-infrastructure clio-pgadmin-<pod-id>

# Follow logs in real-time
kubectl logs -n clio-infrastructure clio-postgres-0 -f
```

**Common causes:**
- Insufficient disk space
- Storage class not created
- Resource constraints
- Database initialization failures
- Network connectivity issues

#### pgAdmin Connection Issues
**Problem:** Cannot connect to PostgreSQL from pgAdmin

**Solutions:**
1. **Verify PostgreSQL pod is running:**
   ```bash
   kubectl get pods -n clio-infrastructure | grep postgres
   ```

2. **Check PostgreSQL logs:**
   ```bash
   kubectl logs -n clio-infrastructure clio-postgres-0
   
   # Check recent logs with timestamps
   kubectl logs -n clio-infrastructure clio-postgres-0 --timestamps --tail=50
   ```

3. **Check pgAdmin logs:**
   ```bash
   # Get pgAdmin pod name
   kubectl get pods -n clio-infrastructure | grep pgadmin
   
   # Check pgAdmin logs
   kubectl logs -n clio-infrastructure <pgadmin-pod-name>
   ```

4. **Verify service is accessible:**
   ```bash
   kubectl get services -n clio-infrastructure
   
   # Check service endpoints
   kubectl get endpoints -n clio-infrastructure
   ```

#### Deployment Timeouts
**Problem:** Creatio deployment takes too long or fails

**Solutions:**
1. **Check system resources** (CPU, memory, disk)
2. **Verify database connectivity**
3. **Check deployment logs** in PowerShell/terminal
4. **Ensure antivirus is not blocking files**

#### WSL/Docker Issues
**Problem:** Docker or WSL not working properly

**Solutions:**
1. **Restart Docker service:**
   ```bash
   wsl --shutdown
   # Restart Rancher Desktop
   ```

2. **Check WSL status:**
   ```bash
   wsl --status
   wsl --list --verbose
   ```

### Reset/Cleanup Procedures

#### Clean Up Failed Infrastructure Deployment
```bash
# Delete all infrastructure components
kubectl delete namespace clio-infrastructure

# Remove persistent volumes
kubectl delete pv --selector=app=clio

# Recreate from scratch
clio create-k8-files
# Then redeploy following steps 4.1
```

#### Clean Up Failed Creatio Deployment
```bash
# Remove IIS site
clio remove-site DevEnv

# Clean up files
Remove-Item "C:\inetpub\wwwroot\clio\DevEnv" -Recurse -Force

# Redeploy
clio deploy-creatio --SiteName "DevEnv" --SitePort 40015 --ZipFile "path\to\setup.zip"
```

---

## Performance Tuning

### Resource Recommendations

#### Development Environment
- **PostgreSQL storage:** 10-20Gi
- **MSSQL storage:** 10-20Gi  
- **WSL memory:** 8-12GB
- **WSL processors:** 4-6

#### Production Environment
- **PostgreSQL storage:** 50-100Gi
- **MSSQL storage:** 50-100Gi
- **WSL memory:** 16-32GB
- **WSL processors:** 8-12

### Monitoring
**Check resource usage:**
```bash
# Kubernetes resources
kubectl top pods -n clio-infrastructure
kubectl top nodes

# WSL resources
wsl --status
```

---

## Security Considerations

### Best Practices
1. **Change default passwords** for pgAdmin and databases in production
2. **Use HTTPS** for production Creatio instances  
3. **Limit network access** to development ports (40000-40100 range)
4. **Regular updates** of Rancher Desktop and Kubernetes components
5. **Backup strategies** for persistent data

### Network Security
- Development ports should not be exposed to external networks
- Use VPN or secure connections for remote access
- Consider firewall rules for production deployments

---

## Glossary

**Clio** - Command-line interface tool for Creatio development and deployment

**Kubernetes (K8s)** - Container orchestration platform used for managing Creatio infrastructure

**StatefulSet** - Kubernetes resource type for managing stateful applications like databases

**PVC (PersistentVolumeClaim)** - Request for storage resources in Kubernetes

**WSL** - Windows Subsystem for Linux, enables running Linux environment on Windows

**Rancher Desktop** - Tool that provides local Kubernetes cluster and container management

**pgAdmin** - Web-based PostgreSQL administration tool

**Redis** - In-memory data structure store used for caching and session management

---

## See Also

- [Official Clio documentation (GitHub)](https://github.com/Advance-Technologies-Foundation/clio)
- [Clio explorer extension for Visual Studio Code](https://marketplace.visualstudio.com/items?itemName=CreatioCompany.clio-explorer)
- [Official Redis website](https://redis.io/)
- [Install Clio](https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/development-tools/clio/install-clio)
- [Use Clio](https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/development-tools/clio/use-clio)
- [Clio tutorials (YouTube)](https://www.youtube.com/playlist?list=PLnolFjGaqGPqyL4QDRHWm0ricoHJcT13P)

---

**Now you can start using Clio and execute different operations with Creatio from any terminal based on your business goals.**