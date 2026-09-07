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
/// MCP probe tool that resolves the package a run's design-time writes land in and verifies it can receive
/// them.
/// </summary>
[McpServerToolType]
public sealed class GetTargetPackageTool(
	GetTargetPackageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<GetTargetPackageOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-target-package";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["packageName"] = "package",
			["package_name"] = "package",
			["package-name"] = "package"
		};

	/// <summary>Resolves the target package and returns its name, or a classified failure.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Resolve the package a run's design-time writes land in on a registered environment, and " +
		"verify it can receive them. Pass package to resolve a package the user named (checks it exists and " +
		"is not locked); omit package to resolve the package the environment's CurrentPackageId system " +
		"setting names. Call this BEFORE telling the user which package new data will be added to, and pass " +
		"the returned package-name to every command of the same run (create-theme, set-logo, " +
		"set-background-image) so everything lands in one package. State the package-name, never a raw id, " +
		"and never invent one. On success=false with resolutionFailed=true the environment answered and there " +
		"is no usable target — relay the error and ask the user for another package; with " +
		"resolutionFailed=false the environment could not be asked, so retry instead of reporting that there " +
		"is no target package.")]
	public GetTargetPackageResponse GetTargetPackage(
		[Description("Parameters: environment-name (required); package (optional — the package the user named; omit to resolve the environment's CurrentPackageId package).")]
		[Required] GetTargetPackageArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".", "Valid: environment-name, package.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return Failure("environment-name is required and cannot be empty.");
		}
		GetTargetPackageOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.Package
		};
		return ExecuteResolved<GetTargetPackageCommand, GetTargetPackageResponse>(options,
			resolvedCommand => {
				resolvedCommand.TryGetTargetPackage(options, out GetTargetPackageResponse response);
				if (response.Success) {
					return response;
				}
				return new GetTargetPackageResponse {
					Success = false,
					ResolutionFailed = response.ResolutionFailed,
					Error = SensitiveErrorTextRedactor.Redact(
						string.IsNullOrWhiteSpace(response.Error)
							? "Failed to resolve the target package."
							: response.Error)
				};
			},
			Failure);
	}

	private static GetTargetPackageResponse Failure(string error) {
		return new GetTargetPackageResponse {
			Success = false,
			ResolutionFailed = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}

/// <summary>
/// MCP arguments for the <c>get-target-package</c> tool.
/// </summary>
public sealed record GetTargetPackageArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("package")]
	[property: Description("Package the user named. Omit to resolve the package the environment's CurrentPackageId system setting names.")]
	string? Package = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
