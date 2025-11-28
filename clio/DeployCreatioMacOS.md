# Deploy Creatio Locally on macOS

Complete guide for deploying Creatio locally on macOS using Rancher Desktop and clio.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Step 1: Deploy Infrastructure](#step-1-deploy-infrastructure)
- [Step 2: Deploy Creatio Application](#step-2-deploy-creatio-application)
- [Managing Applications](#managing-applications)
- [Database Access](#database-access)
- [Typical Workflow](#typical-workflow)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

Before starting the deployment, ensure you have the following software installed:

### 1. Rancher Desktop (latest version)

**Resource Requirements:**
- **Memory**: minimum 6GB for the virtual machine
- **CPU**: minimum 2 cores

**Installation:**
1. Download Rancher Desktop from the official website: https://rancherdesktop.io/
2. Install the application
3. Launch Rancher Desktop and wait for full Kubernetes cluster initialization
4. Verify that \`kubectl\` is available in terminal:
   \`\`\`bash
   kubectl version --client
   \`\`\`

**Recommended Settings:**
- Virtual Machine: use \`virtiofs\` (avoids "no space" errors with pgAdmin)
- Memory: 6-8GB
- CPUs: 2-4

### 2. .NET 8 SDK

**Installation:**
1. Download .NET 8 SDK from the official Microsoft website: https://dotnet.microsoft.com/download/dotnet/8.0
2. Install the downloaded package
3. Verify installation:
   \`\`\`bash
   dotnet --version
   # Should display version 8.x.x
   \`\`\`

### 3. Clio (version 8.0.1.71 or higher)

**Installation/Update:**
\`\`\`bash
# Install
dotnet tool install clio -g

# Update to latest version
dotnet tool update clio -g
\`\`\`

**Check version:**
\`\`\`bash
clio version
# Should be version 8.0.1.71 or higher
\`\`\`

### 4. Creatio Release ZIP File

Ensure you have a Creatio release ZIP file on your local disk (e.g., \`~/Downloads/creatio-8.1.2.zip\`).

---

## Step 1: Deploy Infrastructure

The first step is deploying the necessary Kubernetes infrastructure (PostgreSQL, Redis, pgAdmin).

### Command

\`\`\`bash
clio deploy-infrastructure
\`\`\`

**Short form:**
\`\`\`bash
clio di
\`\`\`

**Important:** This command can be run from any directory.

### What Happens During Command Execution

The command automatically deploys the following components in the Kubernetes cluster:

1. **Namespace** \`clio-infrastructure\` - isolated space for all resources
2. **Storage Class** - configuration for persistent storage
3. **Redis** - cache server for Creatio
   - Internal service (ClusterIP)
   - External access via LoadBalancer on port \`6379\`
4. **PostgreSQL** - database for Creatio
   - Internal service (ClusterIP)
   - External access via LoadBalancer on port \`5432\`
   - Persistent storage (PersistentVolume)
   - Credentials: \`postgres\` / \`postgres\`
5. **pgAdmin** - web interface for managing PostgreSQL
   - Access via LoadBalancer on port \`1080\`
   - Credentials: \`root@creatio.com\` / \`root\`

### Execution Process

The command performs the following steps:

1. **Step 1/4:** Check if \`kubectl\` is available in the system
2. **Step 2/4:** Generate infrastructure YAML files from embedded templates
3. **Step 3/4:** Apply configurations to the Kubernetes cluster
4. **Step 4/4:** Verify PostgreSQL and Redis availability (up to 40 attempts for PostgreSQL, 10 for Redis)

### File Location

Generated infrastructure files are saved in:
\`\`\`
~/.local/creatio/clio/infrastructure/
\`\`\`

### Additional Parameters

\`\`\`bash
# Specify custom path for infrastructure files
clio deploy-infrastructure --path /custom/path

# Skip connection verification (faster, but no guarantee of readiness)
clio deploy-infrastructure --no-verify
\`\`\`

### Verify Successful Deployment

After successful command execution, check component status:

\`\`\`bash
kubectl get pods -n clio-infrastructure
\`\`\`

All pods should be in \`Running\` status.

### Available Services After Deployment

- **PostgreSQL**: \`localhost:5432\` (user: \`postgres\`, password: \`postgres\`)
- **Redis**: \`localhost:6379\` (no authentication)
- **pgAdmin**: \`http://localhost:1080\` (email: \`root@creatio.com\`, password: \`root\`)

---

## Step 2: Deploy Creatio Application

After successfully deploying the infrastructure, you can deploy the Creatio application itself.

### Preparation

1. **Navigate to the directory** where you want to store deployed applications:
   \`\`\`bash
   cd ~/creatio
   \`\`\`
   
   Or create a new directory:
   \`\`\`bash
   mkdir -p ~/creatio
   cd ~/creatio
   \`\`\`

2. **Ensure you have the path to the Creatio ZIP file**:
   \`\`\`bash
   ls ~/Downloads/creatio-*.zip
   \`\`\`

### Command

\`\`\`bash
clio deploy-creatio --ZipFile ~/Downloads/creatio-8.1.2.zip
\`\`\`

**Replace** \`~/Downloads/creatio-8.1.2.zip\` with the actual path to your ZIP file.

### Interactive Prompts

After running the command, you will be prompted to enter the following parameters:

#### 1. Site Name

\`\`\`
Please enter site name: 
\`\`\`

**Recommendations:**
- Use a short unique name (e.g., \`dev1\`, \`local\`, \`myapp\`)
- Name should not contain spaces
- This name will be used for the application folder and environment name in clio

**Example:** \`dev1\`

#### 2. Port

\`\`\`
Press Enter to use default port 8080, or enter a custom port number:
\`\`\`

**Recommendations:**
- Default is port \`8080\` (just press Enter)
- If port 8080 is occupied, specify another (e.g., \`8081\`, \`8082\`)
- Valid range: 1-65535
- Ensure the selected port is not used by another application

**Example:** \`8080\` (or press Enter for default)

### What Happens During Command Execution

1. **Application extraction** to a subdirectory with the site name
2. **Database setup:**
   - Create PostgreSQL template from backup
   - Create target database from template
   - Configure \`ConnectionStrings.config\`
3. **Redis configuration:**
   - Find an available Redis database slot (0-15)
   - Configure Redis connection in \`ConnectionStrings.config\`
4. **Deploy on dotnet runtime** (for macOS)
5. **Register environment in clio** with name, URL, and credentials
6. **Auto-launch** application in browser

### Default Credentials

After deployment, the application will be available with the following credentials:

- **Login:** \`Supervisor\`
- **Password:** \`Supervisor\`

### Application File Location

The application will be deployed to:
\`\`\`
~/creatio/<site_name>/
\`\`\`

For example, if you specified the name \`dev1\`, the path will be:
\`\`\`
~/creatio/dev1/
\`\`\`

### Full Command Form (without interactive prompts)

If you want to specify all parameters at once without dialogs:

\`\`\`bash
clio deploy-creatio \\
  --ZipFile ~/Downloads/creatio-8.1.2.zip \\
  --SiteName dev1 \\
  --SitePort 8080 \\
  --db pg \\
  --platform net6 \\
  --deployment dotnet
\`\`\`

### Additional Parameters

\`\`\`bash
# Deploy with HTTPS
clio deploy-creatio \\
  --ZipFile ~/Downloads/creatio.zip \\
  --use-https \\
  --cert-path ~/certs/app.pem \\
  --cert-password "secret"

# Specify custom installation path
clio deploy-creatio \\
  --ZipFile ~/Downloads/creatio.zip \\
  --app-path /custom/path \\
  --SiteName myapp

# Disable automatic browser opening
clio deploy-creatio \\
  --ZipFile ~/Downloads/creatio.zip \\
  --auto-run false
\`\`\`

### Application URL

After successful deployment, the application will be available at:
\`\`\`
http://localhost:<port>
\`\`\`

For example: \`http://localhost:8080\`

---

## Managing Applications

After deployment, you can manage local Creatio applications using the following commands.

### View Application List

#### Command \`clio hosts\`

Displays a list of all registered local Creatio environments with their status.

\`\`\`bash
clio hosts
\`\`\`

**Alternative form:**
\`\`\`bash
clio list-hosts
\`\`\`

#### Example Output

\`\`\`
=== Creatio Hosts ===
Environment  Service Name    Status              PID     Environment Path
-----------  --------------  ------------------  ------  ---------------------
dev1         creatio-dev1    Running (Process)   12345   /Users/admin/creatio/dev1
dev2         creatio-dev2    Stopped             -       /Users/admin/creatio/dev2

Total: 2 host(s)
\`\`\`

**Column Descriptions:**
- **Environment** - environment name in clio
- **Service Name** - OS service name (format: \`creatio-<env>\`)
- **Status** - current status:
  - \`Running (Service)\` - running as system service
  - \`Running (Process)\` - running as background process
  - \`Stopped\` - stopped
- **PID** - process ID (for background processes)
- **Environment Path** - physical path to Creatio installation

---

### Start Application

#### Command \`clio start\`

Starts a local Creatio application.

\`\`\`bash
clio start -e ENV_NAME
\`\`\`

**Alternative forms:**
\`\`\`bash
clio start-creatio -e ENV_NAME
clio start-server -e ENV_NAME
clio sc -e ENV_NAME
\`\`\`

#### Examples

\`\`\`bash
# Start specific environment
clio start -e dev1

# Start default environment
clio start
\`\`\`

#### Startup Modes

**1. Background Mode (default):**

Application starts as a background process without a visible terminal.

\`\`\`bash
clio start -e dev1
\`\`\`

**Output:**
\`\`\`
Starting Creatio application 'dev1' as a background service...
Path: /Users/admin/creatio/dev1
✓ Creatio application started successfully as background service (PID: 12345)!
Use 'clio start -w' to start with terminal window for logs.
\`\`\`

**2. Terminal Mode (for viewing logs):**

Application starts in a new terminal window with visible logs.

\`\`\`bash
clio start -e dev1 -w
# or
clio start -e dev1 --terminal
\`\`\`

**Output:**
\`\`\`
Starting Creatio application 'dev1' in a new terminal window...
Path: /Users/admin/creatio/dev1
✓ Creatio application started successfully!
Check the new terminal window for application logs.
\`\`\`

A new Terminal.app window will open with the running application and visible logs.

#### Verify Startup

After starting, check the status:

\`\`\`bash
clio hosts
\`\`\`

The environment should display with status \`Running (Process)\` or \`Running (Service)\`.

---

### Stop Application

#### Command \`clio stop\`

Stops a running Creatio application.

\`\`\`bash
clio stop -e ENV_NAME
\`\`\`

**Alternative form:**
\`\`\`bash
clio stop-creatio -e ENV_NAME
\`\`\`

#### Examples

\`\`\`bash
# Stop specific environment (with confirmation)
clio stop -e dev1

# Stop without confirmation
clio stop -e dev1 --quiet

# Stop all environments
clio stop --all

# Stop all environments without confirmation
clio stop --all --quiet
\`\`\`

#### Example Output (with confirmation)

\`\`\`
This will stop 1 Creatio service(s)/process(es):
  - dev1 (/Users/admin/creatio/dev1)

Continue? [y/N]: y

Stopping environment: dev1
Killing process dotnet (PID: 12345)
✓ Background process stopped
✓ Successfully stopped 'dev1'

=== Summary ===
Stopped: 1
\`\`\`

#### What the Command Does

1. Stops system service (if exists)
2. Terminates background process (if running)
3. Removes service configuration (on macOS: \`~/Library/LaunchAgents/com.creatio.<env>.plist\`)

**Important:** The command **does NOT delete**:
- Application files
- Database
- Environment configuration in clio

For complete removal, use:
\`\`\`bash
clio uninstall-creatio -e ENV_NAME
\`\`\`

---

## Database Access

### pgAdmin

After deploying the infrastructure, a pgAdmin web interface is available for managing PostgreSQL databases.

#### Access pgAdmin

**URL:** http://localhost:1080

**Credentials:**
- **Email:** \`root@creatio.com\`
- **Password:** \`root\`

#### Connect to Creatio Database

After logging into pgAdmin:

1. Click **Add New Server**
2. On the **General** tab:
   - **Name:** \`Creatio Local\` (or any name)
3. On the **Connection** tab:
   - **Host:** \`postgres-service.clio-infrastructure.svc.cluster.local\` (for in-cluster access) or \`localhost\` (for external access)
   - **Port:** \`5432\`
   - **Maintenance database:** \`postgres\`
   - **Username:** \`postgres\`
   - **Password:** \`postgres\`
4. Click **Save**

#### Direct Connection via psql

You can also connect to the database directly via command line:

\`\`\`bash
psql -h localhost -p 5432 -U postgres
\`\`\`

**Password:** \`postgres\`

#### List of Creatio Databases

Creatio databases have names in the format:
\`\`\`
creatio_<site_name>
\`\`\`

For example, if you deployed an application named \`dev1\`, the database will be called \`creatio_dev1\`.

---

## Typical Workflow

### Complete Work Cycle

\`\`\`bash
# 1. Deploy infrastructure (one-time setup)
clio deploy-infrastructure

# 2. Deploy Creatio application
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip
# Enter site name: dev1
# Enter port: 8080 (or press Enter)

# 3. Check status
clio hosts

# 4. Work with application
# Application already started automatically
# Open browser: http://localhost:8080
# Login: Supervisor / Supervisor

# 5. Stop application (when finished)
clio stop -e dev1

# 6. Restart application (next day)
clio start -e dev1

# 7. View logs if needed
clio start -e dev1 --terminal
\`\`\`

### Working with Multiple Environments

\`\`\`bash
# Deploy multiple applications
clio deploy-creatio --ZipFile ~/Downloads/creatio-8.1.2.zip --SiteName dev1 --SitePort 8080
clio deploy-creatio --ZipFile ~/Downloads/creatio-8.1.3.zip --SiteName dev2 --SitePort 8081
clio deploy-creatio --ZipFile ~/Downloads/creatio-8.2.0.zip --SiteName test1 --SitePort 8082

# View all environments
clio hosts

# Stop all environments
clio stop --all

# Start only needed one
clio start -e dev2
\`\`\`

### Daily Work

After initial deployment, your daily workflow will look like this:

\`\`\`bash
# Morning - start Rancher Desktop
# (manually launch Rancher Desktop application and wait for cluster readiness)

# Start needed environment
clio start -e dev1

# Open browser
open http://localhost:8080

# Work with application...

# Evening - stop environment
clio stop -e dev1

# Shutdown Rancher Desktop
# (can leave running or stop manually)
\`\`\`

---

## Troubleshooting

### Issue: "kubectl not found" when running deploy-infrastructure

**Cause:** kubectl is not installed or not available in PATH.

**Solution:**
1. Ensure Rancher Desktop is running
2. Check kubectl availability:
   \`\`\`bash
   kubectl version --client
   \`\`\`
3. If kubectl is not found, install it via Rancher Desktop or manually:
   \`\`\`bash
   brew install kubectl
   \`\`\`

### Issue: PostgreSQL or Redis not responding after deploy-infrastructure

**Cause:** Pods are not yet fully initialized.

**Solution:**
1. Check pod status:
   \`\`\`bash
   kubectl get pods -n clio-infrastructure
   \`\`\`
2. Wait for \`Running\` status for all pods
3. View logs of problematic pod:
   \`\`\`bash
   kubectl logs -n clio-infrastructure <pod-name>
   \`\`\`
4. If necessary, restart deployment:
   \`\`\`bash
   kubectl delete namespace clio-infrastructure
   clio deploy-infrastructure
   \`\`\`

### Issue: Port 8080 already in use

**Cause:** Another application is using port 8080.

**Solution:**
1. Specify a different port during deployment:
   \`\`\`bash
   clio deploy-creatio --ZipFile ~/Downloads/creatio.zip --SitePort 8081
   \`\`\`
2. Or find the process using the port:
   \`\`\`bash
   lsof -i :8080
   \`\`\`
3. Stop the process or use a different port.

### Issue: pgAdmin doesn't open on localhost:1080

**Cause:** pgAdmin service is not running or LoadBalancer didn't get external IP.

**Solution:**
1. Check service status:
   \`\`\`bash
   kubectl get svc -n clio-infrastructure pgadmin-service
   \`\`\`
2. Ensure EXTERNAL-IP is not in \`<pending>\` state
3. For Rancher Desktop, may need to configure Virtual Machine type to \`virtiofs\`
4. Check pgAdmin pod logs:
   \`\`\`bash
   kubectl logs -n clio-infrastructure -l app=pgadmin
   \`\`\`

### Issue: Creatio application doesn't start after deploy-creatio

**Cause:** Configuration error or insufficient resources.

**Solution:**
1. Start application in terminal mode to view logs:
   \`\`\`bash
   clio start -e dev1 --terminal
   \`\`\`
2. Check logs for errors
3. Ensure database is accessible:
   \`\`\`bash
   psql -h localhost -p 5432 -U postgres -l
   \`\`\`
4. Check configuration files:
   \`\`\`bash
   cat ~/creatio/dev1/ConnectionStrings.config
   \`\`\`

### Issue: "Environment not found" when using clio start/stop

**Cause:** Environment is not registered in clio or doesn't have EnvironmentPath.

**Solution:**
1. Check list of registered environments:
   \`\`\`bash
   clio hosts
   \`\`\`
2. If environment is missing, register it manually:
   \`\`\`bash
   clio reg-web-app dev1 --ep ~/creatio/dev1
   \`\`\`

### Issue: Rancher Desktop using too many resources

**Cause:** Virtual machine settings are not optimized.

**Solution:**
1. Open Rancher Desktop settings
2. Go to **Virtual Machine** section
3. Set:
   - Memory: 6GB (minimum) or 8GB (recommended)
   - CPUs: 2-4
4. Restart Rancher Desktop

### Issue: Not enough disk space

**Cause:** Docker images and volumes taking up too much space.

**Solution:**
1. Clean up unused Docker resources:
   \`\`\`bash
   docker system prune -a --volumes
   \`\`\`
2. Remove old Creatio environments:
   \`\`\`bash
   clio uninstall-creatio -e old_env
   \`\`\`
3. Delete unused databases via pgAdmin

### Getting Additional Help

If the issue is not resolved:

1. Check complete command documentation: [Commands.md](Commands.md)
2. Review application logs in environment folder:
   \`\`\`bash
   tail -f ~/creatio/dev1/logs/creatio.log
   \`\`\`
3. Reach out to the Creatio community or open an issue in the clio repository

---

## Useful Commands

### Kubernetes Management

\`\`\`bash
# View all pods in clio-infrastructure namespace
kubectl get pods -n clio-infrastructure

# View all services
kubectl get svc -n clio-infrastructure

# View logs of specific pod
kubectl logs -n clio-infrastructure <pod-name>

# Delete entire infrastructure
kubectl delete namespace clio-infrastructure
\`\`\`

### Clio Environment Management

\`\`\`bash
# List all registered environments
clio hosts
