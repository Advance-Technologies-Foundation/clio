using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>create-package</c> command.
/// </summary>
[McpServerToolType]
public sealed class CreatePackageTool(
	CreatePackageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreatePackageOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for creating a package in an environment.
	/// </summary>
	internal const string CreatePackageToolName = "create-package";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["packageName"] = "package-name",
			["package_name"] = "package-name",
			["package"] = "package-name",
			["applicationCode"] = "application-code",
			["application_code"] = "application-code",
			["app-code"] = "application-code"
		};

	/// <summary>
	/// Creates a new package in a registered Creatio environment and returns its readback.
	/// </summary>
	[McpServerTool(Name = CreatePackageToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Creates a new, empty, editable package in a registered Creatio environment.
				 It is the same operation as "Create package" in the Configuration section (PackageService.svc).
				 Use it when the user asks for a separate package (for example to hold a migration's output
				 or to extend out-of-the-box functionality); page and schema writes already resolve their own
				 target package, so do not create one just because none was named.

				 The environment's SchemaNamePrefix is prepended when package-name does not start with it
				 ("Calls" becomes "UsrCalls"), so use the returned package-name — not the one you sent — for
				 get-target-package and every later write. The maintainer comes from the environment's
				 Maintainer system setting. With application-code the package is created inside that
				 installed application; without it the package is standalone.

				 Refuses, and changes nothing, when a package with that name already exists, a dependency or
				 the application is not found, or the environment rejects the name. package-created tells the
				 failure kinds apart: false means nothing changed; true with success=false means the package
				 exists but a later step failed, and error says which (dependencies not applied: add them with
				 add-package-dependency; readback failed: check it with list-packages); null means the create
				 request failed in transport and the outcome is unknown: check list-packages before retrying.
				 Returns package-uid, package-name, maintainer, description, dependencies, install-type and
				 editable. Not idempotent: a second call with the same name is refused as a duplicate.
				 """)]
	public CreatePackageResponse CreatePackage(
		[Description("Parameters: environment-name, package-name (required); description, dependencies, application-code (optional).")]
		[Required] CreatePackageArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, package-name, description, dependencies, application-code.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return Failure("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			return Failure("package-name is required and cannot be empty.");
		}
		CreatePackageOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			Description = args.Description,
			Dependencies = args.Dependencies ?? [],
			ApplicationCode = args.ApplicationCode
		};
		return ExecuteResolved<CreatePackageCommand, CreatePackageResponse>(options,
			resolvedCommand => {
				CreatePackageResponse response = resolvedCommand.CreatePackage(options);
				return response.Error is null
					? response
					: response with { Error = SensitiveErrorTextRedactor.Redact(response.Error) };
			},
			Failure);
	}

	private static CreatePackageResponse Failure(string error) =>
		new() { Success = false, PackageCreated = false, Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error };
}

/// <summary>
/// MCP arguments for the <c>create-package</c> tool.
/// </summary>
public sealed record CreatePackageArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Name of the package to create; the environment's SchemaNamePrefix is prepended when missing")]
	[property: Required]
	string? PackageName = null,

	[property: JsonPropertyName("description")]
	[property: Description("Optional package description")]
	string? Description = null,

	[property: JsonPropertyName("dependencies")]
	[property: Description("Optional names of installed packages the new package depends on, for example [\"CrtBase\"]")]
	IReadOnlyList<string>? Dependencies = null,

	[property: JsonPropertyName("application-code")]
	[property: Description("Optional code of an installed application to create the package in; omit for a standalone package")]
	string? ApplicationCode = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
