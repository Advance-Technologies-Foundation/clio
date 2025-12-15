# Deploy Creatio Locally on macOS

Complete guide for deploying Creatio locally on macOS using Rancher Desktop and clio.

---

## Quick Start

For experienced users, here's the minimal command sequence:

```bash
# 1. Deploy infrastructure (PostgreSQL, Redis, pgAdmin)
clio deploy-infrastructure

# 2. Deploy Creatio (interactive prompts for site name and port)
clio deploy-creatio ~/Downloads/creatio-8.x.x.zip

# 3. Access your application
# URL: http://localhost:8080
# Login: Supervisor / Supervisor
```

---

## Table of Contents

- [Prerequisites](#prerequisites)
  - [Rancher Desktop](#1-rancher-desktop-latest-version)
  - [.NET 8 SDK](#2-net-8-sdk)
  - [Clio](#3-clio-version-80171-or-higher)
- [Step 1: Deploy Infrastructure](#step-1-deploy-infrastructure)
- [Step 2: Deploy Creatio Application](#step-2-deploy-creatio-application)
- [Managing Applications](#managing-applications)
  - [View Application List](#view-application-list)
  - [Start Application](#start-application)
  - [Stop Application](#stop-application)
- [Database Access](#database-access)
- [Typical Workflow](#typical-workflow)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [Useful Commands](#useful-commands)

---

## Prerequisites

Before starting the deployment, ensure you have the following software installed:

### 1. Rancher Desktop (latest version)

| Requirement | Value |
|-------------|-------|
| Memory      | minimum 6GB |
| CPU         | minimum 2 cores |
| Download    | https://rancherdesktop.io/ |

**Installation:**

1. Download Rancher Desktop from the official website
2. Install the application
3. Launch Rancher Desktop and wait for full Kubernetes cluster initialization
4. Verify that `kubectl` is available in terminal:
   ```bash
   kubectl version --client
   ```

**Recommended Settings:**

- Virtual Machine: use `virtiofs` (avoids "no space" errors with pgAdmin)
- Memory: 6-8GB
- CPUs: 2-4

### 2. .NET 8 SDK

**Download:** https://dotnet.microsoft.com/download/dotnet/8.0

**Installation:**

1. Download .NET 8 SDK from the official Microsoft website
2. Install the downloaded package
3. Verify installation:
   ```bash
   dotnet --version
   # Should display version 8.x.x
   ```

### 3. Clio (version 8.0.1.71 or higher)

**Installation/Update:**

```bash
# Install
dotnet tool install clio -g

# Update to latest version
dotnet tool update clio -g
```

**Check version:**

```bash
clio version
# Should be version 8.0.1.71 or higher
```

### 4. Creatio Release ZIP File

Ensure you have a Creatio release ZIP file on your local disk (e.g., `~/Downloads/creatio-8.1.2.zip`).

---

## Step 1: Deploy Infrastructure

The first step is deploying the necessary Kubernetes infrastructure (PostgreSQL, Redis, pgAdmin).

### Command

```bash
clio deploy-infrastructure
```

**Short form:**

```bash
clio di
```

> **Note:** This command can be run from any directory.

### What Happens During Command Execution

The command automatically deploys the following components in the Kubernetes cluster:

| Component  | Description                                    | Access                    |
|------------|------------------------------------------------|---------------------------|
| Namespace  | `clio-infrastructure` - isolated space         | -                         |
| Redis      | Cache server for Creatio                       | `localhost:6379`          |
| PostgreSQL | Database with persistent storage               | `localhost:5432`          |
| pgAdmin    | Web interface for PostgreSQL                   | http://localhost:1080     |

**Default Credentials:**

| Service    | User/Email          | Password   |
|------------|---------------------|------------|
| PostgreSQL | `postgres`          | `postgres` |
| pgAdmin    | `root@creatio.com`  | `root`     |

### Execution Process

1. **Step 1/4:** Check if `kubectl` is available in the system
2. **Step 2/4:** Generate infrastructure YAML files from embedded templates
3. **Step 3/4:** Apply configurations to the Kubernetes cluster
4. **Step 4/4:** Verify PostgreSQL and Redis availability

### File Location

Generated infrastructure files are saved in:

```
~/.local/creatio/clio/infrastructure/
```

### Additional Parameters

```bash
# Specify custom path for infrastructure files
clio deploy-infrastructure --path /custom/path

# Skip connection verification (faster, but no guarantee of readiness)
clio deploy-infrastructure --no-verify
```

### Verify Successful Deployment

After successful command execution, check component status:

```bash
kubectl get pods -n clio-infrastructure
```

All pods should be in `Running` status.

---

## Step 2: Deploy Creatio Application

After successfully deploying the infrastructure, you can deploy the Creatio application itself.

### Preparation

1. **Navigate to the directory** where you want to store deployed applications:

   ```bash
   mkdir -p ~/creatio && cd ~/creatio
   ```

2. **Ensure you have the path to the Creatio ZIP file**:

   ```bash
   ls ~/Downloads/creatio-*.zip
   ```

### Command

```bash
# Simplified syntax (recommended)
clio deploy-creatio ~/Downloads/creatio-8.1.2.zip

# Alternative syntax with named parameter
clio deploy-creatio --ZipFile ~/Downloads/creatio-8.1.2.zip
```

> **Note:** Replace `~/Downloads/creatio-8.1.2.zip` with the actual path to your ZIP file.

### Interactive Prompts

#### 1. Site Name

```
Please enter site name:
```

**Recommendations:**

- Use a short unique name (e.g., `dev1`, `local`, `myapp`)
- Name should not contain spaces
- This name will be used for the application folder and environment name in clio

#### 2. Port

```
Press Enter to use default port 8080, or enter a custom port number:
```

**Recommendations:**

- Default is port `8080` (just press Enter)
- If port 8080 is occupied, specify another (e.g., `8081`, `8082`)
- Valid range: 1-65535

### What Happens During Command Execution

1. **Application extraction** to a subdirectory with the site name
2. **Database setup:** Create PostgreSQL database and configure connection
3. **Redis configuration:** Find available slot (0-15) and configure connection
4. **Deploy on dotnet runtime** (for macOS)
5. **Register environment in clio** with name, URL, and credentials
6. **Auto-launch** application in browser

### Default Credentials

| Parameter | Value        |
|-----------|--------------|
| Login     | `Supervisor` |
| Password  | `Supervisor` |

### Application File Location

```
~/creatio/<site_name>/
```

Example: `~/creatio/dev1/`

### Full Command Form (without interactive prompts)

```bash
clio deploy-creatio ~/Downloads/creatio-8.1.2.zip \
  --SiteName dev1 \
  --SitePort 8080 \
  --db pg \
  --platform net6 \
  --deployment dotnet
```

### Additional Parameters

```bash
# Deploy with HTTPS
clio deploy-creatio ~/Downloads/creatio.zip \
  --use-https \
  --cert-path ~/certs/app.pem \
  --cert-password "secret"

# Specify custom installation path
clio deploy-creatio ~/Downloads/creatio.zip \
  --app-path /custom/path \
  --SiteName myapp

# Disable automatic browser opening
clio deploy-creatio ~/Downloads/creatio.zip \
  --auto-run false
```

### Application URL

After successful deployment:

```
http://localhost:<port>
```

Example: http://localhost:8080

---

## Managing Applications

After deployment, you can manage local Creatio applications using the following commands.

### View Application List

```bash
clio hosts
```

**Alternative:** `clio list-hosts`

#### Example Output

```
=== Creatio Hosts ===
Environment  Service Name    Status              PID     Environment Path
-----------  --------------  ------------------  ------  ---------------------
dev1         creatio-dev1    Running (Process)   12345   /Users/admin/creatio/dev1
dev2         creatio-dev2    Stopped             -       /Users/admin/creatio/dev2

Total: 2 host(s)
```

**Status Values:**

| Status              | Description                        |
|---------------------|------------------------------------|
| `Running (Service)` | Running as system service          |
| `Running (Process)` | Running as background process      |
| `Stopped`           | Not running                        |

---

### Start Application

```bash
clio start -e ENV_NAME
```

**Alternative forms:** `clio start-creatio`, `clio start-server`, `clio sc`

#### Startup Modes

**Background Mode (default):**

```bash
clio start -e dev1
```

**Terminal Mode (for viewing logs):**

```bash
clio start -e dev1 -w
# or
clio start -e dev1 --terminal
```

A new Terminal.app window will open with visible logs.

---

### Stop Application

```bash
clio stop -e ENV_NAME
```

**Alternative:** `clio stop-creatio`

#### Examples

```bash
# Stop specific environment (with confirmation)
clio stop -e dev1

# Stop without confirmation
clio stop -e dev1 --quiet

# Stop all environments
clio stop --all

# Stop all environments without confirmation
clio stop --all --quiet
```

> **Important:** The `stop` command does NOT delete application files, database, or environment configuration. For complete removal, use `clio uninstall-creatio -e ENV_NAME`.

---

## Database Access

### pgAdmin Web Interface

| Parameter | Value                 |
|-----------|-----------------------|
| URL       | http://localhost:1080 |
| Email     | `root@creatio.com`    |
| Password  | `root`                |

### Connect to Creatio Database in pgAdmin

1. Click **Add New Server**
2. **General** tab: Name = `Creatio Local`
3. **Connection** tab:

   | Field                | Value (external) | Value (in-cluster)                                    |
   |----------------------|------------------|-------------------------------------------------------|
   | Host                 | `localhost`      | `postgres-service.clio-infrastructure.svc.cluster.local` |
   | Port                 | `5432`           | `5432`                                                |
   | Maintenance database | `postgres`       | `postgres`                                            |
   | Username             | `postgres`       | `postgres`                                            |
   | Password             | `postgres`       | `postgres`                                            |

4. Click **Save**

### Direct Connection via psql

```bash
psql -h localhost -p 5432 -U postgres
# Password: postgres
```

### Database Naming Convention

Creatio databases follow the format: `creatio_<site_name>`

Example: Site `dev1` -> Database `creatio_dev1`

---

## Typical Workflow

### Complete Work Cycle

```bash
# 1. Deploy infrastructure (one-time setup)
clio deploy-infrastructure

# 2. Deploy Creatio application
clio deploy-creatio ~/Downloads/creatio.zip
# Enter site name: dev1
# Enter port: 8080 (or press Enter)

# 3. Check status
clio hosts

# 4. Work with application (auto-started)
# Open browser: http://localhost:8080
# Login: Supervisor / Supervisor

# 5. Stop application (when finished)
clio stop -e dev1

# 6. Restart application (next day)
clio start -e dev1

# 7. View logs if needed
clio start -e dev1 --terminal
```

### Working with Multiple Environments

```bash
# Deploy multiple applications
clio deploy-creatio ~/Downloads/creatio-8.1.2.zip --SiteName dev1 --SitePort 8080
clio deploy-creatio ~/Downloads/creatio-8.1.3.zip --SiteName dev2 --SitePort 8081
clio deploy-creatio ~/Downloads/creatio-8.2.0.zip --SiteName test1 --SitePort 8082

# View all environments
clio hosts

# Stop all environments
clio stop --all

# Start only needed one
clio start -e dev2
```

### Daily Work

```bash
# Morning - start Rancher Desktop (wait for cluster readiness)

# Start needed environment
clio start -e dev1

# Open browser
open http://localhost:8080

# Work with application...

# Evening - stop environment
clio stop -e dev1
```

---

## Troubleshooting

### "kubectl not found"

**Cause:** kubectl is not installed or not in PATH.

**Solution:**

1. Ensure Rancher Desktop is running
2. Check: `kubectl version --client`
3. If not found: `brew install kubectl`

---

### PostgreSQL or Redis not responding

**Cause:** Pods not yet fully initialized.

**Solution:**

```bash
# Check pod status
kubectl get pods -n clio-infrastructure

# Wait for Running status, then view logs if needed
kubectl logs -n clio-infrastructure <pod-name>

# If necessary, restart
kubectl delete namespace clio-infrastructure
clio deploy-infrastructure
```

---

### Port 8080 already in use

**Solution:**

```bash
# Option 1: Use different port
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip --SitePort 8081

# Option 2: Find and stop process using port
lsof -i :8080
```

---

### pgAdmin doesn't open on localhost:1080

**Solution:**

```bash
# Check service status
kubectl get svc -n clio-infrastructure pgadmin-service

# Check logs
kubectl logs -n clio-infrastructure -l app=pgadmin
```

> **Tip:** For Rancher Desktop, configure Virtual Machine type to `virtiofs`.

---

### Creatio application doesn't start

**Solution:**

```bash
# Start with logs visible
clio start -e dev1 --terminal

# Check database access
psql -h localhost -p 5432 -U postgres -l

# Check configuration
cat ~/creatio/dev1/ConnectionStrings.config
```

---

### "Environment not found"

**Solution:**

```bash
# Check registered environments
clio hosts

# Register manually if missing
clio reg-web-app dev1 --ep ~/creatio/dev1
```

---

### Rancher Desktop using too many resources

**Solution:**

1. Open Rancher Desktop settings
2. Go to **Virtual Machine**
3. Set Memory: 6-8GB, CPUs: 2-4
4. Restart Rancher Desktop

---

### Not enough disk space

**Solution:**

```bash
# Clean Docker resources
docker system prune -a --volumes

# Remove old environments
clio uninstall-creatio -e old_env

# Delete unused databases via pgAdmin
```

---

### Getting Additional Help

1. Check command documentation: [Commands.md](Commands.md)
2. Review logs: `tail -f ~/creatio/dev1/logs/creatio.log`
3. Open issue: https://github.com/anthropics/clio/issues

---

## FAQ

### Recommendation to reconfigure Rancher Desktop

It looks like the current volume virtualization/mount type in Rancher Desktop is causing issues (the virtiofs / Virtual Machine option does not work properly — PostgreSQL connections fail, and others have the same problem). A reliable workaround is to switch the mount type to reverse-sshfs, which has been tested and works well.

What to do:

1. Open Rancher Desktop.
2. Go to **Preferences → Volumes**.
3. In **Mount Type**, select `reverse-sshfs` (instead of `virtiofs / Virtual Machine`).
4. Click **Apply**.
5. Restart Rancher Desktop if prompted.

---

## Useful Commands

### Kubernetes Management

| Command | Description |
|---------|-------------|
| `kubectl get pods -n clio-infrastructure` | View all pods |
| `kubectl get svc -n clio-infrastructure` | View all services |
| `kubectl logs -n clio-infrastructure <pod>` | View pod logs |
| `kubectl delete namespace clio-infrastructure` | Delete entire infrastructure |

### Clio Environment Management

| Command | Description |
|---------|-------------|
| `clio hosts` | List all environments |
| `clio start -e NAME` | Start environment |
| `clio stop -e NAME` | Stop environment |
| `clio stop --all` | Stop all environments |
| `clio uninstall-creatio -e NAME` | Remove environment completely |
| `clio reg-web-app NAME --ep PATH` | Register environment manually |
