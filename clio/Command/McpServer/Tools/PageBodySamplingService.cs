using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Microsoft.Extensions.AI;
using McpServerLib = ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

internal static class PageBodySamplingService {

	internal const string SystemPrompt =
		"You are reviewing a Creatio Freedom UI page body (JavaScript) before it is saved to Creatio.\n" +
		"Check ONLY the following semantic issues:\n" +
		"1. Handler references: every handler `request` value used in SCHEMA_VIEW_CONFIG_DIFF must have " +
		"a matching entry in SCHEMA_HANDLERS with the same `request` field — handlers are matched by their " +
		"`request` property, not by function name\n" +
		"2. Converter references: every non-crt.* converter name used in SCHEMA_VIEW_CONFIG_DIFF binding " +
		"expressions (after the pipe `|`) must be declared in SCHEMA_CONVERTERS — platform crt.* converters " +
		"are built-in and must NOT be declared\n" +
		"3. Type mismatch: when a control's component type implies a data kind (e.g. crt.DateTimePicker → date, " +
		"crt.NumberInput → number, crt.ComboBox → lookup), check that the bound attribute name does not " +
		"obviously contradict it (e.g. crt.DateTimePicker bound to $UsrFullName is suspicious)\n" +
		"4. Redundant resources: if a RESOURCES section is present, check each key against the body. " +
		"When an attribute with a matching or similar name is bound to a data source column via " +
		"`modelConfig.path` (e.g. \"PDS.UsrStatus\"), the platform auto-provides the caption — " +
		"registering it is unnecessary UNLESS the value is clearly a custom override (not just the " +
		"column name humanized). Warn for likely redundant entries.\n" +
		"5. Missing resources: for any component property that uses a `$Resources.Strings.X` binding " +
		"where X does NOT correspond to a datasource-bound attribute (no matching `modelConfig.path` " +
		"like \"PDS.ColumnName\"), verify that key X appears in the RESOURCES section. " +
		"If missing, flag as an issue — the property will render blank at runtime. " +
		"Skip this check when X is a view model attribute that is bound to a data source column " +
		"via `modelConfig.path` (e.g. `PDS_UsrName` with path `PDS.UsrName`, or `UsrLabel` with " +
		"path `PDS.UsrFullName`) — the platform auto-provides labels for DS-bound attributes.\n\n" +
		"Respond with ONLY this JSON (no markdown fences, no explanation):\n" +
		"{\"ok\":true,\"issues\":[],\"warnings\":[]}\n\n" +
		"Set ok=false only for issues that will prevent the page from working. Use warnings for minor concerns.";

	internal const string MobileSystemPrompt =
		"You are reviewing a Creatio Freedom UI mobile page body (plain JSON) before it is saved to Creatio.\n" +
		"The body is a JSON object with optional top-level arrays: viewConfigDiff (or viewConfig), " +
		"viewModelConfigDiff (or viewModelConfig), modelConfigDiff (or modelConfig).\n" +
		"Check ONLY the following semantic issues:\n" +
		"1. Type mismatch: when a control's component type implies a data kind (e.g. crt.DateTimePicker → date, " +
		"crt.NumberInput → number, crt.ComboBox → lookup), check that the bound attribute name does not " +
		"obviously contradict it (e.g. crt.DateTimePicker bound to $UsrFullName is suspicious)\n" +
		"2. Redundant resources: if a RESOURCES section is present, check each key against the body. " +
		"When an attribute with a matching or similar name is bound to a data source column via " +
		"`modelConfig.path` (e.g. \"PDS.UsrStatus\"), the platform auto-provides the caption — " +
		"registering it is unnecessary UNLESS the value is clearly a custom override (not just the " +
		"column name humanized). Warn for likely redundant entries.\n" +
		"3. Missing resources: for any component property that uses a `$Resources.Strings.X` binding " +
		"where X does NOT correspond to a datasource-bound attribute (no matching `modelConfig.path` " +
		"like \"PDS.ColumnName\"), verify that key X appears in the RESOURCES section. " +
		"If missing, flag as an issue — the property will render blank at runtime. " +
		"Skip this check when X is a view model attribute that is bound to a data source column " +
		"via `modelConfig.path` (e.g. `PDS_UsrName` with path `PDS.UsrName`, or `UsrLabel` with " +
		"path `PDS.UsrFullName`) — the platform auto-provides labels for DS-bound attributes.\n\n" +
		"Respond with ONLY this JSON (no markdown fences, no explanation):\n" +
		"{\"ok\":true,\"issues\":[],\"warnings\":[]}\n\n" +
		"Set ok=false only for issues that will prevent the page from working. Use warnings for minor concerns.";

	internal static async Task<PageSamplingReview> TrySamplingReviewAsync(
		McpServerLib.McpServer server, string schemaName, string body,
		string? resources, CancellationToken ct = default) {
		try {
			string userContent = string.IsNullOrWhiteSpace(resources)
				? $"Schema: {schemaName}\n\n{body}"
				: $"Schema: {schemaName}\n\n{body}\n\n--- RESOURCES ---\n{resources}";
			var messages = new List<ChatMessage> {
				new(ChatRole.User, userContent)
			};
			var chatOptions = new ChatOptions {
				Instructions = PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile ? MobileSystemPrompt : SystemPrompt,
				MaxOutputTokens = 500
			};
			ChatResponse response = await server.SampleAsync(messages, chatOptions, null, ct);
			return ParseSamplingResponse(response.Text ?? string.Empty);
		} catch {
			return new PageSamplingReview { Skipped = true };
		}
	}

	internal static PageSamplingReview ParseSamplingResponse(string text) {
		try {
			text = text.Trim();
			if (text.StartsWith("```", System.StringComparison.Ordinal)) {
				int end = text.LastIndexOf("```", System.StringComparison.Ordinal);
				text = text[3..end].Trim();
				if (text.StartsWith("json", System.StringComparison.OrdinalIgnoreCase))
					text = text[4..].Trim();
			}
			using JsonDocument doc = JsonDocument.Parse(text);
			bool ok = doc.RootElement.TryGetProperty("ok", out JsonElement okEl) && okEl.GetBoolean();
			List<string> issues = ParseStringArray(doc.RootElement, "issues");
			List<string> warnings = ParseStringArray(doc.RootElement, "warnings");
			return new PageSamplingReview {
				Ok = ok,
				Issues = issues?.Count > 0 ? issues : null,
				Warnings = warnings?.Count > 0 ? warnings : null
			};
		} catch {
			return new PageSamplingReview { Skipped = true };
		}
	}

	internal static List<string> ParseStringArray(JsonElement root, string propertyName) {
		if (!root.TryGetProperty(propertyName, out JsonElement el) || el.ValueKind != JsonValueKind.Array)
			return [];
		return el.EnumerateArray()
			.Select(e => e.GetString())
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.ToList();
	}
}
