using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that returns the full Freedom UI page replacing-schema chain (root first) with each
/// schema's raw body in one round-trip.
/// </summary>
[McpServerToolType]
public sealed class PageHierarchyGetTool(
	GetPageHierarchyCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetPageHierarchyOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-page-hierarchy";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Return the FULL Freedom UI page replacing-schema chain (root first, ordered by hierarchy level) with each " +
		"schema's raw body in ONE call. Use this instead of calling get-page / get-client-unit-schema once per schema " +
		"when you need to inspect a whole replacing chain (e.g. Classic->Freedom migration discovery): one round-trip " +
		"returns every body the deterministic bundle merge consumes. For a single schema's editable body use get-page. " +
		"Pass metadata-only for a lightweight chain listing, or offset/limit to page a very large chain. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public GetPageHierarchyResponse GetHierarchy(
		[Description("Parameters: schema-name (required); metadata-only (optional bool); offset/limit (optional ints for paging); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetPageHierarchyArgs args) {
		GetPageHierarchyOptions options = new() {
			SchemaName = args.SchemaName,
			MetadataOnly = args.MetadataOnly ?? false,
			Offset = args.Offset ?? 0,
			Limit = args.Limit ?? 0,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			GetPageHierarchyCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetPageHierarchyCommand>(options);
			}
			catch (Exception ex) {
				return new GetPageHierarchyResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryGetHierarchy(options, out GetPageHierarchyResponse response);
			return response;
		});
	}
}

/// <summary>
/// Arguments for the <c>get-page-hierarchy</c> MCP tool.
/// </summary>
public sealed record GetPageHierarchyArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name (any variant in the replacing chain), e.g. 'UsrApplicants_FormPage'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("metadata-only")]
	[property: Description("Optional. When true, omit each schema's raw body and return chain metadata only.")]
	bool? MetadataOnly,

	[property: JsonPropertyName("offset")]
	[property: Description("Optional. Zero-based index of the first chain entry to return (root first). Default 0.")]
	int? Offset,

	[property: JsonPropertyName("limit")]
	[property: Description("Optional. Maximum number of chain entries to return; 0/omitted returns the whole chain from offset.")]
	int? Limit,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password
);
