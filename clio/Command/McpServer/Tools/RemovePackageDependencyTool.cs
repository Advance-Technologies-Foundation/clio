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
/// MCP tool surface for the <c>remove-package-dependency</c> command.
/// </summary>
[McpServerToolType]
public sealed class RemovePackageDependencyTool(
	RemovePackageDependencyCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<RemovePackageDependencyOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for removing package dependencies.
	/// </summary>
	internal const string RemovePackageDependencyToolName = "remove-package-dependency";

	/// <summary>
	/// Removes one or more package dependencies from a package in a registered Creatio environment.
	/// </summary>
	[McpServerTool(Name = RemovePackageDependencyToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Removes one or more package dependencies from a Creatio package via PackageService.svc.

				 The symmetric counterpart of add-package-dependency. Use it to roll back a dependency that was
				 added only to unblock the schema designer once it is no longer needed. Dependencies are matched
				 by name (case-insensitive); removing a dependency that is not present is a no-op (idempotent).

				 CAUTION: keep the dependency if your package still extends an object whose upper layer the
				 dependency owns — removing it will break the schema designer again. See
				 get-guidance name=package-dependencies.
				 """)]
	public CommandExecutionResult RemovePackageDependency(
		[Description("remove-package-dependency parameters")] [Required] RemovePackageDependencyArgs args) {
		RemovePackageDependencyOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			Dependencies = (args.Dependencies ?? [])
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.ToArray()
		};
		try {
			return InternalExecute<RemovePackageDependencyCommand>(options);
		}
		catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)]);
		}
	}
}

/// <summary>
/// MCP arguments for the <c>remove-package-dependency</c> tool.
/// </summary>
public sealed record RemovePackageDependencyArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package whose dependency list will be trimmed")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("dependencies")]
	[property: Description("Dependency package names to remove (matched by name, case-insensitive). "
		+ "Example: [\"CrtLeadOppMgmtApp\"]")]
	[property: Required]
	IReadOnlyList<string> Dependencies
);
