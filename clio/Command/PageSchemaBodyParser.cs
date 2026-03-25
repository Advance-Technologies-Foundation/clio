namespace Clio.Command;

using System;
using HjsonSharp;
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
	public PageParsedSchemaBody Parse(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			throw new InvalidOperationException("Page schema body is empty");
		}

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
			var element = CustomJsonReader.ParseElement(content, CustomJsonReaderOptions.Hjson, true).Value;
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
