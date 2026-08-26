using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>MCP tools for inventorying and selectively deleting clio-managed PostgreSQL templates.</summary>
[McpServerToolType]
public sealed class DbTemplatePruneTool {
	/// <summary>Stable MCP tool name for managed-template inventory.</summary>
	internal const string ListDbTemplatesToolName = "list-db-templates";

	/// <summary>Stable MCP tool name for explicitly targeted managed-template deletion.</summary>
	internal const string PruneDbTemplatesToolName = "prune-db-templates";

	private readonly IDbTemplatePruneService _pruneService;

	/// <summary>Initializes a new instance of the <see cref="DbTemplatePruneTool"/> class.</summary>
	/// <param name="pruneService">Shared inventory and deletion service.</param>
	public DbTemplatePruneTool(IDbTemplatePruneService pruneService) {
		_pruneService = pruneService ?? throw new ArgumentNullException(nameof(pruneService));
	}

	/// <summary>Inventories clio-managed templates on one configured PostgreSQL server.</summary>
	[McpServerTool(Name = ListDbTemplatesToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("Lists clio-managed PostgreSQL template databases on a named configured server. Returns structured source, database name, creation date, and metadata version. Call this before prune-db-templates; an empty successful list is distinct from a configuration or database-access failure.")]
	public DbTemplateInventoryResult ListDbTemplates(
		[Description("Template inventory parameters")] [Required] ListDbTemplatesArgs args) =>
		_pruneService.Inventory(args.DbServerName);

	/// <summary>Deletes only explicitly named, freshly revalidated clio-managed templates.</summary>
	[McpServerTool(Name = PruneDbTemplatesToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Deletes only the explicitly named clio-managed PostgreSQL templates on a configured server. Call list-db-templates first, pass a non-empty databaseNames list, and obtain user approval through clio-run-destructive. The tool revalidates every name, skips templates with active sessions, never infers all, never force-disconnects, and returns every requested outcome.")]
	public DbTemplatePruneResult PruneDbTemplates(
		[Description("Explicit template deletion parameters")] [Required] PruneDbTemplatesArgs args) =>
		_pruneService.Prune(args.DbServerName, args.DatabaseNames);
}

/// <summary>MCP arguments for managed PostgreSQL template inventory.</summary>
public sealed record ListDbTemplatesArgs(
	[property: JsonPropertyName("dbServerName")]
	[property: Description("Configured local PostgreSQL server name from clio settings")]
	[property: Required]
	string DbServerName);

/// <summary>MCP arguments for explicitly targeted managed PostgreSQL template deletion.</summary>
public sealed record PruneDbTemplatesArgs(
	[property: JsonPropertyName("dbServerName")]
	[property: Description("Configured local PostgreSQL server name from clio settings")]
	[property: Required]
	string DbServerName,

	[property: JsonPropertyName("databaseNames")]
	[property: Description("Explicit non-empty list of template database names returned by list-db-templates")]
	[property: Required]
	IReadOnlyList<string> DatabaseNames);
