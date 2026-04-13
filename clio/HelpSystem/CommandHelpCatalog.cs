using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandLine;

namespace Clio.Help;

internal enum HelpGroupId {
	ApplicationManagement,
	PackageManagement,
	Workspace,
	Development,
	DeploymentAndInfrastructure,
	LocalInstanceManagement,
	IntegrationsAndTools,
	General
}

internal sealed record HelpGroupMetadata(HelpGroupId Id, string Title);

internal sealed record HelpCommandMetadata(
	Type OptionsType,
	string CanonicalName,
	string ShortDescription,
	HelpGroupId GroupId,
	IReadOnlyList<string> Aliases,
	IReadOnlyList<string> LegacyNames,
	IReadOnlyList<string> RelatedCommands,
	string Requirement,
	bool Hidden,
	int SourceIndex);

internal sealed class CommandHelpCatalog {
	private const string CallService = "call-service";
	private const string GetEntitySchemaColumnProperties = "get-entity-schema-column-properties";
	private const string GetEntitySchemaProperties = "get-entity-schema-properties";
	private const string LockPackage = "lock-package";
	private const string UnlockPackage = "unlock-package";
	private const string ModifyEntitySchemaColumn = "modify-entity-schema-column";
	private const string PushWorkspace = "push-workspace";
	private const string ClioGateRequirement = "cliogate must be installed on the target Creatio environment.";
	private static readonly IReadOnlyList<HelpGroupMetadata> OrderedGroups = [
		new(HelpGroupId.ApplicationManagement, "Application Management"),
		new(HelpGroupId.PackageManagement, "Package Management"),
		new(HelpGroupId.Workspace, "Workspace"),
		new(HelpGroupId.Development, "Development"),
		new(HelpGroupId.DeploymentAndInfrastructure, "Deployment & Infrastructure"),
		new(HelpGroupId.LocalInstanceManagement, "Local Instance Management"),
		new(HelpGroupId.IntegrationsAndTools, "Integrations & Tools"),
		new(HelpGroupId.General, "General")
	];

	private static readonly IReadOnlyDictionary<string, string> DescriptionOverrides =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["add-item"] = "Generate package item models from Creatio metadata",
			["activate-pkg"] = "Activate a package in Creatio",
			["add-schema"] = "Create a schema file in a workspace package",
			["add-user-task"] = "Create a user task schema in a workspace package",
			["alm-deploy"] = "Deploy a package to Creatio",
			["apply-manifest"] = "Apply an environment manifest",
			[CallService] = "Call a Creatio service endpoint",
			["compare-web-farm-node"] = "Compare file content across web farm nodes",
			["CustomizeDataProtection"] = "Toggle CustomizeDataProtection in appsettings.json",
			["dataservice"] = "Send a Creatio DataService request",
			["deploy-application"] = "Copy an application package between Creatio environments",
			["deploy-creatio"] = "Install Creatio from a distribution package",
			["delete-schema"] = "Delete a schema from a workspace package",
			["delete-app-section"] = "Delete a section from an existing installed application",
			["deactivate-pkg"] = "Deactivate a package in Creatio",
			["execute-sql-script"] = "Execute a SQL script in Creatio",
			["externalLink"] = "Handle external deep links",
			["list-apps"] = "List installed applications",
			["create-app"] = "Create a new application in Creatio",
			["get-app-info"] = "Get information about an installed Creatio application",
			["create-lookup"] = "Create a lookup entity schema in a remote Creatio package",
			["get-app-hash"] = "Calculate the hash of an application package",
			["get-page"] = "Read a Freedom UI page as a merged bundle plus raw schema body",
			["list-app-sections"] = "List sections of an existing installed application",
			["list-pages"] = "List Freedom UI page schemas in a Creatio environment",
			[GetEntitySchemaProperties] = "Get properties from a remote Creatio entity schema",
			["info"] = "Show clio, cliogate, and .NET runtime versions",
			[LockPackage] = "Lock a package in Creatio",
			["mcp-server"] = "Start the MCP server over stdio",
			["mock-data"] = "Generate mock data for unit tests",
			["modify-user-task-parameters"] = "Add or remove parameters in a user task schema",
			["new-pkg"] = "Create a new package project",
			["new-test-project"] = "Create a new test project",
			["new-ui-project"] = "Create a new Freedom UI project",
			["open-k8-files"] = "Open the Kubernetes manifests folder",
			["open-settings"] = "Open the clio settings file",
			["open-web-app"] = "Open a registered Creatio environment in the browser",
			["ping-app"] = "Verify connectivity to a Creatio environment",
			["push-nuget-pkg"] = "Push a NuGet package to a feed",
			["push-pkg"] = "Install a package into Creatio",
			["create-workspace"] = "Create a local workspace",
			["build-workspace"] = "Build the current workspace in Creatio",
			["get-build-info"] = "Resolve the build artifact path for a Creatio distribution",
			["extract-pkg-zip"] = "Extract a packaged application or package archive",
			["healthcheck"] = "Run Creatio health checks",
			["install-tide"] = "Install T.I.D.E. for the current environment",
			["show-package-file-content"] = "Show files that belong to a package",
			["download-application"] = "Download an application package from Creatio",
			["install-application"] = "Install an application package into Creatio",
			["link-package-store"] = "Link PackageStore packages into an environment",
			["listen"] = "Stream Creatio log events over WebSocket",
			["publish-app"] = "Publish a workspace to a ZIP archive or hub folder",
			["pack-nuget-pkg"] = "Pack a package into a NuGet artifact",
			["pkg-to-db"] = "Load packages into Creatio database storage",
			["pkg-to-file-system"] = "Load packages into Creatio file system storage",
			["cfg-worspace"] = "Configure workspace package selection",
			["compressApp"] = "Archive an application directory into ZIP",
			["reg-web-app"] = "Register a Creatio environment",
			["register"] = "Register clio shell integrations",
			["remove-data-binding-row-db"] = "Remove a row from a DB-first package data binding",
			["restore-configuration"] = "Restore the configuration from the last backup",
			["restore-db"] = "Restore a database backup",
			["set-webservice-url"] = "Set a base URL for a registered web service",
			["set-dev-mode"] = "Toggle developer mode for a Creatio environment",
			["get-webservice-url"] = "Show the configured base URL for a web service",
			["download-configuration"] = "Download configuration libraries from Creatio",
			["compile-package"] = "Compile one or more packages in Creatio",
			["compile-configuration"] = "Compile the full configuration in Creatio",
			["restore-workspace"] = "Restore editable packages into a workspace",
			["show-diff"] = "Compare settings between two Creatio environments",
			["show-web-app-list"] = "List registered Creatio environments",
			["set-syssetting"] = "Get or set a system setting value",
			["get-pkg-list"] = "List packages in a Creatio environment",
			["get-info"] = "Show system information for a Creatio instance",
			["install-gate"] = "Install or update cliogate in Creatio",
			["check-nuget-update"] = "Check NuGet for Creatio package updates",
			["uninstall-app-remote"] = "Uninstall an application package from Creatio",
			["update-page"] = "Update the raw schema body of a Freedom UI page",
			["unreg-web-app"] = "Remove a registered Creatio environment",
			["unregister"] = "Remove clio shell integrations",
			["update-cli"] = "Update clio",
			["upload-licenses"] = "Upload license files to Creatio",
			["upsert-data-binding-row-db"] = "Upsert a row in a DB-first package data binding"
		};

	private static readonly IReadOnlyDictionary<string, string[]> LegacyNameOverrides =
		new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
			[CallService] = ["callservice"],
			["cfg-worspace"] = ["configure-workspace"],
			["download-application"] = ["download-app"],
			["extract-pkg-zip"] = ["extract-package"],
			["info"] = ["ver"],
			["open-web-app"] = ["open"],
			["ping-app"] = ["ping"],
			["publish-app"] = ["publish-workspace"],
			["run"] = ["run-scenario"],
			["save-state"] = ["create-manifest"],
			["set-app-icon"] = ["set-application-icon"],
			["set-app-version"] = ["set-application-version"],
			["upload-licenses"] = ["lic"]
		};

	private static readonly IReadOnlyDictionary<string, string[]> RelatedCommands =
		new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
			["add-data-binding-row"] = ["create-data-binding", "remove-data-binding-row"],
			["create-data-binding"] = ["add-data-binding-row", "remove-data-binding-row", CallService],
			["create-entity-schema"] = [GetEntitySchemaProperties, ModifyEntitySchemaColumn],
			[GetEntitySchemaColumnProperties] = [GetEntitySchemaProperties, ModifyEntitySchemaColumn],
			[GetEntitySchemaProperties] = [GetEntitySchemaColumnProperties, ModifyEntitySchemaColumn],
			[LockPackage] = [UnlockPackage, PushWorkspace],
			[ModifyEntitySchemaColumn] = [GetEntitySchemaColumnProperties, GetEntitySchemaProperties],
			["modify-user-task-parameters"] = ["add-user-task", "delete-schema"],
			["remove-data-binding-row"] = ["create-data-binding", "add-data-binding-row"],
			["restore-workspace"] = ["create-workspace", PushWorkspace, "install-gate"],
			[UnlockPackage] = [LockPackage, PushWorkspace],
			["update-entity-schema"] = [ModifyEntitySchemaColumn, GetEntitySchemaProperties]
		};

	private static readonly IReadOnlyDictionary<string, string> RequirementOverrides =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["add-item"] = "cliogate must be installed when generating models from a Creatio environment.",
			["create-entity-schema"] = ClioGateRequirement,
			[ModifyEntitySchemaColumn] = ClioGateRequirement,
			["update-entity-schema"] = ClioGateRequirement,
			[GetEntitySchemaColumnProperties] = ClioGateRequirement,
			[GetEntitySchemaProperties] = ClioGateRequirement
		};

	private static readonly HashSet<string> DeploymentCommands =
		[
			"build-docker-image",
			"check-windows-features",
			"compare-web-farm-node",
			"create-k8-files",
			"delete-infrastructure",
			"deploy-creatio",
			"deploy-infrastructure",
			"get-build-info",
			"manage-windows-features",
			"open-k8-files",
			"restore-db"
		];

	private static readonly HashSet<string> LocalInstanceCommands =
		[
			"clear-redis-db",
			"CustomizeDataProtection",
			"hosts",
			"last-compilation-log",
			"restart-web-app",
			"set-fsm-config",
			"start",
			"stop",
			"turn-farm-mode",
			"turn-fsm",
			"uninstall-creatio",
			"upload-license"
		];

	private static readonly HashSet<string> IntegrationCommands =
		[
			"delete-skill",
			"env-ui",
			"install-gate",
			"install-skills",
			"install-tide",
			"link-package-store",
			"mcp-server",
			"update-skill"
		];

	private static readonly HashSet<string> DevelopmentCommands =
		[
			"add-item",
			"add-schema",
			"add-user-task",
			"alm-deploy",
			"apply-manifest",
			"call-service",
			"create-entity-schema",
			"create-lookup",
			"dataservice",
			"delete-schema",
			"download-configuration",
			"externalLink",
			"generate-process-model",
			"get-entity-schema-column-properties",
			"get-entity-schema-properties",
			"git-sync",
			"listen",
			"mock-data",
			"modify-entity-schema-column",
			"modify-user-task-parameters",
			"new-test-project",
			"new-ui-project",
			"open-settings",
			"get-page",
			"list-pages",
			"update-page",
			"run",
			"save-state",
			"show-package-file-content",
			"update-entity-schema",
			"execute-sql-script"
		];

	private static readonly HashSet<string> GeneralCommands =
		[
			"assert",
			"healthcheck",
			"register",
			"unregister"
		];

	private static readonly HashSet<string> WorkspaceCommands =
		[
			"build-workspace",
			"cfg-worspace",
			"create-workspace",
			"link-core-src",
			"link-from-repository",
			"link-to-repository",
			"merge-workspaces",
			"publish-app",
			"push-workspace",
			"restore-workspace",
			"switch-nuget-to-dll-reference"
		];

	private static readonly HashSet<string> PackageCommands =
		[
			"activate-pkg",
			"add-package",
			"check-nuget-update",
			"compile-configuration",
			"compile-package",
			"compressApp",
			"deactivate-pkg",
			"delete-pkg-remote",
			"extract-pkg-zip",
			"generate-pkg-zip",
			"get-pkg-list",
			"get-pkg-version",
			"install-nuget-pkg",
			"lock-package",
			"new-pkg",
			"pack-nuget-pkg",
			"pkg-hotfix",
			"pull-pkg",
			"push-nuget-pkg",
			"push-pkg",
			"restore-configuration",
			"restore-nuget-pkg",
			"set-pkg-version",
			"unlock-package"
		];

	private static readonly HashSet<string> ApplicationCommands =
		[
			"clear-local-env",
			"clone-env",
			"create-app",
			"create-app-section",
			"delete-app-section",
			"deploy-application",
			"download-application",
			"get-app-hash",
			"get-app-info",
			"list-app-sections",
			"list-apps",
			"get-info",
			"get-webservice-url",
			"install-application",
			"open-web-app",
			"ping-app",
			"reg-web-app",
			"set-dev-mode",
			"set-feature",
			"set-syssetting",
			"set-webservice-url",
			"show-diff",
			"show-local-envs",
			"show-web-app-list",
			"uninstall-app-remote",
			"unreg-web-app",
			"update-app-section"
		];

	private readonly Lazy<IReadOnlyList<HelpCommandMetadata>> _commands;
	private readonly Lazy<IReadOnlyDictionary<string, HelpCommandMetadata>> _lookup;

	public CommandHelpCatalog() {
		_commands = new Lazy<IReadOnlyList<HelpCommandMetadata>>(BuildCommands);
		_lookup = new Lazy<IReadOnlyDictionary<string, HelpCommandMetadata>>(BuildLookup);
	}

	public static IReadOnlyList<HelpGroupMetadata> Groups => OrderedGroups;

	public IReadOnlyList<HelpCommandMetadata> Commands => _commands.Value;

	public IReadOnlyList<HelpCommandMetadata> GetVisibleCommands() =>
		Commands.Where(command => !command.Hidden).ToArray();

	public IReadOnlyList<HelpCommandMetadata> GetCommandsForGroup(HelpGroupId groupId) =>
		GetVisibleCommands()
			.Where(command => command.GroupId == groupId)
			.OrderBy(command => command.CanonicalName, StringComparer.OrdinalIgnoreCase)
			.ToArray();

	public bool TryGetCommand(string name, out HelpCommandMetadata command) =>
		_lookup.Value.TryGetValue(name, out command);

	private static IReadOnlyList<HelpCommandMetadata> BuildCommands() =>
		Program.GetCommandOptionTypes()
			.Select((type, index) => CreateCommand(type, index))
			.ToArray();

	private IReadOnlyDictionary<string, HelpCommandMetadata> BuildLookup() {
		Dictionary<string, HelpCommandMetadata> lookup = new(StringComparer.OrdinalIgnoreCase);
		foreach (HelpCommandMetadata command in Commands) {
			lookup[command.CanonicalName] = command;
			foreach (string alias in command.Aliases) {
				lookup.TryAdd(alias, command);
			}
			foreach (string legacyName in command.LegacyNames) {
				lookup.TryAdd(legacyName, command);
			}
		}
		return lookup;
	}

	private static HelpCommandMetadata CreateCommand(Type optionsType, int sourceIndex) {
		VerbAttribute verb = optionsType.GetCustomAttribute<VerbAttribute>();
		string canonicalName = verb?.Name ?? optionsType.Name;
		IReadOnlyList<string> aliases = verb?.Aliases?
			.Where(alias => !string.IsNullOrWhiteSpace(alias)
				&& !alias.Any(char.IsWhiteSpace)
				&& !string.Equals(alias, canonicalName, StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
			.ToArray() ?? [];
		IReadOnlyList<string> legacyNames = LegacyNameOverrides.TryGetValue(canonicalName, out string[] names)
			? names
			: [];
		IReadOnlyList<string> relatedCommands = RelatedCommands.TryGetValue(canonicalName, out string[] related)
			? related
			: [];
		string requirement = RequirementOverrides.TryGetValue(canonicalName, out string value)
			? value
			: string.Empty;
		return new HelpCommandMetadata(
			optionsType,
			canonicalName,
			GetShortDescription(canonicalName, verb?.HelpText),
			GetGroup(canonicalName, sourceIndex),
			aliases,
			legacyNames,
			relatedCommands,
			requirement,
			verb?.Hidden ?? false,
			sourceIndex);
	}

	private static string GetShortDescription(string canonicalName, string sourceDescription) {
		if (DescriptionOverrides.TryGetValue(canonicalName, out string description)) {
			return description;
		}
		return string.IsNullOrWhiteSpace(sourceDescription)
			? canonicalName
			: sourceDescription.Trim().TrimEnd('.');
	}

	private static HelpGroupId GetGroup(string canonicalName, int sourceIndex) {
		if (DeploymentCommands.Contains(canonicalName)) {
			return HelpGroupId.DeploymentAndInfrastructure;
		}
		if (LocalInstanceCommands.Contains(canonicalName)) {
			return HelpGroupId.LocalInstanceManagement;
		}
		if (IntegrationCommands.Contains(canonicalName)) {
			return HelpGroupId.IntegrationsAndTools;
		}
		if (DevelopmentCommands.Contains(canonicalName)) {
			return HelpGroupId.Development;
		}
		if (WorkspaceCommands.Contains(canonicalName)) {
			return HelpGroupId.Workspace;
		}
		if (PackageCommands.Contains(canonicalName)) {
			return HelpGroupId.PackageManagement;
		}
		if (ApplicationCommands.Contains(canonicalName)) {
			return HelpGroupId.ApplicationManagement;
		}
		if (GeneralCommands.Contains(canonicalName)) {
			return HelpGroupId.General;
		}
		if (sourceIndex <= 22) {
			return HelpGroupId.ApplicationManagement;
		}
		if (sourceIndex <= 34) {
			return HelpGroupId.PackageManagement;
		}
		if (sourceIndex <= 51) {
			return HelpGroupId.Workspace;
		}
		if (sourceIndex <= 103) {
			return HelpGroupId.Development;
		}
		if (sourceIndex <= 111) {
			return HelpGroupId.LocalInstanceManagement;
		}
		return HelpGroupId.General;
	}
}
