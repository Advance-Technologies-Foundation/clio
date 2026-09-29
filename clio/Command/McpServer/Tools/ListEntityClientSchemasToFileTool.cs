using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that resolves an entity's page-role graph like <see cref="ListEntityClientSchemasTool"/> and writes the
/// full result to a local file, returning the path and the classic/freedom counts instead of the page list.
/// </summary>
/// <remarks>
/// A separate tool rather than an argument on <see cref="ListEntityClientSchemasTool"/>: the MCP safety annotations
/// are static per tool, and an optional output-file would have made every ordinary lookup write-capable. The lookup
/// itself runs through <see cref="ListEntityClientSchemasTool.Resolve"/>, so both tools return the same graph.
/// </remarks>
[McpServerToolType]
internal sealed class ListEntityClientSchemasToFileTool(
	ListEntityClientSchemasTool listTool,
	IMcpOutputFileWriter outputFileWriter) {

	internal const string ToolName = "list-entity-client-schemas-to-file";

	private static readonly JsonSerializerOptions FileSerializerOptions = BindingsModule.CreateMcpSerializerOptions();

	/// <summary>Resolves the page-role graph of an entity and writes it to a local file.</summary>
	// ReadOnly and Idempotent are FALSE because the call creates a local file and refuses an existing one, so a
	// retry against the same path fails instead of repeating the lookup.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Write the page-role graph of an entity (Classic sections, edit pages, add mini pages) to a local JSON file, " +
		"returning the path and the number of classic, freedom and unknown sections and edit pages instead of the list. " +
		"The file holds exactly the response list-entity-client-schemas returns inline; use that tool when the list " +
		"should come back inline. output-file is required, is confined to the workspace or the OS temp directory, and " +
		"must not exist yet, so a retry must use a different path.")]
	public ListEntityClientSchemasToFileResponse ResolveToFile(
		[Description("Parameters: entity-name, output-file (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ListEntityClientSchemasToFileArgs args) {
		if (args is null) {
			return ListEntityClientSchemasToFileResponse.Failure("args is required");
		}
		// Confine the path BEFORE the lookup: a rejected path must not cost the lookup first.
		if (!outputFileWriter.TryResolve(args.OutputFile, out string outputPath, out string pathError)) {
			return ListEntityClientSchemasToFileResponse.Failure(
				pathError + " Use list-entity-client-schemas when the list should be returned inline.");
		}
		ListEntityClientSchemasResponse response = listTool.Resolve(args);
		if (response is null || !response.Success) {
			return ListEntityClientSchemasToFileResponse.Failure(response?.Error ?? "list-entity-client-schemas returned no response");
		}
		// The summary is built BEFORE the write, so nothing that can fail runs after the file exists: a failure
		// then would leave a file behind that every retry to the same path refuses.
		ListEntityClientSchemasToFileResponse summary = new(
			Success: true,
			Entity: response.Entity,
			EntityUId: response.EntityUId,
			OutputFile: outputPath,
			Sections: PageKindCounts.Of(response.Sections?.Select(section => section.Kind)),
			EditPages: PageKindCounts.Of(response.EditPages?.Select(page => page.Kind)),
			Warnings: response.Warnings,
			Note: response.Note,
			Error: null);
		byte[] content = JsonSerializer.SerializeToUtf8Bytes(response, FileSerializerOptions);
		return outputFileWriter.TryWriteNew(outputPath, content, out string writeError)
			? summary
			: ListEntityClientSchemasToFileResponse.Failure(writeError);
	}
}

/// <summary>Arguments for <see cref="ListEntityClientSchemasToFileTool"/>: the lookup arguments plus the file destination.</summary>
public sealed record ListEntityClientSchemasToFileArgs(
	string EntityName,

	[property: JsonPropertyName("output-file")]
	[property: Description("Path for the JSON file, confined to the workspace or the OS temp directory. Required. The file must not already exist, so a retry must use a different path.")]
	[property: Required]
	string OutputFile
) : ListEntityClientSchemasArgs(EntityName);

/// <summary>Response of <see cref="ListEntityClientSchemasToFileTool"/>.</summary>
public sealed record ListEntityClientSchemasToFileResponse(
	[property: JsonPropertyName("success")]
	[property: Description("Whether the page-role graph was resolved and written.")]
	bool Success,

	[property: JsonPropertyName("entity")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("The resolved entity schema name.")]
	string? Entity,

	[property: JsonPropertyName("entityUId")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("UId of the entity's base schema.")]
	string? EntityUId,

	[property: JsonPropertyName("output-file")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Absolute path of the file holding the full list-entity-client-schemas response.")]
	string? OutputFile,

	[property: JsonPropertyName("sections")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Number of Classic sections in the file, in total and per kind.")]
	PageKindCounts? Sections,

	[property: JsonPropertyName("editPages")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Number of edit pages in the file, in total and per kind.")]
	PageKindCounts? EditPages,

	[property: JsonPropertyName("warnings")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Non-fatal warnings of the lookup, the same as in the file.")]
	IReadOnlyList<string>? Warnings,

	[property: JsonPropertyName("note")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Advisory note on the scope of the result and how to read an empty one, the same as in the file.")]
	string? Note,

	[property: JsonPropertyName("error")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Failure reason when success is false.")]
	string? Error) {

	/// <summary>Creates a failure response; nothing was written.</summary>
	internal static ListEntityClientSchemasToFileResponse Failure(string error) =>
		new(false, null, null, null, null, null, null, null, error);
}

/// <summary>How many pages of one list are classic, freedom or unknown.</summary>
public sealed record PageKindCounts(
	[property: JsonPropertyName("total")] int Total,
	[property: JsonPropertyName("classic")] int Classic,
	[property: JsonPropertyName("freedom")] int Freedom,
	[property: JsonPropertyName("unknown")] int Unknown) {

	/// <summary>Counts <paramref name="kinds"/>; a kind other than classic or freedom counts as unknown.</summary>
	internal static PageKindCounts Of(IEnumerable<string>? kinds) {
		List<string> all = kinds?.ToList() ?? [];
		int classic = all.Count(kind => string.Equals(kind, ListEntityClientSchemasCommand.KindClassic, StringComparison.Ordinal));
		int freedom = all.Count(kind => string.Equals(kind, ListEntityClientSchemasCommand.KindFreedom, StringComparison.Ordinal));
		return new PageKindCounts(all.Count, classic, freedom, all.Count - classic - freedom);
	}
}
