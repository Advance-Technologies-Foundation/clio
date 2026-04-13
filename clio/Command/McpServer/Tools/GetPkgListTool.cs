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
	/// Stable MCP tool name for listing packages from a Creatio environment.
	/// </summary>
	internal const string GetPkgListToolName = "list-packages";

	/// <summary>
	/// Returns environment packages as structured MCP JSON.
	/// </summary>
	[McpServerTool(Name = GetPkgListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Returns packages from the specified Creatio environment as structured JSON with package name, version, and maintainer.")]
	public IReadOnlyList<PackageListItemResult> GetPkgList(
		[Description("List-packages parameters")] [Required] GetPkgListArgs args) {
		PkgListOptions options = new() {
			Environment = args.EnvironmentName,
			SearchPattern = args.Filter ?? string.Empty
		};
		GetPkgListCommand resolvedCommand;
		try {
			resolvedCommand = ResolveCommand<GetPkgListCommand>(options);
		} catch (Exception ex) {
			throw new InvalidOperationException($"Failed to resolve environment: {ex.Message}", ex);
		}
		if (!resolvedCommand.TryGetFilteredPackages(options, out IReadOnlyList<PackageInfo> packages,
				out string errorMessage, out string remediationMessage)) {
			string message = string.Join(" ", new[] { errorMessage, remediationMessage }
				.Where(value => !string.IsNullOrWhiteSpace(value)));
			throw new InvalidOperationException(message);
		}
		return packages
			.Select(package => new PackageListItemResult(
				package.Descriptor.Name,
				package.Descriptor.PackageVersion,
				package.Descriptor.Maintainer))
			.ToList();
	}
}

/// <summary>
/// MCP arguments for the <c>list-packages</c> tool.
/// </summary>
public sealed record GetPkgListArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("filter")]
	[property: Description("Optional case-insensitive package-name filter")]
	string? Filter = null
);

/// <summary>
/// Structured package-list item returned by the <c>list-packages</c> MCP tool.
/// </summary>
public sealed record PackageListItemResult(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("maintainer")] string Maintainer);
