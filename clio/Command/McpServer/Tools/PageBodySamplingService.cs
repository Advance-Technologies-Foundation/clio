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
		"1. Handler references: every handler name used in SCHEMA_VIEW_CONFIG_DIFF must be defined in SCHEMA_HANDLERS\n" +
		"2. Converter references: every converter name used in SCHEMA_VIEW_CONFIG_DIFF must be defined in SCHEMA_CONVERTERS\n" +
		"3. Validator references: every validator name used in SCHEMA_VIEW_CONFIG_DIFF must be defined in SCHEMA_VALIDATORS\n" +
		"4. Model path consistency: attribute names in view config must match definitions in SCHEMA_VIEW_MODEL_CONFIG or SCHEMA_VIEW_MODEL_CONFIG_DIFF\n\n" +
		"Respond with ONLY this JSON (no markdown fences, no explanation):\n" +
		"{\"ok\":true,\"issues\":[],\"warnings\":[]}\n\n" +
		"Set ok=false only for issues that will prevent the page from working. Use warnings for minor concerns.";

	internal static async Task<PageSamplingReview> TrySamplingReviewAsync(
		McpServerLib.McpServer server, string schemaName, string body, CancellationToken ct) {
		try {
			var messages = new List<ChatMessage> {
				new(ChatRole.User, $"Schema: {schemaName}\n\n{body}")
			};
			var chatOptions = new ChatOptions {
				Instructions = SystemPrompt,
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
			return null;
		return el.EnumerateArray()
			.Select(e => e.GetString())
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.ToList();
	}
}
