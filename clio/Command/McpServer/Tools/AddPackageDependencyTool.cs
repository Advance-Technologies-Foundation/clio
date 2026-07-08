using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>add-package-dependency</c> command.
/// </summary>
[McpServerToolType]
public sealed class AddPackageDependencyTool(
	AddPackageDependencyCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<AddPackageDependencyOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for adding package dependencies.
	/// </summary>
	internal const string AddPackageDependencyToolName = "add-package-dependency";

	/// <summary>
	/// Adds one or more package dependencies to a package in a registered Creatio environment.
	/// </summary>
	[McpServerTool(Name = AddPackageDependencyToolName, ReadOnly = false, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Adds one or more package dependencies to a Creatio package via PackageService.svc.

				 Use this when the schema designer or compiler fails for a package because it extends objects
				 owned by an app/package that is NOT in the target package's dependency list — the classic
				 symptom is `GetSchemaDesignItem returned an HTML error page` when opening a designer for a
				 layered object (for example an app extending the Opportunity layer without depending on
				 `CrtLeadOppMgmtApp`). Add the owning app/package as a dependency and the designer works.

				 Adding a dependency that is already present is a no-op (idempotent). The version of each
				 dependency defaults to its installed version when omitted.
				 """)]
	public CommandExecutionResult AddPackageDependency(
		[Description("add-package-dependency parameters")] [Required] AddPackageDependencyArgs args) {
		AddPackageDependencyOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			Dependencies = (args.Dependencies ?? [])
				.Where(dependency => dependency is not null && !string.IsNullOrWhiteSpace(dependency.Name))
				.Select(dependency => string.IsNullOrWhiteSpace(dependency.Version)
					? dependency.Name
					: $"{dependency.Name}:{dependency.Version}")
				.ToArray()
		};
		try {
			return InternalExecute<AddPackageDependencyCommand>(options);
		}
		catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))]);
		}
	}
}

/// <summary>
/// MCP arguments for the <c>add-package-dependency</c> tool.
/// </summary>
public sealed record AddPackageDependencyArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package whose dependency list will be extended")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("dependencies")]
	[property: Description("Dependencies to add. Each item is { name, version? }; "
		+ "version defaults to the installed package version.")]
	[property: Required]
	IReadOnlyList<PackageDependencyArg> Dependencies
);

/// <summary>
/// A single dependency entry for the <c>add-package-dependency</c> tool.
/// </summary>
public sealed record PackageDependencyArg(
	[property: JsonPropertyName("name")]
	[property: Description("Dependency package name, for example CrtLeadOppMgmtApp")]
	[property: Required]
	string Name,

	[property: JsonPropertyName("version")]
	[property: Description("Optional package version; defaults to the installed version of the dependency")]
	string Version
);
