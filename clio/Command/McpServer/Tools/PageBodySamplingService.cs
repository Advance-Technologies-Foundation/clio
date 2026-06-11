using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Microsoft.Extensions.AI;
using McpServerLib = ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Strategy seam for the LLM semantic-review (sampling) step. The page write
/// tools depend on this interface so tests can substitute a recording fake and
/// observe sampling invocations (proving AC4 — "sampling unchanged after a
/// successful parse"); the live MCP transport implementation lives in
/// <see cref="PageBodySamplingService"/>.
/// </summary>
public interface IPageBodySamplingService {
	Task<PageSamplingReview> TrySamplingReviewAsync(
		McpServerLib.McpServer server,
		string schemaName,
		string body,
		string? resources,
		CancellationToken ct = default);
}

public sealed class PageBodySamplingServiceImpl : IPageBodySamplingService {
	public Task<PageSamplingReview> TrySamplingReviewAsync(
		McpServerLib.McpServer server,
		string schemaName,
		string body,
		string? resources,
		CancellationToken ct = default) =>
		PageBodySamplingService.TrySamplingReviewAsync(server, schemaName, body, resources, ct);
}

internal static class PageBodySamplingService {

	internal const string SystemPrompt =
		"You are reviewing a Creatio Freedom UI page body (JavaScript) before it is saved to Creatio.\n\n" +
		"NAME RESOLUTION MODEL (applies to handler `request` values, converter names, and validator names):\n" +
		"Every reference resolves through one of THREE tiers:\n" +
		"  (a) Platform built-in — uses the `crt.*` prefix (e.g. `crt.SaveRecordRequest`, `crt.InvertBooleanValue`). " +
		"Built-ins are provided by Creatio and MUST NOT be declared on the page.\n" +
		"  (b) Page-local — declared inside the page body in SCHEMA_HANDLERS, SCHEMA_CONVERTERS, or SCHEMA_VALIDATORS. " +
		"Non-`crt.*` names (typically `usr.*`) may live here.\n" +
		"  (c) Remote global module — registered globally by a separately deployed AMD module. " +
		"These are NOT imported via the page's `define([...])` dependency array and leave NO trace in the page body. " +
		"You cannot detect them from the body alone.\n\n" +
		"Because of tier (c), a non-`crt.*` reference that is missing from the corresponding SCHEMA section is " +
		"AMBIGUOUS — it could be a typo, or a legitimate global registration. Treat such cases as `warnings`, " +
		"never as `issues`. Phrase the warning as: \"<name> not declared on page — verify it is provided by a remote module or fix the typo\".\n\n" +
		"Check ONLY the following semantic concerns:\n" +
		"1. Handler references: every handler `request` value used in SCHEMA_VIEW_CONFIG_DIFF should resolve via " +
		"tier (a), (b), or (c). If it is not `crt.*` and has no matching entry in SCHEMA_HANDLERS (matched by the " +
		"`request` field, not by function name), emit a WARNING per the rule above — do NOT mark as an issue.\n" +
		"2. Converter references: every converter name used in SCHEMA_VIEW_CONFIG_DIFF binding expressions " +
		"(after the pipe `|`) should resolve via tier (a), (b), or (c). If it is not `crt.*` and is not declared " +
		"in SCHEMA_CONVERTERS, emit a WARNING per the rule above — do NOT mark as an issue.\n" +
		"3. Validator references: every validator `type` value used in attribute `validators` bindings " +
		"(inside SCHEMA_VIEW_MODEL_CONFIG_DIFF or viewModelConfig) should resolve via tier (a), (b), or (c). " +
		"If it is not `crt.*` and has no matching entry in SCHEMA_VALIDATORS, emit a WARNING per the rule above — " +
		"do NOT mark as an issue.\n" +
		"4. Type mismatch: when a control's component type implies a data kind (e.g. crt.DateTimePicker → date, " +
		"crt.NumberInput → number, crt.ComboBox → lookup), check that the bound attribute name does not " +
		"obviously contradict it (e.g. crt.DateTimePicker bound to $UsrFullName is suspicious)\n" +
		"5. Redundant resources: if a RESOURCES section is present, check each key against the body. " +
		"When an attribute with a matching or similar name is bound to a data source column via " +
		"`modelConfig.path` (e.g. \"PDS.UsrStatus\"), the platform auto-provides the caption — " +
		"registering it is unnecessary UNLESS the value is clearly a custom override (not just the " +
		"column name humanized). Warn for likely redundant entries.\n" +
		"6. Missing resources: for any component property that uses a `$Resources.Strings.X` binding " +
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
		"Custom (non-`crt.*`) handler request types and converter names are only valid when they are " +
		"registered globally by a separately deployed remote AMD module, which leaves no trace in the body.\n" +
		"Check ONLY the following semantic issues:\n" +
		"1. Type mismatch: when a control's component type implies a data kind (e.g. crt.DateTimePicker → date, " +
		"crt.NumberInput → number, crt.ComboBox → lookup), check that the bound attribute name does not " +
		"obviously contradict it (e.g. crt.DateTimePicker bound to $UsrFullName is suspicious)\n" +
		"2. Unresolved custom references: any non-`crt.*` converter name (used after `|` in a binding " +
		"expression) or non-`crt.*` request type (used in a `clicked` binding or in viewModelConfigDiff) " +
		"is ambiguous — it may be provided by a remote module, or it may be a typo. Emit a WARNING per " +
		"reference: \"<name> not provided by Creatio OOTB — verify it is registered by a remote module or fix the typo\". " +
		"Do NOT mark these as issues.\n" +
		"3. Redundant resources: if a RESOURCES section is present, check each key against the body. " +
		"When an attribute with a matching or similar name is bound to a data source column via " +
		"`modelConfig.path` (e.g. \"PDS.UsrStatus\"), the platform auto-provides the caption — " +
		"registering it is unnecessary UNLESS the value is clearly a custom override (not just the " +
		"column name humanized). Warn for likely redundant entries.\n" +
		"4. Missing resources: for any component property that uses a `$Resources.Strings.X` binding " +
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
