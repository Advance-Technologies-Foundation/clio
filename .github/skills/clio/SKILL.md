---
name: clio
description: 'CLI tool for Creatio platform integration with development and CI/CD workflows. Use when asked to manage Creatio environments, install or push packages, create workspaces, compile configuration, deploy applications, run SQL scripts, manage system settings, restart Creatio, work with NuGet packages, apply GitOps manifests, or perform any Creatio development task. Triggers on mentions of clio, Creatio, Terrasoft, creatio packages, creatio environment, creatio workspace, or bpm online.'
---

# Clio — Creatio CLI Tool

Clio is a command-line utility for integrating the Creatio platform with development and CI/CD tools. It manages environments, packages, workspaces, applications, and infrastructure for Creatio instances.

**Repository**: https://github.com/Advance-Technologies-Foundation/clio
**Full command reference**: See `references/commands-reference.md` bundled with this skill.

## Prerequisites

- .NET 8 SDK installed
- Clio installed: `dotnet tool install clio -g`
- For advanced features: cliogate installed on Creatio instance (`clio install-gate -e <ENV>`)

Verify installation:
```bash
clio info
# or
clio ver
```

## When to Use This Skill

- User mentions **clio**, **Creatio**, **Terrasoft**, or **bpm'online**
- User wants to manage Creatio **environments** (register, ping, healthcheck)
- User wants to **install, push, pull, or compile** Creatio packages
- User asks about **workspaces** (create, restore, push, build)
- User needs to **restart**, **compile configuration**, or manage **system settings**
- User wants to **deploy applications**, manage **licenses**, or work with **features**
- User asks about **CI/CD** with Creatio, GitOps manifests, or deployment scenarios
- User wants to execute **SQL scripts** or **service calls** against Creatio
- User needs to set up **infrastructure** (Kubernetes, Docker) for Creatio
- User wants to **create or modify entity schemas** (columns, types, lookups) in Creatio
- User wants to **find or discover entity schemas** by name, pattern, or UId in Creatio
- User wants to **read or update Freedom UI pages** (page-get, page-update, page-list)
- User wants to **manage package data bindings** (seed data for SysSettings, SysModule, custom entities)

## General Syntax

```bash
clio <COMMAND> [arguments] [command_options]
```

Common options for most commands:
- `-e <ENV>` — environment name (from registered environments)
- `-u <URI>` — Creatio application URL
- `-l <LOGIN>` — user login
- `-p <PASSWORD>` — user password

## Core Workflows

### 1. Environment Setup

Register and manage Creatio environments:

```bash
# Register environment
clio reg-web-app myenv -u https://mysite.creatio.com -l administrator -p password

# Set active environment
clio reg-web-app -a myenv

# List all environments
clio show-web-app-list --short

# Ping to verify
clio ping myenv
# or: clio ping-app myenv

# Health check
clio healthcheck myenv

# Get instance info (requires cliogate)
clio get-info -e myenv

# Interactive environment manager (TUI). Aliases: gui, far
clio env-ui

# Open in browser
clio open myenv

# Remove environment
clio unreg-web-app myenv
```

### 2. Package Management

Create, install, pull, and manage packages:

```bash
# Create new package
clio new-pkg MyPackage

# Install package from directory
clio push-pkg MyPackage -e myenv

# Install .gz package
clio push-pkg package.gz -e myenv

# Install from marketplace by ID
clio push-pkg --id 22966 -e myenv

# For composable apps use push-app
clio push-app package.gz -e myenv

# Download package from environment
clio pull-pkg MyPackage -e myenv

# Compile package
clio compile-package MyPackage -e myenv

# Delete package
clio delete-pkg-remote MyPackage -e myenv

# List installed packages
clio get-pkg-list -e myenv
clio get-pkg-list -e myenv -f CustomPrefix -j

# Compress/extract
clio generate-pkg-zip MyPackage
clio extract-pkg-zip package.gz -d ./output

# Lock/unlock
clio lock-package MyPackage -e myenv
clio unlock-package MyPackage -e myenv

# Package version
clio set-pkg-version ./MyPackage -v 1.2.0
clio get-pkg-version ./MyPackage

# Activate/deactivate (8.1.2+)
clio activate-pkg MyPackage -e myenv
clio deactivate-pkg MyPackage -e myenv

# Hotfix mode
clio pkg-hotfix MyPackage true -e myenv
```

### 3. Application Management

Control Creatio application lifecycle:

```bash
# Restart application
clio restart-web-app myenv

# Start/stop local Creatio
clio start -e myenv
clio stop -e myenv
clio stop --all --silent

# Clear Redis cache
clio clear-redis-db myenv

# Compile all configuration
clio compile-configuration -e myenv
clio compile-configuration --all -e myenv

# Get compilation log
clio last-compilation-log -e myenv

# System settings
clio set-syssetting MySetting "MyValue" -e myenv
clio get-syssetting MySetting --GET -e myenv

# Developer mode
clio set-dev-mode true -e myenv

# Features
clio set-feature MyFeature 1 -e myenv
clio set-feature MyFeature 0 -e myenv

# Web service URL
clio set-webservice-url ServiceName https://api.example.com -e myenv
clio get-webservice-url -e myenv

# Applications
clio get-app-list -e myenv
clio download-application MyApp -e myenv
clio deploy-application MyApp -e source -d target
clio install-application ./MyApp.gz -e myenv
clio uninstall-app-remote MyApp -e myenv
clio create-app-section --application-code UsrOrdersApp --caption "Orders" -e myenv
clio create-app-section --application-code UsrSalesApp --caption "Accounts" --entity-schema-name Account -e myenv
clio update-app-section --application-code UsrOrdersApp --section-code UsrOrders --caption "Orders" -e myenv

# Upload license
clio upload-license license.lic -e myenv

# File system mode
clio pkg-to-file-system -e myenv
clio pkg-to-db -e myenv
```

### 4. Workspace Development

Professional development flow with workspaces:

```bash
# Create workspace (connected to environment)
clio create-workspace -e myenv

# Create empty workspace
clio create-workspace my-workspace --empty

# Restore workspace from environment
clio restore-workspace -e myenv

# Build workspace
clio build-workspace

# Push to environment
clio push-workspace -e myenv

# Configure workspace packages (canonical: cfg-worspace)
clio cfgw --Packages Pkg1,Pkg2 -e myenv

# Download configuration (libraries)
clio download-configuration -e myenv
clio dconf --build path/to/creatio.zip

# Install cliogate (required for advanced features)
clio install-gate -e myenv

# Install T.I.D.E.
clio install-tide -e myenv

# Merge workspaces
clio merge-workspaces --workspaces path1,path2 -e myenv

# Publish workspace to ZIP archive or app hub
clio publish-app --file ./output.zip --repo-path ./workspace
clio publish-app --repo-path ./workspace --app-hub /hub/path --app-name MyApp -e myenv

# Link to file design mode
clio link-from-repository -e myenv --repoPath ./packages --packages "*"
```

### 4a. Existing App Section Creation

Use this flow when the app already exists in Creatio and only a new section must be added:

```bash
# Inspect installed apps first when the app code or id is unknown
clio get-app-list -e myenv

# Create a section with a new object
clio create-app-section --application-code UsrOrdersApp --caption "Orders" -e myenv

# Create a section bound to an existing entity
clio create-app-section \
  --application-code UsrSalesApp \
  --caption "Accounts" \
  --entity-schema-name Account \
  -e myenv

# Create a web-only section
clio create-app-section \
  --application-code UsrSalesApp \
  --caption "Visits" \
  --with-mobile-pages false \
  -e myenv
```

Rules:
- pass `--application-code` for the installed target app
- pass `--entity-schema-name` only when reusing an existing entity
- omit entity options to let Creatio create a new object for the new section

### 4b. Existing App Section Update

Use this flow when the app and section already exist in Creatio and only section metadata must change:

```bash
# Fix a broken JSON-style heading
clio update-app-section \
  --application-code UsrOrdersApp \
  --section-code UsrOrders \
  --caption "Orders" \
  -e myenv

# Update only description
clio update-app-section \
  --application-code UsrSalesApp \
  --section-code AccountSection \
  --description "Key customer accounts" \
  -e myenv

# Update only icon metadata
clio update-app-section \
  --application-code UsrSalesApp \
  --section-code VisitSection \
  --icon-id 11111111-1111-1111-1111-111111111111 \
  --icon-background "#A1B2C3" \
  -e myenv
```

Rules:
- pass `--application-code` and `--section-code`
- provide at least one mutable field: `--caption`, `--description`, `--icon-id`, or `--icon-background`
- omit fields that must stay unchanged

### 5. Development Tools

Code generation, SQL, service calls:

```bash
# Add item from template
clio add-item service MyService -n MyCompany.Services
clio add-item entity-listener MyListener -n MyCompany.Listeners

# Generate ATF model
clio add-item model Contact -f Name,Email -n MyNameSpace -d . -e myenv

# Generate all models
clio add-item model -n MyCompany.Models -e myenv

# Generate process model for ATF.Repository
clio generate-process-model MyProcess -n MyNameSpace -e myenv

# Add schema
clio add-schema MySchema -t source-code -p MyPackage

# Create test project
clio new-test-project --package MyPackage

# Execute SQL
clio execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'" -e myenv
clio execute-sql-script -f query.sql -e myenv

# Call service (GET)
clio call-service --service-path ServiceModel/AppInfoService.svc/GetInfo -e myenv

# Call service (POST with inline body)
clio call-service --service-path ServiceModel/YourService.svc/Method \
  --body '{"key":"value"}' -e myenv

# DataService
clio ds -t select --body '{"rootSchemaName":"Contact","operationType":0}' -e myenv
clio ds -t insert --body '{"rootSchemaName":"Contact","values":{"Name":"John"}}' -e myenv

# Convert package to project
clio convert MyPackage

# Set references
clio ref-to src
clio ref-to bin

# Switch NuGet to DLL
clio nuget2dll MyPackage

# Mock data for tests
clio mock-data -m ./Models -d ./TestData -e myenv

# Listen to logs
clio listen --loglevel Debug -e myenv

# Show package files (canonical: show-package-file-content)
clio show-files --package MyPackage -e myenv
```

### 6. NuGet Package Management

```bash
# Pack Creatio package as NuGet
clio pack-nuget-pkg ./MyPackage

# Push to NuGet repository
clio push-nuget-pkg ./MyPackage.nupkg --ApiKey KEY --Source URL

# Restore NuGet package
clio restore-nuget-pkg PackageName

# Install NuGet to Creatio
clio install-nuget-pkg PackageName -e myenv

# Check for updates
clio check-nuget-update
```

### 7. CI/CD & GitOps

```bash
# Apply manifest to instance
clio apply-manifest manifest.yaml -e myenv

# Save instance state to manifest
clio save-state manifest.yaml -e myenv

# Compare two environments
clio show-diff --source production --target qa
clio show-diff --source production --target qa --file diff.yaml

# Run automation scenario
clio run --file-name scenario.yaml

# Clone environment
clio clone-env --source Dev --target QA
```

### 8. Entity Schema Management

Create and evolve entity schemas directly on a remote Creatio instance. Requires cliogate.

```bash
# Discover which package owns a schema (no package name needed)
clio find-entity-schema -e myenv --search-pattern Vehicle
clio find-entity-schema -e myenv --schema-name UsrVehicle
clio find-entity-schema -e myenv --uid 117d32f9-aab9-4e3a-b13e-cfce62e15e4b

# Create entity schema with columns
clio create-entity-schema --package MyPackage --name UsrVehicle --title "Vehicle" \
  --column "Make:ShortText:Manufacturer" \
  --column "OwnerId:Lookup:Owner:Contact" \
  -e myenv

# Verify created schema
clio get-entity-schema-properties -e myenv --package MyPackage --schema-name UsrVehicle

# Add a column
clio modify-entity-schema-column --package MyPackage --schema-name UsrVehicle \
  --action add --column-name Year --type Integer --title "Year" -e myenv

# Remove a column
clio modify-entity-schema-column --package MyPackage --schema-name UsrVehicle \
  --action remove --column-name ObsoleteField -e myenv

# Batch operations
clio update-entity-schema --package MyPackage --schema-name UsrVehicle \
  --operation '{"action":"add","columnName":"Color","type":"ShortText"}' \
  --operation '{"action":"add","columnName":"Mileage","type":"Integer"}' \
  -e myenv

# Batch default from SystemValue caption (normalized to Guid)
clio update-entity-schema --package MyPackage --schema-name UsrVehicle \
  --operation '{"action":"modify","column-name":"UsrStartDate","default-value-config":{"source":"SystemValue","value-source":"Current Time and Date"}}' \
  -e myenv

# Read specific column properties
clio get-entity-schema-column-properties -e myenv --package MyPackage \
  --schema-name UsrVehicle --column-name Make
```

`find-entity-schema` CLI output is labeled as `Schema: ... | Package: ... | Maintainer: ...` so transcript parsing stays unambiguous. When the same lookup is done through MCP, read the returned `package-name` field directly for follow-up tool calls instead of parsing CLI-style text or falling back to `get-pkg-list`.

Default resolution behavior for entity schema defaults:
- `SystemValue` accepts Guid, alias, or caption and persists canonical Guid.
- `Settings` accepts code, name, or id and persists canonical setting code.
- `get-entity-schema-column-properties` reports canonical identifiers in `default-value-config.resolved-value-source`.
- Ambiguous matches fail fast and require explicit Guid/code input.

### 9. Freedom UI Page Management

Read and update Freedom UI page schemas.

```bash
# Discover pages
clio page-list -e myenv
clio page-list --search-pattern FormPage --limit 20 -e myenv

# Read page (get raw.body for editing)
clio page-get --schema-name UsrTodo_FormPage -e myenv

# Validate without saving
clio page-update --schema-name UsrTodo_FormPage --body "<raw body>" --dry-run true -e myenv

# Save updated page
clio page-update --schema-name UsrTodo_FormPage --body "<edited body>" -e myenv

# Save with resource string registration
clio page-update --schema-name UsrTodo_FormPage --body "<edited body>" \
  --resources '{"UsrDetailsTab_caption":"Details"}' -e myenv
```

For updating multiple pages in one call, use the `page-sync` MCP tool.

### 10. Data Bindings

Create and manage package data bindings for seed data.

```bash
# Create SysSettings binding (offline — no environment needed)
clio create-data-binding --package MyPackage --schema SysSettings

# Create binding for custom entity
clio create-data-binding -e myenv --package MyPackage --schema UsrVehicle \
  --values '{"Name":"Default vehicle"}'

# Add or update a row
clio add-data-binding-row --package MyPackage --binding-name SysSettings \
  --values '{"Name":"My setting","Code":"UsrMySetting"}'

# Remove a row by primary key
clio remove-data-binding-row --package MyPackage --binding-name SysSettings \
  --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15

# DB-first flow (saves directly to remote DB)
clio create-data-binding-db -e myenv --package MyPackage --schema SysSettings \
  --rows '[{"values":{"Name":"Row","Code":"UsrRow"}}]'
clio upsert-data-binding-row-db -e myenv --package MyPackage --binding-name SysSettings \
  --values '{"Name":"Updated","Code":"UsrRow"}'
```

### 11. Infrastructure & Deployment

```bash
# Deploy Kubernetes infrastructure (PostgreSQL, Redis, pgAdmin)
clio deploy-infrastructure

# Generate K8s files with custom resources
clio create-k8-files --pg-limit-memory 8Gi --pg-limit-cpu 4

# Deploy Creatio from ZIP
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip

# List deployed hosts
clio hosts

# Uninstall Creatio
clio uninstall-creatio -e myenv

# Delete infrastructure
clio delete-infrastructure

# Restore database
clio restore-db --dbServerName my-local-postgres --dbName mydb --backupPath backup.backup

# Update clio
clio update-cli
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| `clio` not found | Run `dotnet tool install clio -g` and ensure `~/.dotnet/tools` is in PATH |
| Ping fails | Check URL, credentials, and network. Use `clio ping <ENV>` |
| cliogate required | Install with `clio install-gate -e <ENV>` |
| Compilation errors | Check `clio last-compilation-log -e <ENV>` |
| Permission denied | Ensure administrator-level Creatio credentials |
| Package locked | Unlock with `clio unlock-package <PKG> -e <ENV>` |
| Entity schema command fails | Ensure cliogate is installed: `clio install-gate -e <ENV>` |
| page-update validation error | Use `--dry-run true` first; check Freedom UI schema markers |

## Important Notes

- Always verify command options with `clio <CMD> --help` — it is the authoritative source
- Always use `-e <ENV>` to target a specific registered environment
- For composable applications use `push-app` instead of `push-pkg`
- cliogate package is required for many advanced features (workspace, get-info, entity schema commands, etc.)
- T.I.D.E. requires cliogate: install with `clio install-tide -e <ENV>`
- Use `clio help` for full command list, `clio <CMD> --help` for command details
- Manifest YAML files support GitOps: apps, syssettings, features, webservices
- Entity schema commands (`create-entity-schema`, `modify-entity-schema-column`, etc.) require cliogate ≥ 2.0
- Freedom UI page commands (`page-get`, `page-update`, `page-list`) work without cliogate
- Data binding commands that work offline (no environment): `create-data-binding` with SysSettings/SysModule templates, `add-data-binding-row`, `remove-data-binding-row`
