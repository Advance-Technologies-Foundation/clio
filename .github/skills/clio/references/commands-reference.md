# Clio Command Reference

Complete reference for all clio CLI commands. Source: [Commands.md](https://github.com/Advance-Technologies-Foundation/clio/blob/master/clio/Commands.md)

## General Syntax

```bash
clio <COMMAND> [arguments] [command_options]
```

### Common Environment Options

| Option | Description |
|--------|-------------|
| `-e, --Environment` | Environment name (registered) |
| `-u, --uri` | Application URI |
| `-l, --Login` | User login |
| `-p, --Password` | User password |
| `-i, --IsNetCore` | Use .NET Core application |
| `-m, --Maintainer` | Maintainer name |
| `--clientId` | OAuth client ID |
| `--clientSecret` | OAuth client secret |
| `--authAppUri` | OAuth app URI |
| `--silent` | No user interaction |

---

## Help & Version

### help
```bash
clio help
clio <COMMAND_NAME> --help
```

### info
Display version and system info. **Aliases:** `ver`, `get-version`, `i`
```bash
clio info          # All versions
clio info --all    # All known components
clio info --clio   # clio version only
clio info --gate   # cliogate version only
clio info --runtime # .NET runtime only
clio info -s       # Settings file path
```

---

## Environment Management

### reg-web-app
Register or update environment settings.
```bash
clio reg-web-app <ENV> -u https://mysite.creatio.com -l admin -p password
clio reg-web-app <ENV> -u https://mysite.creatio.com -l admin -p password -i true  # NET8
clio reg-web-app <ENV> --ep /path/to/creatio  # With environment path
clio reg-web-app -a <ENV>  # Set active environment
```

### unreg-web-app
Remove environment from settings.
```bash
clio unreg-web-app <ENV>
```

### ping-app
Validate environment connectivity. **Aliases:** `ping`
```bash
clio ping-app <ENV>
clio ping <ENV>
```

### show-web-app-list
Display registered environments. **Aliases:** `envs`, `show-web-app`
```bash
clio show-web-app-list           # Full JSON
clio show-web-app-list --short   # Concise table
clio show-web-app-list <ENV>     # Specific env
clio envs --format table         # Table format
clio envs --format raw           # Plain text
```

### env-ui
Interactive console UI for environment management. **Aliases:** `gui`, `far`
```bash
clio env-ui
clio gui
```

### healthcheck
Check application health. **Aliases:** `hc`
```bash
clio healthcheck <ENV>
clio hc <ENV> -a true -h true
```

### get-info
Get system information about Creatio instance. **Aliases:** `describe`, `instance-info`
Requires cliogate ≥ 2.0.0.32.
```bash
clio get-info -e <ENV>
clio describe -e <ENV>
```

### open-web-app
Open environment in default browser (cross-platform). **Aliases:** `open`
```bash
clio open-web-app <ENV>
clio open <ENV>
```

### clone-env
Clone environment settings and packages.
```bash
clio clone-env --source Dev --target QA --working-directory /optional/path
```

### show-local-envs
Display local environments with health status.
```bash
clio show-local-envs
```

### clear-local-env
Remove deleted local environments and orphaned services.
```bash
clio clear-local-env
clio clear-local-env --force
```

### CustomizeDataProtection
Adjust CustomizeDataProtection in appsettings (for Net8 dev). **Aliases:** `cdp`
```bash
clio cdp true -e <ENV>
clio cdp false -e <ENV>
```

---

## Package Management

### new-pkg
Create new package project.
```bash
clio new-pkg <PACKAGE_NAME>
clio new-pkg <PACKAGE_NAME> -r bin  # With local core reference
```

### add-package
Add package with optional app descriptor.
```bash
clio add-package <PACKAGE_NAME> -a True
clio add-package <PACKAGE_NAME> -a True -e env1,env2
```

### push-pkg
Install package to environment.
```bash
clio push-pkg <PACKAGE_NAME>           # From directory
clio push-pkg package.gz               # From archive
clio push-pkg package.gz -r log.txt    # With install log
clio push-pkg --id 22966 10096         # From marketplace
clio push-app package.gz               # For composable apps
clio push-app package.gz --check-configuration-errors true
```

### pull-pkg
Download package from environment.
```bash
clio pull-pkg <PACKAGE_NAME>
clio pull-pkg <PACKAGE_NAME> -e <ENV>
```

### compile-package
Compile specific package.
```bash
clio compile-package <PACKAGE_NAME> -e <ENV>
```

### delete-pkg-remote
Delete package from environment.
```bash
clio delete-pkg-remote <PACKAGE_NAME> -e <ENV>
```

### generate-pkg-zip
Compress package to .gz archive.
```bash
clio generate-pkg-zip <PACKAGE_NAME>
clio generate-pkg-zip C:\Packages\pkg -d C:\Store\pkg.gz
```

### extract-pkg-zip
Extract package from .gz archive.
```bash
clio extract-pkg-zip package.gz -d ./output
```

### get-pkg-list
List installed packages. **Aliases:** `packages`
```bash
clio get-pkg-list -e <ENV>
clio get-pkg-list -e <ENV> -f CustomPrefix -j  # Filter + JSON
```

### set-pkg-version / get-pkg-version
```bash
clio set-pkg-version <PATH> -v 1.2.0
clio get-pkg-version <PATH>
```

### set-app-version
```bash
clio set-app-version <WORKSPACE_PATH> -v 1.0.0
```

### set-app-icon
```bash
clio set-app-icon -p MyAppName -i /path/to/icon.svg -f /path/to/app
```

### lock-package / unlock-package
**Aliases:** `lp` / `up`. Requires cliogate ≥ 2.0.0.
```bash
clio lock-package <PACKAGE_NAME> -e <ENV>
clio unlock-package <PACKAGE_NAME> -e <ENV>
clio unlock-package Pkg1,Pkg2,Pkg3 -e <ENV>  # Multiple
clio unlock-package -m Creatio -e <ENV>       # All packages
```

### activate-pkg / deactivate-pkg
Requires Creatio ≥ 8.1.2.
```bash
clio activate-pkg <PACKAGE_NAME> -e <ENV>
clio deactivate-pkg <PACKAGE_NAME> -e <ENV>
```

### pkg-hotfix
Enable/disable hotfix mode.
```bash
clio pkg-hotfix <PACKAGE_NAME> true -e <ENV>
clio pkg-hotfix <PACKAGE_NAME> false -e <ENV>
```

### validation-pkg
Validate package structure. **Aliases:** `validation`
```bash
clio validation-pkg <PACKAGE_NAME>
clio validation-pkg <PACKAGE_NAME> -d ./results
```
Options: `-d, --DestinationResult` (destination path for validation results)

---

## Application Management

### restart-web-app
Restart Creatio application.
```bash
clio restart-web-app
clio restart-web-app <ENV>
```

### start
Start local Creatio. Auto-detects IIS or .NET Core. **Aliases:** `start-server`, `start-creatio`, `sc`
```bash
clio start -e <ENV>
clio start -e <ENV> --terminal  # With terminal window (.NET Core)
```
Requires `EnvironmentPath` configured: `clio reg-web-app <ENV> --ep /path/to/creatio`

### stop
Stop Creatio services/processes. **Aliases:** `stop-creatio`
```bash
clio stop -e <ENV>
clio stop --all
clio stop --all --silent
```

### clear-redis-db
Clear Redis cache.
```bash
clio clear-redis-db
clio clear-redis-db <ENV>
```

### compile-configuration
Compile configuration with progress monitoring. **Aliases:** `cc`, `compile-remote`
```bash
clio compile-configuration -e <ENV>
clio cc -e <ENV>
clio compile-configuration --all -e <ENV>  # Full rebuild
```
Requires cliogate.

### last-compilation-log
```bash
clio last-compilation-log -e <ENV>
clio last-compilation-log -e <ENV> --raw
clio last-compilation-log -e <ENV> --log "C:\log.txt"
```

### set-syssetting / get-syssetting
```bash
clio set-syssetting <CODE> <VALUE> -e <ENV>
clio get-syssetting <CODE> --GET -e <ENV>
```

### set-dev-mode
```bash
clio set-dev-mode true -e <ENV>
clio set-dev-mode false -e <ENV>
```

### set-feature
```bash
clio set-feature <CODE> 1 -e <ENV>   # Enable
clio set-feature <CODE> 0 -e <ENV>   # Disable
clio set-feature <CODE> 1 --SysAdminUnitName Supervisor
```

### set-webservice-url / get-webservice-url
```bash
clio set-webservice-url <SERVICE_NAME> <BASE_URL> -e <ENV>
clio get-webservice-url -e <ENV>
clio get-webservice-url <SERVICE_NAME> -e <ENV>
```

### download-application
Download application from environment. **Aliases:** `dapp`, `download-app`
```bash
clio download-application <APP_NAME> -e <ENV>
clio download-app <APP_NAME> -e <ENV> --FilePath output.zip
```

### deploy-application
Deploy application between environments. **Aliases:** `deploy-app`
```bash
clio deploy-application <APP_NAME> -e <SOURCE> -d <TARGET>
```

### install-application
Install application from file or marketplace. **Aliases:** `install-app`
```bash
clio install-application ./MyApp.gz -e <ENV>
clio install-application --id 12345 -e <ENV>
```

### publish-app
Publish workspace to zip or app hub. **Aliases:** `publishw`, `publish-hub`, `ph`, `publish-workspace`
```bash
clio publish-app --file ./out.zip --repo-path ./workspace
clio publish-app --repo-path ./workspace --app-hub /hub/path --app-name MyApp -e <ENV>
```

### uninstall-app-remote
```bash
clio uninstall-app-remote <APP_NAME> -e <ENV>
```

### list-apps
List installed applications. **Aliases:** `get-app-list`, `apps`, `lia`
```bash
clio list-apps -e <ENV>
clio apps -e <ENV>
```

### create-app-section
Create a section inside an existing installed application.
```bash
clio create-app-section --application-code UsrOrdersApp --caption "Orders" -e <ENV>
clio create-app-section --application-code UsrSalesApp --caption "Accounts" --entity-schema-name Account -e <ENV>
clio create-app-section --application-code UsrSalesApp --caption "Visits" --with-mobile-pages false -e <ENV>
```
Rules: require `--application-code`; provide `--entity-schema-name` when the section must reuse an existing entity; omit that field to let Creatio create a new object; `--with-mobile-pages` is optional and defaults to `true`.

### update-app-section
Update metadata of a section inside an existing installed application.
```bash
clio update-app-section --application-code UsrOrdersApp --section-code UsrOrders --caption "Orders" -e <ENV>
clio update-app-section --application-code UsrSalesApp --section-code AccountSection --description "Key customer accounts" -e <ENV>
clio update-app-section --application-code UsrSalesApp --section-code VisitSection --icon-id 11111111-1111-1111-1111-111111111111 --icon-background "#A1B2C3" -e <ENV>
```
Rules: require `--application-code` and `--section-code`; provide at least one mutable field from `--caption`, `--description`, `--icon-id`, `--icon-background`; omitted fields remain unchanged; caption updates persist plain text and can repair broken JSON-style headings.

### delete-app-section
Delete a section and all its metadata artifacts from an existing installed application.
```bash
clio delete-app-section --application-code UsrOrdersApp --section-code UsrOrders -e <ENV>
clio delete-app-section --application-code UsrOrdersApp --section-code UsrOrders --delete-entity-schema -e <ENV>
```
Rules: require `--application-code` and `--section-code`; by default the underlying entity schema is preserved (data is not lost); pass `--delete-entity-schema` to also remove the entity schema; deletes SysModuleInWorkplace, SysModuleLcz, all Freedom UI page schemas and addon schemas, SysModuleEntity, and SysModule in correct FK order; destructive and cannot be undone; cliogate must be installed.

### list-app-sections
List sections of an existing installed application as a human-readable table.
```bash
clio list-app-sections --application-code UsrOrdersApp -e <ENV>
clio list-app-sections --application-code UsrOrdersApp --json -e <ENV>
```
Rules: require `--application-code`; default output is a table with columns Code, Caption, EntitySchemaName, Description preceded by an application header line; use `--json` for indented JSON output suitable for scripting.

### upload-license
Upload license file. **Aliases:** `license`, `loadlicense`, `load-license`
```bash
clio upload-license license.lic -e <ENV>
```

### upload-licenses
Upload licenses (batch). **Aliases:** `lic`
```bash
clio upload-licenses license.lic -e <ENV>
```

### pkg-to-file-system / pkg-to-db
Switch between file system and database modes.
```bash
clio pkg-to-file-system -e <ENV>
clio pkg-to-db -e <ENV>
```

### set-fsm-config / turn-fsm
Configure file system mode.
```bash
clio set-fsm-config --environmentName <ENV> on
clio turn-fsm --environmentName <ENV> off
```

---

## Workspaces

### create-workspace
```bash
clio create-workspace                          # With current environment
clio create-workspace -e <ENV>                 # With specific environment
clio create-workspace my-workspace --empty     # Empty workspace
clio create-workspace --AppCode <APP_CODE>     # For specific app
```

### restore-workspace
Download packages and create solution. **Aliases:** `restorew`, `pullw`, `pull-workspace`
Requires cliogate ≥ 2.0.0.
```bash
clio restore-workspace -e <ENV>
```
Options: `--IsNugetRestore`, `--IsCreateSolution`, `--AddBuildProps`, `--AppCode`

### push-workspace
Push workspace code to environment.
```bash
clio push-workspace -e <ENV>
clio push-workspace -e <ENV> --unlock
```

### build-workspace
```bash
clio build-workspace
```

### cfg-worspace
Configure workspace packages. **Aliases:** `cfgw`
```bash
clio cfgw --Packages Pkg1,Pkg2 -e <ENV>
```

### merge-workspaces
Merge packages from multiple workspaces. **Aliases:** `mergew`
```bash
clio merge-workspaces --workspaces path1,path2 -e <ENV>
clio merge-workspaces --workspaces path1,path2 --output ./out --name MergedPkgs
```

### publish-app (workspace)
Publish workspace to file or app hub. **Aliases:** `publishw`, `publish-hub`, `ph`, `publish-workspace`
```bash
clio publish-workspace --file ./output.zip --repo-path ./workspace
clio publish-app --app-name MyApp --app-hub /hub/path --repo-path ./workspace -e <ENV>
```

### download-configuration
Download configuration (libraries/assemblies). **Aliases:** `dconf`
```bash
clio download-configuration -e <ENV>           # From environment
clio dconf --build /path/to/creatio.zip        # From ZIP
clio dconf --build /path/to/extracted/dir      # From directory
```

### install-gate
Install cliogate service package. **Aliases:** `update-gate`, `gate`, `installgate`
```bash
clio install-gate -e <ENV>
```

### install-tide
Install T.I.D.E. extension. **Aliases:** `tide`, `itide`
```bash
clio install-tide -e <ENV>
```

### Package Filtering
Configure in `.clio/workspaceSettings.json`:
```json
{
  "IgnorePackages": ["*Test*", "Demo*", "Sample*"]
}
```
Supports: exact match, `*` wildcards, `?` single char. Case-insensitive.

### External Packages
Configure in `.clio/workspaceSettings.json`:
```json
{
  "Packages": ["MyAppPkg"],
  "ExternalPackages": ["SharedLib"]
}
```
External packages placed in `packages/` folder above workspace root. Auto-resolves dependencies on publish.

---

## Development

### convert
Convert package to project.
```bash
clio convert <PACKAGE_NAMES>
clio convert -p "C:\Pkg\" MyApp,MyIntegration
clio convert -p "C:\Pkg\"  # All packages in folder
```

### execute-assembly-code
Execute code from assembly.
```bash
clio execute-assembly-code -f myassembly.dll -t MyNamespace.CodeExecutor
```

### ref-to
Set references for project.
```bash
clio ref-to src   # Source references
clio ref-to bin   # Binary references
```

### execute-sql-script
Execute SQL script on Creatio.
```bash
clio execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'" -e <ENV>
clio execute-sql-script -f query.sql -e <ENV>
```

### call-service
Make HTTP calls to service endpoints.
```bash
# GET request
clio call-service --service-path ServiceModel/AppInfoService.svc/GetInfo -e <ENV>

# POST with inline body
clio call-service --service-path ServiceModel/YourService.svc/Method \
  --body '{"key":"value"}' -e <ENV>

# POST with file
clio call-service --service-path ServiceModel/YourService.svc/Method \
  --input request.json --destination result.json -e <ENV>

# DELETE method
clio call-service --service-path ServiceModel/DeleteService.svc/Remove \
  --method DELETE --body '{"id":123}' -e <ENV>

# Variable substitution
clio call-service --service-path ServiceModel/UserService.svc/Get \
  --body '{"userId":"{{userId}}"}' --variables userId=12345 -e <ENV>
```
Options: `--service-path`, `--method` (GET|POST|DELETE), `--input` / `--body`, `--destination`, `--variables`

### dataservice
Execute DataService requests. **Aliases:** `ds`
```bash
# SELECT
clio ds -t select --body '{"rootSchemaName":"Contact","operationType":0}' -e <ENV>

# INSERT
clio ds -t insert --body '{"rootSchemaName":"Contact","values":{"Name":"John"}}' -e <ENV>

# UPDATE with variables
clio ds -t update --body '{"rootSchemaName":"Contact","values":{"Name":"{{name}}"}}' \
  --variables name=Jane -e <ENV>

# DELETE
clio ds -t delete --body '{"rootSchemaName":"Contact","filters":{"Id":"{{id}}"}}' \
  --variables id=12345 -e <ENV>
```
Options: `-t` (select|insert|update|delete), `--body` / `-f`, `-d`, `-v`

### add-item
Create items from templates or generate ATF models.
```bash
clio add-item service MyService -n MyCompany.Services
clio add-item entity-listener MyListener -n MyCompany.Listeners
clio add-item model Contact -f Name,Email -n MyNameSpace -d . -e <ENV>
clio add-item model -n MyCompany.Models -e <ENV>  # All entities
```
Options: `-d` (destination), `-n` (namespace), `-f` (fields), `-a` (all entities), `-x` (culture)

### add-schema
Add cs schema to project.
```bash
clio add-schema <SCHEMA_NAME> -t source-code -p <PACKAGE_NAME>
```

### generate-process-model
Generate process model for ATF.Repository. **Aliases:** `gpm`
Requires cliogate.
```bash
clio generate-process-model <PROCESS_CODE> -n MyNameSpace -d . -e <ENV>
```
Options: `-d` (destination, default: `.`), `-n` (namespace, default: `AtfTIDE.ProcessModels`), `-x` (culture, default: `en-US`)

### new-test-project
Create a unit test project for a package. **Aliases:** `unit-test`, `create-test-project`
```bash
clio new-test-project --package MyPackage
```

### nuget2dll (switch-nuget-to-dll-reference)
Convert NuGet references to DLL references.
```bash
clio nuget2dll <PACKAGE_NAME>
```

### link-from-repository
Link workspace packages to file design mode. **Aliases:** `l4r`
```bash
clio l4r -e <ENV> -p * -r ./packages
clio l4r --envPkgPath /path/to/Pkg --repoPath ./packages --packages "*"
```

### link-to-repository
Link environment packages to repository (Windows only). **Aliases:** `l2r`, `link2repo`
```bash
clio link-to-repository -r ./packages -e /path/to/Pkg
```
Options: `-r, --repoPath` (required), `-e, --envPkgPath` (required)

### link-core-src
Link Creatio core source code. **Aliases:** `lcs`
```bash
clio link-core-src -e <ENV> -c /path/to/core
```

### link-package-store
Link PackageStore to environment. **Aliases:** `lps`
```bash
clio lps --packageStorePath /store --envPkgPath /path/to/Pkg
```

### mock-data
Generate mock data for unit tests. **Aliases:** `data-mock`
```bash
clio mock-data -m ./Models -d ./Tests/Data -e <ENV>
```

### get-app-hash
Calculate hash for application directory.
```bash
clio get-app-hash
clio get-app-hash /path/to/dir
```

### git-sync
Synchronize environment with Git. **Aliases:** `sync`
```bash
clio git-sync --Direction git-to-env -e <ENV>
clio git-sync --Direction env-to-git -e <ENV>
```

### listen
Subscribe to Creatio telemetry websocket stream.
```bash
clio listen --loglevel Debug -e <ENV>
clio listen --loglevel Debug --logPattern ExceptNoisyLoggers -e <ENV>
clio listen --FileName logs.txt --Silent true -e <ENV>
```

### show-package-file-content
Show package file content. **Aliases:** `show-files`, `files`
```bash
clio show-package-file-content --package <PACKAGE_NAME> -e <ENV>
clio show-files --package <PACKAGE_NAME> -e <ENV>
clio show-files --package <PACKAGE_NAME> --file <FILE_PATH> -e <ENV>
```

### get-build-info
Get product build info. **Aliases:** `buildinfo`, `bi`
```bash
clio get-build-info --Product studio --DBType PostgreSQL --RuntimePlatform Net6
```

---

## NuGet Packages

### pack-nuget-pkg
```bash
clio pack-nuget-pkg ./MyPackage
clio pack-nuget-pkg ./MyPackage --Dependencies Dep1:1.0,Dep2
```

### push-nuget-pkg
```bash
clio push-nuget-pkg ./pkg.nupkg --ApiKey KEY --Source URL
```

### restore-nuget-pkg
```bash
clio restore-nuget-pkg PackageName:1.0.0 --DestinationDirectory ./out
```

### install-nuget-pkg
```bash
clio install-nuget-pkg PackageName -e <ENV>
clio install-nuget-pkg Pkg1,Pkg2:2.0 --Source URL -e <ENV>
```

### check-nuget-update
```bash
clio check-nuget-update
```

### update-cli
Update clio to latest version. **Aliases:** `update`
```bash
clio update-cli        # Interactive
clio update -y         # Auto-confirm
```

---

## CI/CD & GitOps

### Manifest YAML Structure
```yaml
environment:
  url: https://production.creatio.com
  username: <CREATIO_LOGIN>
  password: <CREATIO_PASSWORD>
apps:
  - name: CrtCustomer360
    version: "1.0.1"
    apphub: MyAppHub
syssettings:
  - name: Setting1
    value: Value1
features:
  - name: Feature1
    enabled: "true"
webservices:
  - name: Service1
    url: "https://api.example.com"
```
Security note: never commit real credentials to Git. Use placeholders and inject secrets from a secure store (CI variables, vault, or local secret manager).

### apply-manifest
```bash
clio apply-manifest manifest.yaml -e <ENV>
```

### save-state
Download instance state to manifest.
```bash
clio save-state manifest.yaml -e <ENV>
```

### show-diff
Compare two environments.
```bash
clio show-diff --source production --target qa
clio show-diff --source production --target qa --file diff.yaml
```

### run
Execute automation scenario. **Aliases:** `scenario`, `run-scenario`
```bash
clio run --file-name scenario.yaml
```

Scenario example:
```yaml
secrets:
  Login: <LOGIN_FROM_SECRET_STORE>
  Password: <PASSWORD_FROM_SECRET_STORE>
settings:
  uri: http://localhost:80
steps:
  - action: restart
    description: restart application
    options:
      uri: "{{settings.uri}}"
      Login: "{{secrets.Login}}"
      Password: "{{secrets.Password}}"
```

---

## Infrastructure & Deployment

### deploy-infrastructure
Deploy K8s infrastructure (PostgreSQL, Redis, pgAdmin). **Aliases:** `di`
```bash
clio deploy-infrastructure
clio deploy-infrastructure --force  # Recreate without prompt
```

### delete-infrastructure
Delete K8s infrastructure.
```bash
clio delete-infrastructure
clio delete-infrastructure --force
```

### create-k8-files
Generate K8s deployment scripts. **Aliases:** `ck8f`
```bash
clio create-k8-files
clio create-k8-files --pg-limit-memory 8Gi --pg-limit-cpu 4
clio create-k8-files --mssql-limit-memory 4Gi --mssql-limit-cpu 2
```

### open-k8-files
Open K8s deployment files folder. **Aliases:** `cfg-k8f`, `cfg-k8s`, `cfg-k8`
```bash
clio open-k8-files
```

### deploy-creatio
Deploy Creatio from ZIP.
```bash
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip --db-server-name my-local-postgres --drop-if-exists
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip --redis-db 3
```
Options: `--ZipFile`, `--db-server-name`, `--drop-if-exists`, `--redis-db`, `--SiteName`, `--SitePort`, `--deployment` (auto|iis|dotnet), `--no-iis`, `--auto-run`

### uninstall-creatio
Remove local Creatio instance. **Aliases:** `uc`
```bash
clio uninstall-creatio -e <ENV>
clio uc -d C:\inetpub\wwwroot\mysite
```

### hosts
List deployed environments and status. **Aliases:** `list-hosts`
```bash
clio hosts
```

### restore-db
Restore database from backup.
```bash
clio restore-db --dbServerName my-local-postgres --dbName mydb --backupPath backup.backup
clio restore-db --dbServerName my-local-mssql --dbName mydb --backupPath backup.bak --drop-if-exists
clio restore-db -e <ENV>  # From environment config
```

### assert
Validate infrastructure resources (K8s or filesystem).
```bash
clio assert k8 --db postgres --db-connect --db-check version
clio assert k8 --redis --redis-connect --redis-ping
clio assert fs --path /path/to/app --user "BUILTIN\IIS_IUSRS" --perm full-control
```

---

## Web Farm

### compare-web-farm-node
Verify web farm node consistency. **Aliases:** `check-web-farm-node`, `check-farm`, `farm-check`, `cwf`
```bash
clio compare-web-farm-node "\\Node1\Creatio,\\Node2\Creatio" -d
```

### turn-farm-mode
Configure IIS for web farm deployment.
```bash
clio turn-farm-mode -e <ENV>
```

### compare-web-farm-node
Compare two farm nodes.
```bash
clio compare-web-farm-node C:\node1 C:\node2
```

---

## Additional Commands

### restore-configuration
```bash
clio restore-configuration
clio restore-configuration -d  # Without rollback data
clio restore-configuration -f  # Without SQL backward compatibility check
```

### open-settings
Open clio config file.
```bash
clio open-settings
```

### new-ui-project
Create Freedom UI project.
```bash
clio new-ui-project <PROJECT_NAME> --package <PACKAGE_NAME> --vendor-prefix <vendorprefix>
```

### register / unregister
Windows context menu integration.
```bash
clio register    # Add to context menu
clio unregister  # Remove from context menu
```

### alm-deploy
Deploy via ALM workflow. **Aliases:** `deploy`
```bash
clio alm-deploy <PACKAGE_NAME> -e <ENV>
```

### check-windows-features
Check required Windows components (IIS, .NET Framework, etc.). **Aliases:** `checkw`, `cf`
Windows only.
```bash
clio check-windows-features
```

### compressApp
Compress application packages into zip file. **Aliases:** `comp-app`
```bash
clio compressApp -s /path/to/packages -p Pkg1,Pkg2 -d /path/to/output
```
Options: `-s, --SourcePath` (required), `-p, --Packages` (required), `-d, --DestinationPath` (required), `--SkipPdb` (default: true)

### externalLink
Handle external deep links. **Aliases:** `link`
```bash
clio externalLink <CONTENT>
clio link <CONTENT>
```

---

## Entity Schema Management

### create-entity-schema
Create an entity schema in a remote Creatio package. Requires cliogate.
```bash
clio create-entity-schema --package MyPackage --name UsrVehicle --title "Vehicle" -e <ENV>

# With columns inline
clio create-entity-schema --package MyPackage --name UsrVehicle --title "Vehicle" \
  --column "Name:ShortText:Vehicle name" \
  --column "OwnerId:Lookup:Owner:Contact" \
  -e <ENV>

# Replacement schema (extend parent)
clio create-entity-schema --package MyPackage --name UsrAccount --parent Account \
  --extend-parent --title "Extended Account" -e <ENV>

# SystemValue default from friendly caption (normalized to Guid)
clio create-entity-schema --package MyPackage --name UsrVehicle --title "Vehicle" \
  --column '{"name":"UsrStartDate","type":"DateTime","title":"Start date","default-value-config":{"source":"SystemValue","value-source":"Current Time and Date"}}' \
  -e <ENV>

# Settings default from name/code/id (normalized to setting code)
clio create-entity-schema --package MyPackage --name UsrVehicle --title "Vehicle" \
  --column '{"name":"UsrOwner","type":"Lookup","title":"Owner","reference-schema-name":"Contact","default-value-config":{"source":"Settings","value-source":"Maintainer"}}' \
  -e <ENV>
```
Options: `--package` (required), `--name` (required), `--title` (required), `--parent`, `--extend-parent`, `--column` (repeatable, format: `name:type[:title[:refSchema]]` or JSON), `--timeout`
Default resolution:
- `default-value-config.source = SystemValue` accepts Guid, alias, or caption and persists canonical Guid.
- `default-value-config.source = Settings` accepts code, name, or id and persists canonical setting code.
- Ambiguous matches fail with explicit disambiguation guidance.

> **DataForge enrichment** — The MCP `create-entity-schema` and `create-lookup` tools automatically query Data Forge before creating the schema and return an optional `dataforge.context-summary` section with similar tables and lookup hints. Inspect `similar-tables` to confirm no equivalent schema exists before proceeding.

### get-entity-schema-properties
Get a human-readable summary of a remote Creatio entity schema.
```bash
clio get-entity-schema-properties -e dev --package Custom --schema-name UsrVehicle
```
Output includes: package, parent schema, primary columns, column counts, indexes, schema flags, and grouped own/inherited column listings.

### find-entity-schema
Find entity schemas in a Creatio environment without knowing the package name. Returns schema name, package, maintainer, and parent schema for each match. Exactly one of `--schema-name`, `--search-pattern`, or `--uid` is required. Does not require cliogate.
```bash
# Search by substring
clio find-entity-schema -e dev --search-pattern Task

# Exact name lookup
clio find-entity-schema -e dev --schema-name UsrVehicle

# Lookup by UId
clio find-entity-schema -e dev --uid 117d32f9-aab9-4e3a-b13e-cfce62e15e4b
```
Use this command when you need to discover which package owns a schema before calling `get-entity-schema-properties` or `modify-entity-schema-column`.
CLI output is labeled as `Schema: ... | Package: ... | Maintainer: ...` so logs stay unambiguous. When the same capability is consumed through MCP, use the returned `package-name` field directly for follow-up tool calls instead of parsing CLI-style text.

### get-entity-schema-column-properties
Get column properties from a remote Creatio entity schema.
```bash
# Own column
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Name

# Inherited column
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Owner
```
Output includes: own/inherited flag, type, default-value-source, default-value, default-value-config.
`default-value-config` also includes `resolved-value-source` for canonical identifiers (`SystemValue` Guid, `Settings` code).

### modify-entity-schema-column
Add, modify, or remove a column in a remote Creatio entity schema. Requires cliogate.
```bash
# Add column
clio modify-entity-schema-column --package MyPackage --schema-name UsrVehicle \
  --action add --column-name Make --type ShortText --title "Manufacturer" -e <ENV>

# Add lookup column
clio modify-entity-schema-column --package MyPackage --schema-name UsrVehicle \
  --action add --column-name OwnerId --type Lookup --reference-schema Contact \
  --title "Owner" --required -e <ENV>

# Rename column
clio modify-entity-schema-column --package MyPackage --schema-name UsrVehicle \
  --action modify --column-name Make --new-name Brand -e <ENV>

# Remove column
clio modify-entity-schema-column --package MyPackage --schema-name UsrVehicle \
  --action remove --column-name ObsoleteField -e <ENV>
```
Options: `--package` (required), `--schema-name` (required), `--action` add|modify|remove (required), `--column-name` (required), `--new-name`, `--type`, `--title`, `--description`, `--reference-schema`, `--required`, `--indexed`, `--cloneable`, `--track-changes`, `--default-value`, `--default-value-source`, `--masked`, `--timeout`
Default resolution:
- CLI flags `--default-value-source/--default-value` remain shorthand for `Const` and `None`.
- MCP `default-value-config.source = SystemValue` accepts Guid, alias, or caption and persists canonical Guid.
- MCP `default-value-config.source = Settings` accepts code, name, or id and persists canonical setting code.
- Ambiguous matches fail with explicit disambiguation guidance.

### update-entity-schema
Apply batch column operations to a remote Creatio entity schema. Requires cliogate.
```bash
clio update-entity-schema --package MyPackage --schema-name UsrVehicle \
  --operation '{"action":"add","columnName":"Make","type":"ShortText"}' \
  --operation '{"action":"add","columnName":"Year","type":"Integer"}' \
  -e <ENV>

# SystemValue default from friendly caption (normalized to Guid)
clio update-entity-schema --package MyPackage --schema-name UsrVehicle \
  --operation '{"action":"modify","column-name":"UsrStartDate","default-value-config":{"source":"SystemValue","value-source":"Current Time and Date"}}' \
  -e <ENV>

# Settings default from name/code/id (normalized to setting code)
clio update-entity-schema --package MyPackage --schema-name UsrVehicle \
  --operation '{"action":"modify","column-name":"UsrOwner","default-value-config":{"source":"Settings","value-source":"Maintainer"}}' \
  -e <ENV>
```
Options: `--package` (required), `--schema-name` (required), `--operation` JSON (repeatable, required), `--timeout`
Default resolution:
- `default-value-config.source = SystemValue` accepts Guid, alias, or caption and persists canonical Guid.
- `default-value-config.source = Settings` accepts code, name, or id and persists canonical setting code.
- Ambiguous matches fail with explicit disambiguation guidance.

> **DataForge tip** — When adding a reference (Lookup) column and the correct `reference-schema-name` is uncertain, call `dataforge-find-tables` (MCP tool) first to confirm a semantically matching schema exists.

---

## Freedom UI Page Management

### list-pages
List Freedom UI page schemas in a Creatio environment. **Alias:** `page-list`
```bash
clio list-pages -e <ENV>
clio list-pages --search-pattern FormPage --limit 20 -e <ENV>
clio list-pages --package-name UsrApp -e <ENV>
```
Options: `--package-name`, `--search-pattern`, `--limit` (default: 50)

### get-page
Read a Freedom UI page as a merged bundle plus raw schema body. **Alias:** `page-get`
```bash
clio get-page --schema-name UsrTodo_FormPage -e <ENV>
```
Returns a JSON envelope with page metadata, bundle data, and `raw.body`. Use `raw.body` as the editable payload for `update-page`.

### update-page
Update the raw schema body of a Freedom UI page. **Alias:** `page-update`
```bash
# Dry-run validation (no save)
clio update-page --schema-name UsrTodo_FormPage --body "<raw body>" --dry-run true -e <ENV>

# Save updated body
clio update-page --schema-name UsrTodo_FormPage --body "<edited body>" -e <ENV>

# Save with missing resource string registration
clio update-page --schema-name UsrTodo_FormPage --body "<edited body>" \
  --resources '{"UsrDetailsTab_caption":"Details"}' -e <ENV>
```
Options: `--schema-name` (required), `--body` (required), `--dry-run`, `--resources` (JSON object)

### page-sync
Update multiple Freedom UI page schemas in one MCP call. **MCP-only tool** — not available as a standalone CLI command.

Each page is processed independently; failures do not stop remaining pages. Supports client-side validation (`validate: true`, default) and read-back verification (`verify: false`, default).

Input:
```json
{
  "environment-name": "dev",
  "pages": [
    { "schema-name": "UsrTodo_FormPage", "body": "define(...)" },
    { "schema-name": "UsrTodo_ListPage", "body": "define(...)", "resources": "{\"caption\":\"List\"}" }
  ],
  "validate": true,
  "verify": false
}
```

---

## Data Bindings

### create-data-binding
Create or regenerate a package data binding from a runtime schema.
```bash
# SysSettings / SysModule — no environment required (offline template)
clio create-data-binding --package Custom --schema SysSettings

# Non-templated schema — environment required
clio create-data-binding -e dev --package Custom --schema UsrVehicle \
  --values '{"Name":"Initial name"}'

# With localizations
clio create-data-binding -e dev --package Custom --schema SysSettings \
  --values '{"Name":"Setting"}' \
  --localizations '{"ru-RU":{"Name":"Настройка"}}'
```
Options: `--package` (required), `--schema` (required), `--binding-name`, `--workspace-path`, `--install-type` (0-3, default: 0), `--values` (JSON), `--localizations` (JSON)

### create-data-binding-db
Create a DB-first package data binding by saving data directly to the remote Creatio database.
```bash
clio create-data-binding-db -e dev --package Custom --schema SysSettings

clio create-data-binding-db -e dev --package Custom --schema SysSettings \
  --binding-name UsrMyBinding \
  --rows '[{"values":{"Name":"Row","Code":"UsrRow"}}]'
```
Options: `--package` (required), `--schema` (required), `--binding-name`, `--rows` (JSON array)

> **Lookup value resolution** — When a row contains reference (lookup) columns and the correct GUID is not already known, call `dataforge-find-lookups` (MCP tool) with `schema-name` set to the reference schema and a descriptive query term **before** calling this tool. Use the `lookup-id` from the best-matching result as the column value.

### add-data-binding-row
Add or replace a row in an existing package data binding.
```bash
clio add-data-binding-row --package Custom --binding-name SysSettings \
  --values '{"Name":"Setting name"}'

# With lookup column
clio add-data-binding-row --package Custom --binding-name SysModule \
  --values '{"Code":"UsrModule","FolderMode":{"value":"b659d704-...","displayValue":"Folders"}}'
```
Options: `--package` (required), `--binding-name` (required), `--values` (JSON, required), `--workspace-path`, `--localizations` (JSON)

### remove-data-binding-row
Remove a row from a local package data binding by primary key.
```bash
clio remove-data-binding-row --package Custom --binding-name SysSettings \
  --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```
Options: `--package` (required), `--binding-name` (required), `--key-value` (required), `--workspace-path`

### remove-data-binding-row-db
Remove a row from a DB-first package data binding.
```bash
clio remove-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
  --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```
Options: `--package` (required), `--binding-name` (required), `--key-value` (required)

### upsert-data-binding-row-db
Upsert a single row in a DB-first package data binding.
```bash
clio upsert-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
  --values '{"Name":"Updated name","Code":"UsrSetting"}'
```
Options: `--package` (required), `--binding-name` (required), `--values` (JSON, required)

> **Lookup value resolution** — When the row contains reference (lookup) columns and the correct GUID is not known, call `dataforge-find-lookups` (MCP tool) with `schema-name` set to the reference schema before calling this tool. Use the returned `lookup-id` as the column value.

---

## Schema & Process User Task CRUD

### delete-entity-schema
Delete a schema from a workspace package. **Alias:** `delete-schema`. Must be run from a workspace directory.
```bash
clio delete-entity-schema UsrSendInvoice -e <ENV>
```
Only schemas whose package belongs to the current local workspace can be deleted.

### add-user-task
Create a process user task schema in a workspace package. Must be run from a workspace directory.
```bash
# Simple task
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice" -e <ENV>

# With parameters
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice" \
  --parameter "code=IsError;title=Is error;type=Boolean;direction=Out|code=ResultMessage;title=Result message;type=Text;required=true" \
  -e <ENV>

# With lookup parameter
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice" \
  --parameter "code=AccountRef;title=Account reference;type=Lookup;lookup=Account" \
  -e <ENV>
```
Options: `--package` (required), `--title` / `-t` (required), `--description` / `-d`, `--parameter` (repeatable, `|`-separated), `--parameter-item` (repeatable), `--culture` (default: en-US), `--title-localization`, `--description-localization`

### modify-user-task-parameters
Add or remove parameters on an existing workspace user task.
```bash
# Add parameter
clio modify-user-task-parameters UsrSendInvoice \
  --add-parameter "code=IsError;title=Is error;type=Boolean;direction=In" -e <ENV>

# Remove parameter
clio modify-user-task-parameters UsrSendInvoice \
  --remove-parameter "ObsoleteFlag|LegacyResult" -e <ENV>

# Update direction
clio modify-user-task-parameters UsrSendInvoice \
  --set-direction "IsError=Out|ResultMessage=Variable" -e <ENV>
```
Options: `--add-parameter` (`|`-separated), `--add-parameter-item` (`|`-separated), `--remove-parameter` (`|`-separated), `--set-direction` (`|`-separated, format: `name=In|Out|Variable`), `--culture` (default: en-US)

---

## DataForge Orchestration

DataForge is a semantic search and knowledge-graph service embedded in Creatio. clio exposes it through two surfaces:
- **Passive enrichment** — built into write tools (`application-create`, `schema-sync`, `create-entity-schema`, `create-lookup`, `update-entity-schema`). Runs automatically before the mutation, returns a `dataforge` section alongside the result. Never blocks on failure.
- **Active orchestration** — the AI agent calls DataForge tools explicitly, at the right points in a workflow, via the Layer 0–4 protocol below.

Full guidance: `docs://mcp/guides/dataforge-orchestration` (read from the running clio MCP server).

### Protocol layers

**Layer 0 — Health preflight**
Call `dataforge-health` or `dataforge-status` before a long multi-step workflow.
- `health.liveness = true` and `health.readiness = true` → proceed normally.
- `health.data-structure-readiness = false` or `health.lookups-readiness = false` → proceed with caution; discovery may return partial context.
- `status.status != "Ready"` or the call throws → skip all DataForge calls for this session.

**Layer 1 — Planning discovery**
Call `dataforge-context(requirement-summary, candidate-terms, lookup-hints)` once before any write tool when creating new schemas.
- multiple close entries in `similar-tables` with matching names/captions/descriptions → treat as a strong duplicate candidate and surface to the user.
- `similar-lookups[].score >= 0.85` → existing lookup may already cover the concept.
- Failure → skip Layer 1; write tools carry their own enrichment.

**Layer 2 — Read auto-enrichment from write tool responses**
After `application-create`, `schema-sync`, `create-entity-schema`, `create-lookup`, `update-entity-schema` — read the `dataforge` section returned in the response.
Do NOT call DataForge separately before these tools; they already do it.

**Layer 3 — Explicit pre-flight for tools without internal enrichment**
Consistent failure rule: if the caller supplied the value → proceed + warn; if DataForge was the only resolution path → ask the user, do not guess.

| Situation | Call | Decision rule |
|---|---|---|
| Adding Lookup column via `modify-entity-schema-column` with uncertain `reference-schema-name` | `dataforge-find-tables(query)` | use name/caption/description similarity as a manual confirmation step; if still ambiguous, confirm with the user |
| Writing rows with unknown lookup GUID via `create-data-binding-db` / `upsert-data-binding-row-db` | `dataforge-find-lookups(schema-name, query)` | use the best match when `score >= 0.70`; otherwise ask the user |
| Cross-entity FK design before multi-entity `schema-sync` | `dataforge-get-relations(source, target)` | any result helps; on failure, design the FK independently |
| Runtime column inspection outside local package | `dataforge-get-table-columns(table-name)` | on failure, fall back to `get-entity-schema-properties` |

> **Note on `modify-entity-schema-column`**: this tool permanently requires Layer 3 pre-flight for Lookup adds. It will never receive internal enrichment (single-column targeted tool; no batch semantics). Future change guard: only reconsider if the tool gains multi-column batch semantics.

> **Note on `update-entity-schema`**: this tool now includes internal DataForge enrichment (same pattern as `schema-sync`). Use Layer 2 (read the `dataforge` response section) instead of a Layer 3 pre-flight for Lookup column adds.

**Layer 4 — Index maintenance and stale index recovery**
After bulk schema creation (5+ new entities): call `dataforge-update`.
Staleness detection: `coverage.tables = false` or empty `similar-tables` for just-created schemas → call `dataforge-update`.
Recovery when `dataforge-update` fails:
1. Retry after 30 seconds.
2. Check `dataforge-status`.
3. Fall back to `dataforge-initialize` (full reindex).
4. If all fail → warn user, proceed without DataForge this session.

### MCP tools summary

| Tool | Layer | Read-only | Purpose |
|---|---|---|---|
| `dataforge-health` | 0 | yes | Direct service health endpoints |
| `dataforge-status` | 0 | yes | Health + Creatio maintenance status |
| `dataforge-context` | 1 | yes | Aggregated planning discovery |
| `dataforge-find-tables` | 3 | yes | Find semantically similar schemas |
| `dataforge-find-lookups` | 3 | yes | Find lookup values by schema + query |
| `dataforge-get-relations` | 3 | yes | Cypher relation paths between tables |
| `dataforge-get-table-columns` | 3 | yes | Runtime column list for a table |
| `dataforge-initialize` | 4 | no | Full DataForge reindex |
| `dataforge-update` | 4 | no | Incremental DataForge index refresh |
