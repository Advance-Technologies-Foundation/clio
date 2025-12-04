# Create Development Environment for macOS

## Task Description

A unified integrated command is required that automates the complete process of deploying a local Creatio development environment on macOS. The command should execute a sequence of operations to initialize, configure, and prepare the application for development.

## Initial Data
- ZIP archive with Creatio application on .NET 8
- PostgreSQL database
- Kubernetes for infrastructure orchestration

## Main Execution Stages

### 1. **Archive Preparation and Extraction**
- Extract ZIP file to specified directory
- If directory not specified, use current directory
- Create folder named `environment` for file placement

### 2. **Infrastructure Creation**
- Execute command: `clio create infrastructure`
- Deploy required services via kubectl (Redis, pgAdmin, PostgreSQL)
- Configure database connection via `clio rdb`

### 3. **Web Application Configuration**
- Edit connection string (ConnectionString) in configuration
- Set **Maintainer** system setting (user can specify at launch or system will prompt interactively)
- Change `CookiesSameSiteMode` parameter value in `Terrasoft.WebHost.dll.config` file to `Lax`

### 4. **Component Installation**
- Install cliogate via `clio gate` command
- Register environment in clio via `clio cfg` command
- Activate development mode (CDP): `clio cdp true`
- Enable File Design Mode: `clio fsm on`

### 5. **Interactive Parameters**
- **Environment Name** (env_name) - ask first, after initialization (required parameter)
- **Maintainer** - ask second if not specified in parameters (required parameter)
- **Port Number** - use default value or allow override
- **Credentials** - use default values (Supervisor/Supervisor) or allow override

## Usage Examples

### Example 1: Minimal Parameters (all interactive)
```bash
clio create-dev-env --zip ~/Downloads/creatio-application.zip
# System will ask:
# 1. Environment Name: my-dev-env
# 2. Maintainer: John Doe
```

### Example 2: Full Parameter Specification
```bash
clio create-dev-env \
  --zip ~/Downloads/creatio-application.zip \
  --target-dir ~/projects/creatio-env \
  --env-name development \
  --maintainer "John Doe" \
  --port 8080 \
  --username Supervisor \
  --password Supervisor
```

### Example 3: With Existing Directory
```bash
clio create-dev-env \
  --zip ~/Downloads/creatio-app.zip \
  --target-dir ~/existing-project \
  --env-name prod-dev \
  --maintainer "Admin User"
```

### Example 4: Using Current Directory (will create environment folder)
```bash
cd ~/projects/my-creatio
clio create-dev-env --zip ~/Downloads/creatio-application.zip --env-name local-dev
```

### Example 5: With Alternative Credentials
```bash
clio create-dev-env \
  --zip ~/Downloads/creatio-application.zip \
  --env-name staging \
  --maintainer "Dev Team" \
  --port 443 \
  --username CustomAdmin \
  --password CustomPassword123
```

## Command Parameters

| Parameter | Flag | Type | Required | Description |
|----------|------|------|----------|---------|
| ZIP file | `--zip` | string | Yes | Path to Creatio application ZIP archive |
| Target directory | `--target-dir` | string | No | Directory for deployment (default: current) |
| Environment name | `--env-name` | string | No | Environment name (will ask interactively if not specified) |
| Maintainer | `--maintainer` | string | No | Maintainer system setting (will ask interactively if not specified) |
| Port | `--port` | int | No | Application port (default: 8080) |
| Username | `--username` | string | No | Credentials (default: Supervisor) |
| Password | `--password` | string | No | Password (default: Supervisor) |
| Skip confirmation | `--no-confirm` | bool | No | Don't ask for confirmation before execution |
