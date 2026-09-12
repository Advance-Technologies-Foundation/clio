using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>list-packages</c> command.
/// </summary>
public sealed class GetPkgListTool(
	GetPkgListCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PkgListOptions>(command, logger, commandResolver) {
	/// <summary>
	/// Default maximum number of packages returned by one MCP call.
	/// </summary>
	internal const int DefaultLimit = 50;

	/// <summary>
	/// Stable MCP tool name for listing packages from a Creatio environment.
	/// </summary>
	internal const string GetPkgListToolName = "list-packages";

	/// <summary>
	/// Returns environment packages as structured MCP JSON.
	/// </summary>
	[McpServerTool(Name = GetPkgListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Returns a bounded page of packages from the specified Creatio environment as structured JSON. Results are ordered by package name and capped at 50 by default. The response always reports count, total, offset, limit, and truncated so callers can page through the full filtered set.")]
	public PackageListResponse GetPkgList(
		[Description("List-packages parameters")] [Required] GetPkgListArgs args) {
		if (args.Limit < 0) {
			throw new ArgumentOutOfRangeException(nameof(args), args.Limit,
				$"limit must be zero or greater. Omit limit or pass 0 to use the default of {DefaultLimit}.");
		}
		if (args.Offset < 0) {
			throw new ArgumentOutOfRangeException(nameof(args), args.Offset,
				"offset must be zero or greater.");
		}
		PkgListOptions options = new() {
			Environment = args.EnvironmentName,
			SearchPattern = args.Filter ?? string.Empty
		};
		GetPkgListCommand resolvedCommand;
		try {
			resolvedCommand = ResolveCommand<GetPkgListCommand>(options);
		} catch (Exception ex) {
			throw new InvalidOperationException(
				$"Failed to resolve environment '{args.EnvironmentName}': {ex.Message}. " +
				"Verify the environment is registered with 'reg-web-app' and accessible.", ex);
		}
		if (!resolvedCommand.TryGetFilteredPackages(options, out IReadOnlyList<PackageInfo> packages,
				out string errorMessage, out string remediationMessage)) {
			string message = string.Join(" ", new[] { errorMessage, remediationMessage }
				.Where(value => !string.IsNullOrWhiteSpace(value)));
			throw new InvalidOperationException(message);
		}
		int effectiveLimit = args.Limit is null or 0 ? DefaultLimit : args.Limit.Value;
		IReadOnlyList<PackageListItemResult> packagePage = packages
			.Skip(args.Offset)
			.Take(effectiveLimit)
			.Select(package => new PackageListItemResult(
				package.Descriptor.Name,
				package.Descriptor.PackageVersion,
				package.Descriptor.Maintainer,
				package.Descriptor.UId.ToString()))
			.ToList();
		bool truncated = (long)args.Offset + packagePage.Count < packages.Count;
		return new PackageListResponse(
			packagePage,
			packagePage.Count,
			packages.Count,
			args.Offset,
			effectiveLimit,
			truncated);
	}
}

/// <summary>
/// MCP arguments for the <c>list-packages</c> tool.
/// </summary>
public sealed record GetPkgListArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("filter")]
	[property: Description("Optional case-insensitive package-name filter")]
	string? Filter = null,

	[property: JsonPropertyName("limit")]
	[property: Description("Maximum number of packages to return. Omit or pass 0 to use the default of 50. A negative value is rejected.")]
	int? Limit = null,

	[property: JsonPropertyName("offset")]
	[property: Description("Number of matching packages to skip before returning this page. Defaults to 0. A negative value is rejected.")]
	int Offset = 0
);

/// <summary>
/// Structured paged response returned by the <c>list-packages</c> MCP tool.
/// </summary>
public sealed record PackageListResponse(
	[property: JsonPropertyName("packages")] IReadOnlyList<PackageListItemResult> Packages,
	[property: JsonPropertyName("count")] int Count,
	[property: JsonPropertyName("total")] int Total,
	[property: JsonPropertyName("offset")] int Offset,
	[property: JsonPropertyName("limit")] int Limit,
	[property: JsonPropertyName("truncated")] bool Truncated);

/// <summary>
/// Structured package-list item returned by the <c>list-packages</c> MCP tool.
/// </summary>
public sealed record PackageListItemResult(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("maintainer")] string Maintainer,
	[property: JsonPropertyName("uId")] string UId);
