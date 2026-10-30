using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// The raw database reads the process-designer E2E fixtures make to assert on what a save STORED rather than on what
/// describe reports: an element's node in <c>SysSchema.MetaData</c>, its <c>SysLocalizableValue</c> rows, and the two
/// SelectQuery routes those reads go through.
/// <para>The database and not describe, on purpose: describe is not a capture read path, and a preservation claim is
/// about the stored bytes. A node is located by its <c>UId</c> and returned with <see cref="JsonElement.GetRawText"/>,
/// so a comparison is of the stored text, not of a re-serialization. The metadata goes through
/// <c>execute-esq-to-file</c> because a process's metadata can exceed the inline response budget; DataService rather
/// than SQL for the reason the caption reads give in <c>SubProcessElementToolE2ETests</c>.</para>
/// </summary>
internal static class ProcessDesignerStoredReads {

	/// <summary>
	/// The raw JSON of the element with <paramref name="elementUId"/> in each stored <c>SysSchema.MetaData</c> row of
	/// <paramref name="schemaName"/>, ordered by row id, each prefixed with its row id (<c>&lt;id&gt;: &lt;node&gt;</c>)
	/// so a comparison of two reads also compares which rows carry the element.
	/// </summary>
	internal static async Task<IReadOnlyList<string>> ReadElementNodesAsync(ProcessDesignerArrangeContext context,
			string schemaName, string elementUId) =>
		(await ReadStoredElementNodesAsync(context, schemaName, elementUId))
			.Select(node => $"{node.RowId}: {node.Node}")
			.ToList();

	/// <summary>
	/// The raw JSON of the element with <paramref name="elementUId"/> in the LAST stored <c>SysSchema.MetaData</c> row
	/// of <paramref name="schemaName"/> (by row id) that carries it, with no row-id prefix, so it parses as JSON.
	/// </summary>
	internal static async Task<string> ReadLastElementNodeAsync(ProcessDesignerArrangeContext context,
			string schemaName, string elementUId) =>
		(await ReadStoredElementNodesAsync(context, schemaName, elementUId)).Last().Node;

	/// <summary>
	/// The stored <c>SysLocalizableValue</c> rows of one element - keys <c>BaseElements.&lt;element&gt;.*</c>, every
	/// culture - as sorted <c>key | culture | value</c> lines. Read from the database, not describe: every read
	/// re-materializes captions, so describe can show a caption the database does not hold.
	/// </summary>
	internal static async Task<IReadOnlyList<string>> ReadElementResourceRowsAsync(
			ProcessDesignerArrangeContext context, string schemaName, string elementName) {
		JsonElement rows = await SelectToFileAsync(context, SelectQueryHelper.BuildSelectQuery("SysLocalizableValue",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Key", "Key"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SysCulture.Name", "Culture"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Value", "Value")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("SysSchema.Name", schemaName,
					SelectQueryHelper.TextDataValueType)
			]), $"the resource rows of '{schemaName}'");
		string prefix = $"BaseElements.{elementName}.";
		return rows.EnumerateArray()
			.Select(row => (Key: TextOf(row, "Key"), Culture: TextOf(row, "Culture"), Value: TextOf(row, "Value")))
			.Where(row => row.Key.StartsWith(prefix, StringComparison.Ordinal))
			.Select(row => $"{row.Key} | {row.Culture} | {row.Value}")
			.OrderBy(line => line, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>The names of the environment's active cultures (<c>SysCulture.Active</c>), e.g. <c>en-US</c>.</summary>
	internal static async Task<IReadOnlySet<string>> ReadActiveCultureNamesAsync(ProcessDesignerArrangeContext context) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysCulture",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Active", "Active")
			],
			[]), "the environment's cultures");
		HashSet<string> active = rows.EnumerateArray()
			.Where(row => row.TryGetProperty("Active", out JsonElement flag) && flag.ValueKind == JsonValueKind.True)
			.Select(row => TextOf(row, "Name"))
			.ToHashSet(StringComparer.Ordinal);
		active.Should().NotBeEmpty(because: "an environment has at least one active culture, or the read saw nothing");
		return active;
	}

	/// <summary>
	/// The resource rows (<c>key | culture | value</c>) without the template parameter captions
	/// (<c>BaseElements.&lt;El&gt;.Parameters.&lt;P&gt;[.&lt;Item&gt;].Caption</c>) of the cultures that are not active, in
	/// their order: what the first builder save after the designer or a builder create wrote an element keeps. Both
	/// write those captions in every culture; the platform save keeps only the active cultures' rows.
	/// </summary>
	internal static IReadOnlyList<string> WithoutInactiveTemplateCaptions(IReadOnlyList<string> rows,
			IReadOnlySet<string> activeCultures) =>
		rows.Where(row => {
			string[] parts = row.Split(" | ");
			if (parts.Length < 2 || activeCultures.Contains(parts[1])) {
				return true;
			}
			string key = parts[0];
			bool templateCaption = key.StartsWith("BaseElements.", StringComparison.Ordinal)
				&& key.Contains(".Parameters.", StringComparison.Ordinal)
				&& key.EndsWith(".Caption", StringComparison.Ordinal);
			return !templateCaption;
		}).ToList();

	/// <summary>
	/// The rows <paramref name="after"/> lost and gained against <paramref name="before"/>, in full: an assertion's own
	/// listing stops after 32 items, which hides the difference in a long resource list.
	/// </summary>
	internal static string RowDifference(IReadOnlyList<string> before, IReadOnlyList<string> after) {
		string removed = string.Join("; ", before.Except(after, StringComparer.Ordinal));
		string added = string.Join("; ", after.Except(before, StringComparer.Ordinal));
		return $"{before.Count} rows before, {after.Count} after. REMOVED: [{removed}] ADDED: [{added}]";
	}

	/// <summary>Runs a small SelectQuery through <c>execute-esq</c> and returns its rows array.</summary>
	internal static async Task<JsonElement> SelectInlineAsync(ProcessDesignerArrangeContext context, object query,
			string what) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["query"] = JsonSerializer.SerializeToElement(query)
			});
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(result);
		response.Success.Should().BeTrue(because: $"{what} must be readable for this case to measure anything; the read "
			+ "failed with: {0}", response.Error);
		JsonElement rows = response.Rows.GetValueOrDefault();
		rows.ValueKind.Should().Be(JsonValueKind.Array,
			because: "a SelectQuery answers with a rows array; anything else is a shape this read cannot use");
		return rows.Clone();
	}

	/// <summary>Runs a SelectQuery through <c>execute-esq-to-file</c> and returns the rows array the file holds.</summary>
	internal static async Task<JsonElement> SelectToFileAsync(ProcessDesignerArrangeContext context, object query,
			string what) {
		string directory = ScratchDirectory.CreateUnderPhysicalTemp("clio-process-designer-e2e");
		try {
			string outputFile = Path.Combine(directory, "rows.json");
			CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ExecuteEsqToFileTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["query"] = JsonSerializer.SerializeToElement(query),
					["output-file"] = outputFile
				});
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(result);
			response.Success.Should().BeTrue(because: $"{what} must be readable for this case to measure anything; the "
				+ "read failed with: {0}", response.Error);
			using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(outputFile));
			document.RootElement.ValueKind.Should().Be(JsonValueKind.Array,
				because: "the file holds the rows array of the SelectQuery");
			return document.RootElement.Clone();
		} finally {
			ScratchDirectory.TryDelete(directory);
		}
	}

	/// <summary>
	/// The metadata JSON text of a <c>SysSchema.MetaData</c> value as DataService returns a binary column: a base64
	/// string, an array of bytes, or - tolerated - the JSON text itself. A UTF-8 byte-order mark is dropped.
	/// </summary>
	internal static string DecodeMetaData(JsonElement value) {
		byte[]? bytes = null;
		if (value.ValueKind == JsonValueKind.String) {
			string text = value.GetString() ?? string.Empty;
			if (text.TrimStart().StartsWith('{')) {
				return text;
			}
			bytes = Convert.FromBase64String(text);
		} else if (value.ValueKind == JsonValueKind.Array) {
			bytes = value.EnumerateArray().Select(item => item.GetByte()).ToArray();
		}
		bytes.Should().NotBeNull(because: "SysSchema.MetaData must come back as binary content; the value read was: {0}",
			value.GetRawText());
		return Encoding.UTF8.GetString(bytes!).TrimStart('﻿');
	}

	/// <summary>The raw text of the first object whose <c>UId</c> is <paramref name="uid"/>, depth first.</summary>
	internal static string? FindNodeByUId(JsonElement node, Guid uid) {
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				if (node.TryGetProperty("UId", out JsonElement id) && id.ValueKind == JsonValueKind.String
						&& Guid.TryParse(id.GetString(), out Guid parsed) && parsed == uid) {
					return node.GetRawText();
				}
				foreach (JsonProperty property in node.EnumerateObject()) {
					string? found = FindNodeByUId(property.Value, uid);
					if (found != null) {
						return found;
					}
				}
				return null;
			case JsonValueKind.Array:
				foreach (JsonElement item in node.EnumerateArray()) {
					string? found = FindNodeByUId(item, uid);
					if (found != null) {
						return found;
					}
				}
				return null;
			default:
				return null;
		}
	}

	/// <summary>A row value as text: a string as is, a lookup by its display value, anything else raw.</summary>
	internal static string TextOf(JsonElement row, string column) {
		if (!row.TryGetProperty(column, out JsonElement value)) {
			return string.Empty;
		}
		return value.ValueKind switch {
			JsonValueKind.String => value.GetString() ?? string.Empty,
			JsonValueKind.Object when value.TryGetProperty("displayValue", out JsonElement display) => display.ToString(),
			JsonValueKind.Null => string.Empty,
			_ => value.GetRawText()
		};
	}

	// Every stored row of the schema that carries the element, in row-id order. Fails the test when none does, or the
	// comparison built on the read would compare nothing.
	private static async Task<IReadOnlyList<(string RowId, string Node)>> ReadStoredElementNodesAsync(
			ProcessDesignerArrangeContext context, string schemaName, string elementUId) {
		JsonElement rows = await SelectToFileAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id", OrderDirection: 1, OrderPosition: 0),
				new SelectQueryHelper.SelectQueryColumnDefinition("MetaData", "MetaData")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "ProcessSchemaManager",
					SelectQueryHelper.TextDataValueType)
			]), $"the SysSchema rows of '{schemaName}'");
		List<(string RowId, string Node)> nodes = [];
		foreach (JsonElement row in rows.EnumerateArray()) {
			string metadata = DecodeMetaData(row.GetProperty("MetaData"));
			using JsonDocument document = JsonDocument.Parse(metadata);
			string? node = FindNodeByUId(document.RootElement, Guid.Parse(elementUId));
			if (node != null) {
				nodes.Add(($"{row.GetProperty("Id")}", node));
			}
		}
		nodes.Should().NotBeEmpty(
			because: $"the element {elementUId} must be found in the stored metadata of '{schemaName}', or the "
				+ "comparison would compare nothing");
		return nodes;
	}

}
