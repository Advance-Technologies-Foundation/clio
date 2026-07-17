# Clio Command Reference

Use `clio help` for the terminal overview and `clio <command> --help` for command details.

## Application Management

<a id="clear-local-env"></a>
<a id="clear-env"></a>
- [`clear-local-env`](docs/commands/clear-local-env.md) - Clear deleted local environments, `clear-env`
<a id="clone-env"></a>
<a id="clone"></a>
<a id="clone-environment"></a>
- [`clone-env`](docs/commands/clone-env.md) - Clone one environment to another, `clone`, `clone-environment`
<a id="deploy-application"></a>
<a id="deploy-app"></a>
- [`deploy-application`](docs/commands/deploy-application.md) - Copy an application package between Creatio environments, `deploy-app`
<a id="download-application"></a>
<a id="dapp"></a>
<a id="download-app"></a>
- [`download-application`](docs/commands/download-application.md) - Download an application package from Creatio, `dapp`, `download-app`
<a id="get-app-hash"></a>
- [`get-app-hash`](docs/commands/get-app-hash.md) - Calculate the hash of an application package
<a id="create-app"></a>
- [`create-app`](docs/commands/create-app.md) - Create a new application in Creatio
<a id="get-app-info"></a>
- [`get-app-info`](docs/commands/get-app-info.md) - Get information about an installed Creatio application, including each entity's virtual flag
<a id="list-apps"></a>
<a id="get-app-list"></a>
<a id="app-list"></a>
<a id="apps"></a>
<a id="apps-list"></a>
<a id="lia"></a>
- [`list-apps`](docs/commands/list-apps.md) - List installed applications, `get-app-list`, `app-list`, `apps`, `apps-list`, `lia`
<a id="find-app"></a>
- [`find-app`](docs/commands/find-app.md) - Find installed applications and their sections by name, code, or pattern
<a id="create-app-section"></a>
- [`create-app-section`](docs/commands/create-app-section.md) - Create a section inside an existing installed application
<a id="update-app-section"></a>
- [`update-app-section`](docs/commands/update-app-section.md) - Update metadata of a section inside an existing installed application
<a id="delete-app-section"></a>
- [`delete-app-section`](docs/commands/delete-app-section.md) - Delete a section and its artifacts from an existing installed application
<a id="list-app-sections"></a>
- [`list-app-sections`](docs/commands/list-app-sections.md) - List sections of an existing installed application
<a id="idp-list"></a>
- [`idp-list`](docs/commands/idp-list.md) - List identity providers
<a id="idp-upsert"></a>
- [`idp-upsert`](docs/commands/idp-upsert.md) - Create or update an identity provider
<a id="idp-set-secret"></a>
- [`idp-set-secret`](docs/commands/idp-set-secret.md) - Set identity provider client secret
<a id="idp-delete"></a>
- [`idp-delete`](docs/commands/idp-delete.md) - Delete an identity provider
<a id="idp-set-default"></a>
- [`idp-set-default`](docs/commands/idp-set-default.md) - Set the default identity provider
<a id="idp-bind"></a>
- [`idp-bind`](docs/commands/idp-bind.md) - Bind an identity provider to an external service code
<a id="idp-unbind"></a>
- [`idp-unbind`](docs/commands/idp-unbind.md) - Unbind an identity provider from an external service code
<a id="idp-services"></a>
- [`idp-services`](docs/commands/idp-services.md) - List external services and identity provider bindings
<a id="get-info"></a>
<a id="describe"></a>
<a id="describe-creatio"></a>
<a id="instance-info"></a>
- [`get-info`](docs/commands/get-info.md) - Validate and show system information for a Creatio instance with actionable target/auth/response errors, `describe`, `describe-creatio`, `instance-info`
<a id="get-webservice-url"></a>
<a id="gwu"></a>
- [`get-webservice-url`](docs/commands/get-webservice-url.md) - Show the configured base URL for a web service, `gwu`
<a id="install-application"></a>
<a id="install-app"></a>
<a id="push-app"></a>
- [`install-application`](docs/commands/install-application.md) - Install an application package into Creatio, `install-app`, `push-app`
<a id="open-web-app"></a>
<a id="open"></a>
- [`open-web-app`](docs/commands/open-web-app.md) - Open a registered Creatio environment in the browser (use `--authenticated` to land already signed in, no login form), `open`
<a id="get-browser-session"></a>
<a id="get-session"></a>
- [`get-browser-session`](docs/commands/get-browser-session.md) - Obtain an authenticated Creatio browser session (Playwright storageState), `get-session`
<a id="clear-browser-session"></a>
<a id="clear-session"></a>
- [`clear-browser-session`](docs/commands/clear-browser-session.md) - Delete the cached Creatio browser session, `clear-session`
<a id="ping-app"></a>
<a id="ping"></a>
- [`ping-app`](docs/commands/ping-app.md) - Verify connectivity to a Creatio environment, `ping`
<a id="reg-web-app"></a>
<a id="cfg"></a>
<a id="reg"></a>
- [`reg-web-app`](docs/commands/reg-web-app.md) - Register a Creatio environment, `cfg`, `reg`
<a id="set-dev-mode"></a>
<a id="dev"></a>
<a id="unlock"></a>
- [`set-dev-mode`](docs/commands/set-dev-mode.md) - Toggle developer mode for a Creatio environment, `dev`, `unlock`
<a id="set-feature"></a>
<a id="feature"></a>
- [`set-feature`](docs/commands/set-feature.md) - Set feature state, `feature`
<a id="set-file-content-storage-connection-string"></a>
<a id="set-fcs-connection-string"></a>
- [`set-file-content-storage-connection-string`](docs/commands/set-file-content-storage-connection-string.md) - Set the connection string of a file content storage, `set-fcs-connection-string`
<a id="set-syssetting"></a>
<a id="get-syssetting"></a>
<a id="ss"></a>
<a id="sys-setting"></a>
<a id="syssetting"></a>
- [`set-syssetting`](docs/commands/set-syssetting.md) - Get or set a system setting value (including blob-data settings such as the logo — pass a file path for Type Binary), `get-syssetting`, `ss`, `sys-setting`, `syssetting`
<a id="set-webservice-url"></a>
<a id="swu"></a>
<a id="webservice"></a>
- [`set-webservice-url`](docs/commands/set-webservice-url.md) - Set a base URL for a registered web service, `swu`, `webservice`
<a id="show-diff"></a>
<a id="compare"></a>
<a id="diff"></a>
- [`show-diff`](docs/commands/show-diff.md) - Compare settings between two Creatio environments, `compare`, `diff`
<a id="show-local-envs"></a>
<a id="localenvs"></a>
- [`show-local-envs`](docs/commands/show-local-envs.md) - Show local environments with filesystem and auth status, `localenvs`
<a id="show-web-app-list"></a>
<a id="list-environments"></a>
<a id="env"></a>
<a id="envs"></a>
<a id="show-web-app"></a>
- [`list-environments`](docs/commands/list-environments.md) - List registered Creatio environments, `show-web-app-list`, `env`, `envs`, `show-web-app`
<a id="uninstall-app-remote"></a>
<a id="uninstall"></a>
- [`uninstall-app-remote`](docs/commands/uninstall-app-remote.md) - Uninstall an application package from Creatio, `uninstall`
<a id="unreg-web-app"></a>
<a id="unreg"></a>
- [`unreg-web-app`](docs/commands/unreg-web-app.md) - Remove a registered Creatio environment, `unreg`

## Package Management

<a id="add-package-dependency"></a>
<a id="add-pkg-dependency"></a>
<a id="add-pkg-dep"></a>
- [`add-package-dependency`](docs/commands/add-package-dependency.md) - Add one or more package dependencies to a package, `add-pkg-dependency`, `add-pkg-dep`
<a id="remove-package-dependency"></a>
<a id="remove-pkg-dependency"></a>
<a id="remove-pkg-dep"></a>
- [`remove-package-dependency`](docs/commands/remove-package-dependency.md) - Remove one or more package dependencies from a package, `remove-pkg-dependency`, `remove-pkg-dep`
<a id="activate-pkg"></a>
<a id="activate-package"></a>
<a id="apkg"></a>
<a id="enable-package"></a>
- [`activate-pkg`](docs/commands/activate-pkg.md) - Activate a package in Creatio, `activate-package`, `apkg`, `enable-package`
<a id="add-package"></a>
<a id="ap"></a>
- [`add-package`](docs/commands/add-package.md) - Add package to workspace or local folder, `ap`
<a id="check-nuget-update"></a>
<a id="check"></a>
- [`check-nuget-update`](docs/commands/check-nuget-update.md) - Check NuGet for Creatio package updates, `check`
<a id="compile-configuration"></a>
<a id="cc"></a>
<a id="compile-remote"></a>
- [`compile-configuration`](docs/commands/compile-configuration.md) - Compile the full configuration in Creatio, `cc`, `compile-remote`
<a id="compile-package"></a>
<a id="comp-pkg"></a>
- [`compile-package`](docs/commands/compile-package.md) - Compile one or more packages in Creatio, `comp-pkg`
<a id="compressApp"></a>
<a id="comp-app"></a>
- [`compressApp`](docs/commands/compressApp.md) - Archive an application directory into ZIP, `comp-app`
<a id="deactivate-pkg"></a>
<a id="deactivate-package"></a>
<a id="disable-package"></a>
<a id="dpkg"></a>
- [`deactivate-pkg`](docs/commands/deactivate-pkg.md) - Deactivate a package in Creatio, `deactivate-package`, `disable-package`, `dpkg`
<a id="delete-pkg-remote"></a>
<a id="delete"></a>
- [`delete-pkg-remote`](docs/commands/delete-pkg-remote.md) - Delete a package from Creatio, `delete`
<a id="extract-pkg-zip"></a>
<a id="extract"></a>
<a id="unzip"></a>
<a id="extract-package"></a>
- [`extract-pkg-zip`](docs/commands/extract-pkg-zip.md) - Extract a packaged application or package archive, `extract`, `unzip`
<a id="generate-pkg-zip"></a>
<a id="comp-pkg"></a>
<a id="compress"></a>
- [`generate-pkg-zip`](docs/commands/generate-pkg-zip.md) - Prepare an archive of creatio package, `comp-pkg`, `compress`
<a id="generate-source-code"></a>
<a id="gsc"></a>
- [`generate-source-code`](docs/commands/generate-source-code.md) - Generate source code for schemas in Creatio, `gsc`
<a id="get-pkg-list"></a>
<a id="list-packages"></a>
<a id="packages"></a>
- [`list-packages`](docs/commands/list-packages.md) - List packages in a Creatio environment, `get-pkg-list`, `packages`
<a id="get-pkg-version"></a>
<a id="gpv"></a>
- [`get-pkg-version`](docs/commands/get-pkg-version.md) - Get package version, `gpv`
<a id="install-nuget-pkg"></a>
<a id="installng"></a>
- [`install-nuget-pkg`](docs/commands/install-nuget-pkg.md) - Install NuGet package to a web application (website), `installng`
<a id="lock-package"></a>
<a id="lp"></a>
- [`lock-package`](docs/commands/lock-package.md) - Lock a package in Creatio, `lp`
<a id="new-pkg"></a>
<a id="init"></a>
- [`new-pkg`](docs/commands/new-pkg.md) - Create a new package project, `init`
<a id="pack-nuget-pkg"></a>
<a id="pack"></a>
- [`pack-nuget-pkg`](docs/commands/pack-nuget-pkg.md) - Pack a package into a NuGet artifact, `pack`
<a id="pkg-hotfix"></a>
<a id="hf"></a>
<a id="hotfix"></a>
- [`pkg-hotfix`](docs/commands/pkg-hotfix.md) - Enable/disable hotfix state for package, `hf`, `hotfix`
<a id="pull-pkg"></a>
<a id="download"></a>
- [`pull-pkg`](docs/commands/pull-pkg.md) - Download package from a web application, `download`
<a id="push-nuget-pkg"></a>
<a id="push-n"></a>
<a id="push-nuget"></a>
- [`push-nuget-pkg`](docs/commands/push-nuget-pkg.md) - Push a NuGet package to a feed, `push-n`, `push-nuget`
<a id="push-pkg"></a>
<a id="install"></a>
<a id="push"></a>
- [`push-pkg`](docs/commands/push-pkg.md) - Install a package into Creatio, `install`, `push`
<a id="restore-configuration"></a>
<a id="rc"></a>
<a id="restore"></a>
- [`restore-configuration`](docs/commands/restore-configuration.md) - Restore the configuration from the last backup, `rc`, `restore`
<a id="restore-nuget-pkg"></a>
<a id="restore-nuget"></a>
<a id="rn"></a>
- [`restore-nuget-pkg`](docs/commands/restore-nuget-pkg.md) - Restore NuGet package to a folder, `restore-nuget`, `rn`
<a id="set-pkg-version"></a>
<a id="spv"></a>
- [`set-pkg-version`](docs/commands/set-pkg-version.md) - Set package version, `spv`
<a id="unlock-package"></a>
<a id="up"></a>
- [`unlock-package`](docs/commands/unlock-package.md) - Unlock a package in Creatio, `up`
<a id="autoupdate"></a>
- [`autoupdate`](docs/commands/autoupdate.md) - Enable or disable automatic updates on startup
<a id="experimental"></a>
<a id="exp"></a>
- [`experimental`](docs/commands/experimental.md) - List and toggle clio experimental feature flags, `exp`

<a id="update-cli"></a>
<a id="update"></a>
- [`update-cli`](docs/commands/update-cli.md) - Update clio, `update`

## Workspace

<a id="add-data-binding-row"></a>
- [`add-data-binding-row`](docs/commands/add-data-binding-row.md) - Add or replace a row in a package data binding
<a id="build-workspace"></a>
<a id="build"></a>
<a id="compile"></a>
<a id="compile-all"></a>
<a id="rebuild"></a>
- [`build-workspace`](docs/commands/build-workspace.md) - Build the current workspace in Creatio, `build`, `compile`, `compile-all`, `rebuild`
<a id="cfg-worspace"></a>
<a id="cfgw"></a>
<a id="configure-workspace"></a>
- [`cfg-worspace`](docs/commands/cfg-worspace.md) - Configure workspace package selection, `cfgw`
<a id="create-data-binding"></a>
- [`create-data-binding`](docs/commands/create-data-binding.md) - Create or regenerate a package data binding
<a id="create-data-binding-db"></a>
- [`create-data-binding-db`](docs/commands/create-data-binding-db.md) - Create a DB-first package data binding by saving data directly to the remote Creatio database
<a id="create-workspace"></a>
<a id="createw"></a>
- [`create-workspace`](docs/commands/create-workspace.md) - Create a local workspace, `createw`
<a id="link-core-src"></a>
<a id="lcs"></a>
- [`link-core-src`](docs/commands/link-core-src.md) - Link core source code to environment for development, `lcs`
<a id="link-from-repository"></a>
<a id="l4r"></a>
<a id="link4repo"></a>
- [`link-from-repository`](docs/commands/link-from-repository.md) - Link repository package(s) to environment, `l4r`, `link4repo`
<a id="link-to-repository"></a>
<a id="l2r"></a>
<a id="link2repo"></a>
- [`link-to-repository`](docs/commands/link-to-repository.md) - Link environment package(s) to repository, `l2r`, `link2repo`
<a id="merge-workspaces"></a>
<a id="mergew"></a>
- [`merge-workspaces`](docs/commands/merge-workspaces.md) - Merge packages from multiple workspaces and install them to the environment, `mergew`
<a id="pkg-to-db"></a>
<a id="2db"></a>
<a id="todb"></a>
- [`pkg-to-db`](docs/commands/pkg-to-db.md) - Load packages into Creatio database storage, `2db`, `todb`
<a id="pkg-to-file-system"></a>
<a id="2fs"></a>
<a id="tofs"></a>
- [`pkg-to-file-system`](docs/commands/pkg-to-file-system.md) - Load packages into Creatio file system storage, `2fs`, `tofs`
<a id="publish-app"></a>
<a id="ph"></a>
<a id="publish-hub"></a>
<a id="publish-workspace"></a>
<a id="publishw"></a>
- [`publish-app`](docs/commands/publish-app.md) - Publish a workspace to a ZIP archive or hub folder, `ph`, `publish-hub`, `publish-workspace`, `publishw`
<a id="push-workspace"></a>
<a id="pushw"></a>
- [`push-workspace`](docs/commands/push-workspace.md) - Push workspace to selected environment, `pushw`
<a id="remove-data-binding-row"></a>
- [`remove-data-binding-row`](docs/commands/remove-data-binding-row.md) - Remove a row from a package data binding
<a id="remove-data-binding-row-db"></a>
- [`remove-data-binding-row-db`](docs/commands/remove-data-binding-row-db.md) - Remove a row from a DB-first package data binding
<a id="restore-workspace"></a>
<a id="pull-workspace"></a>
<a id="pullw"></a>
<a id="restorew"></a>
- [`restore-workspace`](docs/commands/restore-workspace.md) - Restore editable packages into a workspace, `pull-workspace`, `pullw`, `restorew`
<a id="switch-nuget-to-dll-reference"></a>
<a id="nuget2dll"></a>
- [`switch-nuget-to-dll-reference`](docs/commands/switch-nuget-to-dll-reference.md) - Switches nuget references to dll references in csproj files, `nuget2dll`
<a id="distribute-license"></a>
<a id="grant-license"></a>
- [`distribute-license`](docs/commands/distribute-license.md) - Add or remove users from a Creatio license package, `grant-license`
<a id="upload-licenses"></a>
<a id="lic"></a>
- [`upload-licenses`](docs/commands/upload-licenses.md) - Upload license files to Creatio, `lic`
<a id="upsert-data-binding-row-db"></a>
- [`upsert-data-binding-row-db`](docs/commands/upsert-data-binding-row-db.md) - Upsert a row in a DB-first package data binding

## Development

<a id="add-item"></a>
<a id="create"></a>
- [`add-item`](docs/commands/add-item.md) - Generate package item models from Creatio metadata, `create`
<a id="add-schema"></a>
- [`add-schema`](docs/commands/add-schema.md) - Create a schema file in a workspace package
<a id="add-user-task"></a>
- [`add-user-task`](docs/commands/add-user-task.md) - Create a user task schema in a workspace package
<a id="alm-deploy"></a>
<a id="deploy"></a>
- [`alm-deploy`](docs/commands/alm-deploy.md) - Deploy a package to Creatio, `deploy`
<a id="apply-manifest"></a>
<a id="apply-environment-manifest"></a>
<a id="applym"></a>
- [`apply-manifest`](docs/commands/apply-manifest.md) - Apply an environment manifest, `apply-environment-manifest`, `applym`
<a id="call-service"></a>
<a id="cs"></a>
<a id="callservice"></a>
- [`call-service`](docs/commands/call-service.md) - Call a Creatio service endpoint, `cs`
<a id="create-client-unit-schema"></a>
<a id="client-unit-schema-create"></a>
- [`create-client-unit-schema`](docs/commands/create-client-unit-schema.md) - Create a new JavaScript (ClientUnit) schema on a remote Creatio environment, `client-unit-schema-create`
<a id="create-entity-schema"></a>
- [`create-entity-schema`](docs/commands/create-entity-schema.md) - Create a persistent or virtual entity schema in a remote Creatio package
<a id="create-lookup"></a>
- [`create-lookup`](docs/commands/create-lookup.md) - Create a lookup entity schema in a remote Creatio package
<a id="create-schema"></a>
<a id="schema-create"></a>
- [`create-schema`](docs/commands/create-schema.md) - Create a new C# source-code schema on a remote Creatio environment, `schema-create`
<a id="create-sql-schema"></a>
<a id="sql-schema-create"></a>
- [`create-sql-schema`](docs/commands/create-sql-schema.md) - Create a new SQL script schema on a remote Creatio environment, `sql-schema-create`
<a id="dataservice"></a>
<a id="ds"></a>
- [`dataservice`](docs/commands/dataservice.md) - Send a Creatio DataService request, `ds`
<a id="delete-schema"></a>
- [`delete-schema`](docs/commands/delete-schema.md) - Delete a schema from a workspace package
<a id="download-configuration"></a>
<a id="dconf"></a>
- [`download-configuration`](docs/commands/download-configuration.md) - Download configuration libraries from Creatio, `dconf`
<a id="execute-sql-script"></a>
<a id="sql"></a>
- [`execute-sql-script`](docs/commands/execute-sql-script.md) - Execute a SQL script in Creatio, `sql`
<a id="externalLink"></a>
<a id="link"></a>
- [`externalLink`](docs/commands/externalLink.md) - Handle external deep links, `link`
<a id="generate-process-model"></a>
<a id="gpm"></a>
- [`generate-process-model`](docs/commands/generate-process-model.md) - Generate process model for ATF.Repository, `gpm`
<a id="get-client-unit-schema"></a>
<a id="client-unit-schema-get"></a>
- [`get-client-unit-schema`](docs/commands/get-client-unit-schema.md) - Read body and metadata of a client unit (JavaScript) schema on a remote Creatio environment, `client-unit-schema-get`
<a id="get-entity-schema-column-properties"></a>
- [`get-entity-schema-column-properties`](docs/commands/get-entity-schema-column-properties.md) - Get column properties from a remote Creatio entity schema
<a id="find-entity-schema"></a>
- [`find-entity-schema`](docs/commands/find-entity-schema.md) - Find entity schemas in a Creatio environment by name, pattern, or UId
<a id="get-entity-schema-properties"></a>
- [`get-entity-schema-properties`](docs/commands/get-entity-schema-properties.md) - Get properties from a remote Creatio entity schema (omit `--package` for the merged all-packages view, including custom columns from other packages)
<a id="set-entity-schema-properties"></a>
- [`set-entity-schema-properties`](docs/commands/set-entity-schema-properties.md) - Set schema-level properties (e.g. the primary-display column) on a remote Creatio entity schema
<a id="get-process-signature"></a>
<a id="gps"></a>
- [`get-process-signature`](docs/commands/get-process-signature.md) - Read the parameter signature (codes, types, direction) of a Creatio business process, `gps`
<a id="get-schema"></a>
<a id="schema-get"></a>
- [`get-schema`](docs/commands/get-schema.md) - Read body and metadata of a C# source-code schema on a remote Creatio environment, `schema-get`
<a id="get-sql-schema"></a>
<a id="sql-schema-get"></a>
- [`get-sql-schema`](docs/commands/get-sql-schema.md) - Read body and metadata of a SQL script schema on a remote Creatio environment, `sql-schema-get`
<a id="git-sync"></a>
<a id="sync"></a>
- [`git-sync`](docs/commands/git-sync.md) - Syncs environment with Git repository, `sync`
<a id="info"></a>
<a id="get-version"></a>
<a id="i"></a>
<a id="ver"></a>
- [`info`](docs/commands/info.md) - Show clio, cliogate, and .NET runtime versions, `get-version`, `i`, `ver`
<a id="install-sql-schema"></a>
<a id="sql-schema-install"></a>
<a id="execute-sql-schema"></a>
- [`install-sql-schema`](docs/commands/install-sql-schema.md) - Execute a SQL script schema on a remote Creatio environment (irreversible raw SQL), `sql-schema-install`, `execute-sql-schema`
<a id="listen"></a>
- [`listen`](docs/commands/listen.md) - Stream Creatio log events over WebSocket
<a id="mock-data"></a>
<a id="data-mock"></a>
- [`mock-data`](docs/commands/mock-data.md) - Generate mock data for unit tests, `data-mock`
<a id="modify-entity-schema-column"></a>
- [`modify-entity-schema-column`](docs/commands/modify-entity-schema-column.md) - Add, modify, or remove a column in a remote Creatio entity schema
<a id="modify-user-task-parameters"></a>
- [`modify-user-task-parameters`](docs/commands/modify-user-task-parameters.md) - Add or remove parameters in a user task schema
<a id="new-test-project"></a>
<a id="create-test-project"></a>
<a id="unit-test"></a>
- [`new-test-project`](docs/commands/new-test-project.md) - Create a new test project, `create-test-project`, `unit-test`

<a id="new-integration-test-project"></a>
<a id="integration-test"></a>
- [`new-integration-test-project`](docs/commands/new-integration-test-project.md) - Create a portable Creatio integration-test project, `integration-test`
<a id="new-ui-project"></a>
<a id="create-ui-project"></a>
<a id="createup"></a>
<a id="new-ui"></a>
<a id="ui"></a>
<a id="uiproject"></a>
- [`new-ui-project`](docs/commands/new-ui-project.md) - Create a new Freedom UI project, `create-ui-project`, `createup`, `new-ui`, `ui`, `uiproject`
<a id="open-settings"></a>
<a id="conf"></a>
<a id="configuration"></a>
<a id="os"></a>
<a id="settings"></a>
- [`open-settings`](docs/commands/open-settings.md) - Open the clio settings file, `conf`, `configuration`, `os`, `settings`
<a id="get-page"></a>
<a id="page-get"></a>
- [`get-page`](docs/commands/get-page.md) - Get a Freedom UI page bundle and raw schema body, `page-get`
<a id="list-pages"></a>
<a id="page-list"></a>
- [`list-pages`](docs/commands/list-pages.md) - List Freedom UI pages, `page-list`
<a id="update-client-unit-schema"></a>
<a id="client-unit-schema-update"></a>
- [`update-client-unit-schema`](docs/commands/update-client-unit-schema.md) - Update the raw body of a client unit schema, `client-unit-schema-update`
<a id="update-page"></a>
<a id="page-update"></a>
- [`update-page`](docs/commands/update-page.md) - Update Freedom UI page schema body with best-effort Designer Presence save push, `page-update`
<a id="create-page"></a>
<a id="page-create"></a>
- [`create-page`](docs/commands/create-page.md) - Create a new Freedom UI page from a supported template, `page-create`
<a id="list-page-templates"></a>
<a id="page-templates"></a>
<a id="page-templates-list"></a>
- [`list-page-templates`](docs/commands/list-page-templates.md) - List Freedom UI page templates available for create-page, `page-templates`, `page-templates-list`
<a id="create-related-page-addon"></a>
<a id="related-page-addon"></a>
<a id="set-related-pages"></a>
- [`create-related-page-addon`](docs/commands/create-related-page-addon.md) - Bind Freedom UI pages to an object (default/add page, optionally per role and type) via the RelatedPage add-on, `related-page-addon`, `set-related-pages`
<a id="get-related-page-addon"></a>
<a id="related-page-addon-get"></a>
<a id="get-related-pages"></a>
- [`get-related-page-addon`](docs/commands/get-related-page-addon.md) - Read an object's current RelatedPage configuration (bound default/add pages per role and type), `related-page-addon-get`, `get-related-pages`
<a id="run"></a>
<a id="run-scenario"></a>
<a id="scenario"></a>
- [`run`](docs/commands/run.md) - Run scenario, `run-scenario`, `scenario`
<a id="save-state"></a>
<a id="save-manifest"></a>
<a id="state"></a>
<a id="create-manifest"></a>
- [`save-state`](docs/commands/save-state.md) - Save state of Creatio instance to file, `save-manifest`, `state`
<a id="show-package-file-content"></a>
<a id="files"></a>
<a id="show-files"></a>
- [`show-package-file-content`](docs/commands/show-package-file-content.md) - Show files that belong to a package, `files`, `show-files`
<a id="update-entity-schema"></a>
- [`update-entity-schema`](docs/commands/update-entity-schema.md) - Apply batch column operations to a remote Creatio entity schema
<a id="update-schema"></a>
<a id="schema-update"></a>
- [`update-schema`](docs/commands/update-schema.md) - Update the body of a C# source-code schema on a remote Creatio environment, `schema-update`
<a id="update-sql-schema"></a>
<a id="sql-schema-update"></a>
- [`update-sql-schema`](docs/commands/update-sql-schema.md) - Update the body of a SQL script schema on a remote Creatio environment, `sql-schema-update`

## Deployment & Infrastructure

<a id="build-docker-image"></a>
- [`build-docker-image`](docs/commands/build-docker-image.md) - Build one or more Docker images for a Creatio .NET 8+ distribution or bundled database backup
<a id="check-windows-features"></a>
<a id="cf"></a>
<a id="checkf"></a>
<a id="checks"></a>
<a id="checkw"></a>
- [`check-windows-features`](docs/commands/check-windows-features.md) - Check windows for the required components, `cf`, `checkf`, `checks`, `checkw`
<a id="compare-web-farm-node"></a>
<a id="check-farm"></a>
<a id="check-web-farm-node"></a>
<a id="cwf"></a>
<a id="farm-check"></a>
- [`compare-web-farm-node`](docs/commands/compare-web-farm-node.md) - Compare file content across web farm nodes, `check-farm`, `check-web-farm-node`, `cwf`, `farm-check`
<a id="create-k8-files"></a>
<a id="ck8f"></a>
- [`create-k8-files`](docs/commands/create-k8-files.md) - Prepare K8 files for deployment, `ck8f`
<a id="delete-infrastructure"></a>
<a id="di-delete"></a>
<a id="remove-infrastructure"></a>
- [`delete-infrastructure`](docs/commands/delete-infrastructure.md) - Delete Kubernetes infrastructure for Creatio (removes namespace and all resources), `di-delete`, `remove-infrastructure`
<a id="deploy-creatio"></a>
<a id="dc"></a>
<a id="ic"></a>
<a id="install-creatio"></a>
- [`deploy-creatio`](docs/commands/deploy-creatio.md) - Install Creatio from a distribution package, with Explorer support for unambiguous local infrastructure defaults, `dc`, `ic`, `install-creatio`
<a id="deploy-identity"></a>
- [`deploy-identity`](docs/commands/deploy-identity.md) - Deploy IdentityService to IIS and connect it to a Creatio environment
<a id="get-identity-service-config"></a>
- [`get-identity-service-config`](docs/commands/get-identity-service-config.md) - Read (or derive) the OAuth IdentityService configuration of a Creatio environment over REST
<a id="resolve-oauth-system-user"></a>
- [`resolve-oauth-system-user`](docs/commands/resolve-oauth-system-user.md) - Resolve a Creatio system user (SysAdminUnit) by name or id over DataService REST
<a id="create-oauth-technical-user"></a>
- [`create-oauth-technical-user`](docs/commands/create-oauth-technical-user.md) - Create a Creatio technical user for a server-to-server OAuth app via OAuthConfigService REST
<a id="create-server-to-server-oauth-app"></a>
- [`create-server-to-server-oauth-app`](docs/commands/create-server-to-server-oauth-app.md) - Create a server-to-server (client_credentials) OAuth app in Creatio via OAuthConfigService REST
<a id="verify-oauth-app"></a>
- [`verify-oauth-app`](docs/commands/verify-oauth-app.md) - Verify a server-to-server OAuth app: acquire a client_credentials token and run a bearer DataService smoke test
<a id="deploy-infrastructure"></a>
<a id="di"></a>
- [`deploy-infrastructure`](docs/commands/deploy-infrastructure.md) - Deploy Kubernetes infrastructure for Creatio (namespace, storage, redis, postgres, pgadmin), `di`
<a id="get-build-info"></a>
<a id="bi"></a>
<a id="buildinfo"></a>
- [`get-build-info`](docs/commands/get-build-info.md) - Resolve the build artifact path for a Creatio distribution, `bi`, `buildinfo`
<a id="manage-windows-features"></a>
<a id="mng-win-features"></a>
<a id="mwf"></a>
- [`manage-windows-features`](docs/commands/manage-windows-features.md) - Install windows features required for Creatio, `mng-win-features`, `mwf`
<a id="open-k8-files"></a>
<a id="cfg-k8"></a>
<a id="cfg-k8f"></a>
<a id="cfg-k8s"></a>
- [`open-k8-files`](docs/commands/open-k8-files.md) - Open the Kubernetes manifests folder, `cfg-k8`, `cfg-k8f`, `cfg-k8s`
<a id="restore-db"></a>
<a id="rdb"></a>
- [`restore-db`](docs/commands/restore-db.md) - Restore a database backup, `rdb`

## Local Instance Management

<a id="clear-redis-db"></a>
<a id="flushdb"></a>
- [`clear-redis-db`](docs/commands/clear-redis-db.md) - Clear redis database, `flushdb`
<a id="CustomizeDataProtection"></a>
<a id="cdp"></a>
- [`CustomizeDataProtection`](docs/commands/CustomizeDataProtection.md) - Toggle CustomizeDataProtection in appsettings.json, `cdp`
<a id="hosts"></a>
<a id="list-hosts"></a>
- [`hosts`](docs/commands/hosts.md) - List all Creatio hosts and their status, `list-hosts`
<a id="last-compilation-log"></a>
<a id="lcl"></a>
- [`last-compilation-log`](docs/commands/last-compilation-log.md) - Get last compilation log, `lcl`
<a id="restart-web-app"></a>
<a id="restart"></a>
- [`restart-web-app`](docs/commands/restart-web-app.md) - Restart a web application, `restart`
<a id="set-fsm-config"></a>
<a id="fsmc"></a>
<a id="sfsmc"></a>
- [`set-fsm-config`](docs/commands/set-fsm-config.md) - Set file system mode properties in config file, `fsmc`, `sfsmc`
<a id="start"></a>
<a id="sc"></a>
<a id="start-creatio"></a>
<a id="start-server"></a>
- [`start`](docs/commands/start.md) - Start local Creatio application, `sc`, `start-creatio`, `start-server`
<a id="stop"></a>
<a id="stop-creatio"></a>
- [`stop`](docs/commands/stop.md) - Stop Creatio application(s) and remove services, `stop-creatio`
<a id="turn-farm-mode"></a>
<a id="farm-mode"></a>
<a id="tfm"></a>
- [`turn-farm-mode`](docs/commands/turn-farm-mode.md) - Configure IIS site for Creatio web farm deployment, `farm-mode`, `tfm`
<a id="turn-fsm"></a>
<a id="fsm"></a>
<a id="tfsm"></a>
- [`turn-fsm`](docs/commands/turn-fsm.md) - Turn file system mode on or off for an environment, `fsm`, `tfsm`
<a id="uninstall-creatio"></a>
<a id="uc"></a>
- [`uninstall-creatio`](docs/commands/uninstall-creatio.md) - Uninstall local Creatio with shared-pool-safe IIS profile and best-effort dbHub cleanup, `uc`
<a id="upload-license"></a>
<a id="license"></a>
<a id="load-license"></a>
<a id="loadlicense"></a>
- [`upload-license`](docs/commands/upload-license.md) - Load license to selected environment, `license`, `load-license`, `loadlicense`

## Theming

<a id="build-theme"></a>
- [`build-theme`](docs/commands/build-theme.md) - Build the artifacts of a Creatio theme from brand colours and fonts
<a id="clear-themes-cache"></a>
<a id="flush-themes"></a>
- [`clear-themes-cache`](docs/commands/clear-themes-cache.md) - Refresh the Creatio theme cache (Creatio 10.0.0+), `flush-themes`
<a id="create-theme"></a>
- [`create-theme`](docs/commands/create-theme.md) - Create a custom Creatio theme on an environment (Creatio 10.0.0+)
<a id="delete-theme"></a>
- [`delete-theme`](docs/commands/delete-theme.md) - Delete a custom Creatio theme from an environment (Creatio 10.0.0+)
<a id="list-themes"></a>
<a id="get-themes"></a>
- [`list-themes`](docs/commands/list-themes.md) - List the custom Creatio themes available on an environment (Creatio 10.0.0+), `get-themes`
<a id="update-theme"></a>
- [`update-theme`](docs/commands/update-theme.md) - Overwrite an existing custom Creatio theme on an environment (Creatio 10.0.0+)

## Integrations & Tools

<a id="install-dbhub"></a>
- [`install-dbhub`](docs/commands/install-dbhub.md) - Install, adopt, or repair a local loopback dbHub HTTP MCP server
<a id="sync-dbhub"></a>
- [`sync-dbhub`](docs/commands/sync-dbhub.md) - Reconcile clio-owned dbHub sources with local Creatio environments
<a id="ring"></a>
- [`ring`](docs/commands/ring.md) - Manage the opt-in internal clio-ring desktop preview (experimental; Windows x64 only)
<a id="delete-toolkit"></a>
- [`delete-toolkit`](docs/commands/delete-toolkit.md) - Uninstall the Creatio AI App Development Toolkit from coding agents (alias: delete-skill)
<a id="env-ui"></a>
<a id="far"></a>
<a id="gui"></a>
- [`env-ui`](docs/commands/env-ui.md) - Interactive console UI for environment management, `far`, `gui`
<a id="install-gate"></a>
<a id="gate"></a>
<a id="installgate"></a>
<a id="update-gate"></a>
- [`install-gate`](docs/commands/install-gate.md) - Install or update cliogate in Creatio, `gate`, `installgate`, `update-gate`
<a id="install-toolkit"></a>
- [`install-toolkit`](docs/commands/install-toolkit.md) - Install the Creatio AI App Development Toolkit for all detected coding agents (alias: install-skills)
<a id="link-package-store"></a>
<a id="lps"></a>
- [`link-package-store`](docs/commands/link-package-store.md) - Link PackageStore packages into an environment, `lps`
<a id="mcp-http"></a>
- [`mcp-http`](docs/commands/mcp-http.md) - Start the MCP server over HTTP (Streamable HTTP), with standard OAuth 2.1 Resource-Server authorization (off by default; whole-endpoint when configured) and an optional multi-tenant per-request credential-passthrough edge (dev/offline platform-api-key fallback when OAuth is not configured)
<a id="mcp-server"></a>
<a id="mcp"></a>
- [`mcp-server`](docs/commands/mcp-server.md) - Start the MCP server over stdio, `mcp`
<a id="update-toolkit"></a>
- [`update-toolkit`](docs/commands/update-toolkit.md) - Update the Creatio AI App Development Toolkit for all detected coding agents (alias: update-skill)

## Identity & Authentication

<a id="get-identity-assertion"></a>
<a id="identity-assertion"></a>
- [`get-identity-assertion`](docs/commands/get-identity-assertion.md) - Issue a short-lived signed identity assertion (JWT) for the current user, `identity-assertion`
<a id="get-identity-public-jwk"></a>
<a id="identity-public-jwk"></a>
- [`get-identity-public-jwk`](docs/commands/get-identity-public-jwk.md) - Get the instance public key (JWK) for Identity Service V3, `identity-public-jwk`
<a id="regenerate-identity-signing-key"></a>
<a id="identity-regenerate-key"></a>
- [`regenerate-identity-signing-key`](docs/commands/regenerate-identity-signing-key.md) - Regenerate the instance identity-assertion signing key pair, `identity-regenerate-key`
<a id="check-auth-code-flow"></a>
<a id="auth-code-flow"></a>
- [`check-auth-code-flow`](docs/commands/check-auth-code-flow.md) - Check whether the environment can use the OAuth authorization code flow, `auth-code-flow`
<a id="register-sso-provider"></a>
<a id="sso-register-provider"></a>
- [`register-sso-provider`](docs/commands/register-sso-provider.md) - Register a new OIDC SSO provider in Creatio, `sso-register-provider`

## General

<a id="assert"></a>
- [`assert`](docs/commands/assert.md) - Validates infrastructure and filesystem resources
<a id="component-registry-refresh"></a>
<a id="component-registry"></a>
- [`component-registry-refresh`](docs/commands/component-registry-refresh.md) - Refresh the local Freedom UI component registry cache from the academy.creatio.com CDN, `component-registry`
<a id="get-component-info"></a>
<a id="component-info"></a>
- [`get-component-info`](docs/commands/get-component-info.md) - Get curated Freedom UI component metadata by component type or list the catalog, `component-info`
<a id="get-user-culture"></a>
<a id="profile-language"></a>
- [`get-user-culture`](docs/commands/get-user-culture.md) - Print the logged-in Creatio user's profile culture (e.g. en-US, uk-UA) for an environment, `profile-language`
<a id="healthcheck"></a>
<a id="hc"></a>
- [`healthcheck`](docs/commands/healthcheck.md) - Run Creatio health checks, `hc`
<a id="register"></a>
- [`register`](docs/commands/register.md) - Register clio shell integrations
<a id="config"></a>
- [`config`](docs/commands/config.md) - View and set clio configuration defaults (e.g. deploy-creatio defaults used by the Explorer context menu)
<a id="pin-certificate"></a>
- [`pin-certificate`](docs/commands/pin-certificate.md) - Select the preferred local IIS certificate for HTTPS deployments
<a id="set-app-icon"></a>
<a id="ai"></a>
<a id="appicon"></a>
<a id="set-icon"></a>
<a id="set-application-icon"></a>
- [`set-app-icon`](docs/commands/set-app-icon.md) - Set application icon, `ai`, `appicon`, `set-icon`
<a id="set-app-version"></a>
<a id="appversion"></a>
<a id="set-application-version"></a>
- [`set-app-version`](docs/commands/set-app-version.md) - Set application version, `appversion`
<a id="unregister"></a>
- [`unregister`](docs/commands/unregister.md) - Remove clio shell integrations
