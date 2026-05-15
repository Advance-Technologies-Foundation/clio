namespace Clio.Command;

using System;
using JsonhCs;
using Newtonsoft.Json.Linq;

/// <summary>
/// Parses Freedom UI page schema bodies into structured sections.
/// </summary>
public interface IPageSchemaBodyParser {
	/// <summary>
	/// Parses the supplied schema body.
	/// </summary>
	/// <param name="body">Raw schema body.</param>
	/// <returns>Parsed page schema body sections.</returns>
	PageParsedSchemaBody Parse(string body);
}

internal sealed class PageSchemaBodyParser : IPageSchemaBodyParser {
	/// <inheritdoc />
	public PageParsedSchemaBody Parse(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			throw new InvalidOperationException("Page schema body is empty");
		}

		return SchemaValidationService.IsLikelyMobileBody(body)
			? ParseMobileJsonBody(body)
			: ParseAmdBody(body);
	}

	/// <summary>
	/// Parses a mobile page body — plain JSON with top-level <c>viewConfigDiff</c>,
	/// <c>viewModelConfigDiff</c>, and <c>modelConfigDiff</c> arrays.
	/// </summary>
	private static PageParsedSchemaBody ParseMobileJsonBody(string body) {
		JObject json;
		try {
			json = JObject.Parse(body);
		} catch (Exception ex) {
			throw new InvalidOperationException(
				$"Failed to parse mobile page body as JSON: {ex.Message}", ex);
		}

		return new PageParsedSchemaBody {
			ViewConfigDiff = json["viewConfigDiff"] as JArray ?? new JArray(),
			ViewModelConfigDiff = json["viewModelConfigDiff"] as JArray ?? new JArray(),
			ModelConfigDiff = json["modelConfigDiff"] as JArray ?? new JArray(),
			ViewModelConfig = new JObject(),
			ModelConfig = new JObject(),
			Deps = "[]",
			Args = "()",
			Handlers = "[]",
			Converters = "{}",
			Validators = "{}"
		};
	}

	/// <summary>
	/// Parses an AMD-wrapped web page body using marker-based section extraction.
	/// </summary>
	private static PageParsedSchemaBody ParseAmdBody(string body) {
		return new PageParsedSchemaBody {
			ViewConfigDiff = ParseJsonSection(body, new JArray(), "SCHEMA_VIEW_CONFIG_DIFF", "SCHEMA_DIFF"),
			ViewModelConfig = ParseJsonSection(body, new JObject(), "SCHEMA_VIEW_MODEL_CONFIG"),
			ViewModelConfigDiff = ParseJsonSection(body, new JArray(), "SCHEMA_VIEW_MODEL_CONFIG_DIFF"),
			ModelConfig = ParseJsonSection(body, new JObject(), "SCHEMA_MODEL_CONFIG"),
			ModelConfigDiff = ParseJsonSection(body, new JArray(), "SCHEMA_MODEL_CONFIG_DIFF"),
			Deps = ReadRawSection(body, "[]", "SCHEMA_DEPS"),
			Args = ReadRawSection(body, "()", "SCHEMA_ARGS"),
			Handlers = ReadRawSection(body, "[]", "SCHEMA_HANDLERS", "SCHEMA_HANDLERS_CONFIG"),
			Converters = ReadRawSection(body, "{}", "SCHEMA_CONVERTERS"),
			Validators = ReadRawSection(body, "{}", "SCHEMA_VALIDATORS")
		};
	}

	private static JToken ParseJsonSection(string body, JToken fallback, params string[] markers) {
		if (!PageSchemaSectionReader.TryRead(body, out string content, markers)) {
			return fallback.DeepClone();
		}

		try {
			var element = JsonhReader.ParseElement(content).Value;
			string rawJson = element.GetRawText();
			return string.IsNullOrWhiteSpace(rawJson) ? fallback.DeepClone() : JToken.Parse(rawJson);
		}
		catch (Exception ex) {
			throw new InvalidOperationException(
				$"Failed to parse schema section '{markers[0]}': {ex.Message}",
				ex);
		}
	}

	private static string ReadRawSection(string body, string fallback, params string[] markers) {
		return PageSchemaSectionReader.TryRead(body, out string content, markers)
			? content.Trim()
			: fallback;
	}
}
