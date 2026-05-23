# CLI Option Naming Violations (camelCase / PascalCase → kebab-case)

Rule: all `[Option("...", ...)]` long names must use kebab-case.  
Pattern for fix: rename main `[Option]`, add hidden alias property for backward compat.

---

## CommandLineOptions.cs

| Current | Should be | Line |
|---|---|---|
| `--Password` | `--password` | 19 |
| `--Login` | `--login` | 28 |
| `--Environment` | `--environment` | 40 |
| `--Maintainer` | `--maintainer` | 43 |
| `--WorkspacePathes` | `--workspace-pathes` | 49 |
| `--Safe` | `--safe` | 70 |
| `--clientId` | `--client-id` | 73 |
| `--clientSecret` | `--client-secret` | 76 |
| `--authAppUri` | `--auth-app-uri` | 79 |
| `--Path` (WorkspaceCommandOptions) | `--path` | 211 |
| `--ConvertSourceCode` | `--convert-source-code` | 219 |

## AddItemCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationPath` | `--destination-path` | 32 |
| `--Namespace` | `--namespace` | 35 |
| `--Fields` | `--fields` | 38 |
| `--All` | `--all` | 41 |
| `--Culture` | `--culture` | 44 |

## AddPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--asApp` | `--as-app` | 26 |

## AssemblyCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--ExecutorType` | `--executor-type` | 17 |
| `--WriteResponse` | `--write-response` | 20 |

## CheckNugetUpdateCommand.cs / InstallNugetPackageCommand.cs / PushNuGetPackagesCommand.cs / RestoreNugetPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--Source` | `--source` | multiple |
| `--ApiKey` | `--api-key` | PushNuGet:16 |

## CompressAppCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--SourcePath` | `--source-path` | 15 |
| `--Packages` | `--packages` | 18 |
| `--DestinationPath` | `--destination-path` | 21 |
| `--SkipPdb` | `--skip-pdb` | 24 |

## CompressPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationPath` | `--destination-path` | 14 |
| `--Packages` | `--packages` | 17 |
| `--SkipPdb` | `--skip-pdb` | 20 |

## CompileWorspaceCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--ModifiedItems` | `--modified-items` | 11 |

## ConfigureWorkspaceCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--Packages` | `--packages` | 21 |

## CreatioInstallCommand/InstallerCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--SiteName` | `--site-name` | 110 |
| `--SitePort` | `--site-port` | 116 |
| `--ZipFile` | `--zip-file` | 122 |

## DeployAppCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationEnvironment` | `--destination-environment` | 25 |

## DownloadAppCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--FilePath` | `--file-path` | 16 |

## FeatureCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--SysAdminUnitName` | `--sys-admin-unit-name` | 26 |
| `--UseFeatureWebService` | `--use-feature-web-service` | 29 |

## GenerateProcessModelCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationPath` | `--destination-path` | 25 |
| `--Namespace` | `--namespace` | 34 |
| `--Culture` | `--culture` | 42 |

## GetPkgListCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--Filter` | `--filter` | 19 |
| `--Json` | `--json` | 23 |

## GitSync.cs

| Current | Should be | Line |
|---|---|---|
| `--Direction` | `--direction` | 12 |

## HealthCheckCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--WebHost` | `--web-host` | 12 |
| `--WebApp` | `--web-app` | 15 |

## InstallApplicationOptions.cs

| Current | Should be | Line |
|---|---|---|
| `--ReportPath` | `--report-path` | 22 |

## Link2RepoCommand.cs / Link4RepoCommand.cs / LinkPackageStoreCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--repoPath` | `--repo-path` | multiple |
| `--envPkgPath` | `--env-pkg-path` | multiple |
| `--packageStorePath` | `--package-store-path` | multiple |

## ListenCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--logPattern` | `--log-pattern` | 22 |
| `--FileName` | `--file-name` | 25 |
| `--Silent` | `--silent` | 28 |

## NewPkgCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--References` | `--references` | 16 |

## PackNuGetPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--SkipPdb` | `--skip-pdb` | 26 |
| `--Dependencies` | `--dependencies` | 29 |
| `--NupkgDirectory` | `--nupkg-directory` | 32 |

## PackageCommand/DownloadPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationPath` | `--destination-path` | 13 |
| `--UnZip` | `--unzip` | 19 |
| `--Async` | `--async` | 25 |

## PackageCommand/ExtractPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationPath` | `--destination-path` | 15 |

## PackageCommand/SetPackageVersionCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--PackageVersion` | `--package-version` | 19 |

## PackageCommand/ValidationPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationResult` | `--destination-result` | 17 |

## PingCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--Endpoint` | `--endpoint` | 10 |

## PushPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--InstallSqlScript` | `--install-sql-script` | 18 |
| `--InstallPackageData` | `--install-package-data` | 21 |
| `--ContinueIfError` | `--continue-if-error` | 24 |
| `--SkipConstraints` | `--skip-constraints` | 27 |
| `--SkipValidateActions` | `--skip-validate-actions` | 30 |
| `--ExecuteValidateActions` | `--execute-validate-actions` | 33 |
| `--IsForceUpdateAllColumns` | `--is-force-update-all-columns` | 36 |

## ReferenceCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--ReferencePattern` | `--reference-pattern` | 14 |
| `--Path` | `--path` | 18 |

## RegAppCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--ActiveEnvironment` | `--active-environment` | 19 |
| `--checkLogin` | `--check-login` | 22 |

## RegisterCommand.cs / UnregisterCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--Path` | `--path` | 18 / 15 |

## RestoreDb.cs

| Current | Should be | Line |
|---|---|---|
| `--dbName` | `--db-name` | 27 |
| `--backupPath` | `--backup-path` | 30 |
| `--dbServerName` | `--db-server-name` | 33 |

## RestoreNugetPackageCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--DestinationDirectory` | `--destination-directory` | 14 |

## RestoreWorkspaceCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--IsNugetRestore` | `--is-nuget-restore` | 20 |
| `--IsCreateSolution` | `--is-create-solution` | 24 |
| `--AppCode` | `--app-code` | 27 |
| `--AddBuildProps` | `--add-build-props` | 30 |

## SetFsmConfigCommand.cs / UninstallCreatio.cs

| Current | Should be | Line |
|---|---|---|
| `--physicalPath` | `--physical-path` | multiple |

## SqlScriptCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--File` | `--file` | 20 |
| `--DestinationPath` | `--destination-path` | 27 |

## SysSettingsCommand.cs

| Current | Should be | Line |
|---|---|---|
| `--GET` | `--get` | 19 |

---

## Already fixed (this session)
- `--restartEnvironment` → `--restart-environment` (CommandLineOptions.cs:100) — alias kept hidden, author: Vladimir, commit bf625d2e
