namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class SchemaValidationService
{
	public static readonly string[] RequiredMarkerNames = {
		"SCHEMA_DEPS",
		"SCHEMA_ARGS",
		"SCHEMA_VIEW_CONFIG_DIFF",
		"SCHEMA_HANDLERS",
		"SCHEMA_CONVERTERS",
		"SCHEMA_VALIDATORS"
	};

	public static readonly string[][] AlternateMarkerPairs = {
		new[] { "SCHEMA_VIEW_MODEL_CONFIG_DIFF", "SCHEMA_VIEW_MODEL_CONFIG" },
		new[] { "SCHEMA_MODEL_CONFIG_DIFF", "SCHEMA_MODEL_CONFIG" }
	};

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

	public static string BuildMarkerPattern(string markerName) {
		return @"/\*\*" + Regex.Escape(markerName) + @"\*/(.*?)/\*\*" + Regex.Escape(markerName) + @"\*/";
	}

	public static SchemaValidationResult ValidateMarkerIntegrity(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			result.IsValid = false;
			result.Errors.Add("JS body is null or empty.");
			return result;
		}
		foreach (string markerName in RequiredMarkerNames) {
			string pattern = BuildMarkerPattern(markerName);
			if (!Regex.IsMatch(jsBody, pattern, RegexOptions.Singleline, RegexTimeout)) {
				result.IsValid = false;
				result.Errors.Add(markerName);
			}
		}
		foreach (string[] pair in AlternateMarkerPairs) {
			string pattern0 = BuildMarkerPattern(pair[0]);
			string pattern1 = BuildMarkerPattern(pair[1]);
			bool hasFirst = Regex.IsMatch(jsBody, pattern0, RegexOptions.Singleline, RegexTimeout);
			bool hasSecond = Regex.IsMatch(jsBody, pattern1, RegexOptions.Singleline, RegexTimeout);
			if (!hasFirst && !hasSecond) {
				result.IsValid = false;
				result.Errors.Add($"{pair[0]} or {pair[1]}");
			}
		}
		return result;
	}

	private static readonly string[] ArrayMarkers = {
		"SCHEMA_VIEW_CONFIG_DIFF",
		"SCHEMA_DIFF",
		"SCHEMA_VIEW_MODEL_CONFIG_DIFF",
		"SCHEMA_MODEL_CONFIG_DIFF",
		"SCHEMA_HANDLERS",
		"SCHEMA_HANDLERS_CONFIG",
		"SCHEMA_DEPS"
	};

	private static readonly string[] ObjectMarkers = {
		"SCHEMA_VIEW_MODEL_CONFIG",
		"SCHEMA_MODEL_CONFIG",
		"SCHEMA_CONVERTERS",
		"SCHEMA_VALIDATORS"
	};

	public static SchemaValidationResult ValidateMarkerContent(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			result.IsValid = false;
			result.Errors.Add("JS body is null or empty.");
			return result;
		}
		foreach (string marker in ArrayMarkers) {
			if (!PageSchemaSectionReader.TryRead(jsBody, out string content, marker)) {
				continue;
			}
			if (!TryParseHjson(content, marker, result)) {
				break;
			}
		}
		foreach (string marker in ObjectMarkers) {
			if (!PageSchemaSectionReader.TryRead(jsBody, out string content, marker)) {
				continue;
			}
			if (!TryParseHjson(content, marker, result)) {
				break;
			}
		}
		return result;
	}

	private static bool TryParseHjson(string content, string marker, SchemaValidationResult result) {
		try {
			string cleaned = Regex.Replace(content, @",(\s*[\]\}])", "$1");
			System.Text.Json.JsonDocument.Parse(cleaned);
			return true;
		} catch (Exception ex) {
			result.IsValid = false;
			result.Errors.Add($"Invalid JSON in {marker}: {ex.Message}");
			return false;
		}
	}

	public static SchemaValidationResult ValidateColumnBindings(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, "SCHEMA_VIEW_CONFIG_DIFF", "SCHEMA_DIFF")) {
			return result;
		}
		var columnCodes = ExtractDataTableColumnCodes(vcdContent);
		if (columnCodes.Count == 0) {
			return result;
		}
		HashSet<string> boundPaths = new(StringComparer.OrdinalIgnoreCase);
		if (PageSchemaSectionReader.TryRead(jsBody, out string vmContent, "SCHEMA_VIEW_MODEL_CONFIG_DIFF")) {
			CollectModelPaths(vmContent, boundPaths);
		}
		if (PageSchemaSectionReader.TryRead(jsBody, out string vmContent2, "SCHEMA_VIEW_MODEL_CONFIG")) {
			CollectModelPaths(vmContent2, boundPaths);
		}
		foreach (string code in columnCodes) {
			string expectedPath = code.Replace("_", ".", StringComparison.Ordinal);
			if (!boundPaths.Contains(expectedPath)) {
				result.Errors.Add($"DataTable column '{code}' has no matching binding (expected path '{expectedPath}')");
			}
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	private static List<string> ExtractDataTableColumnCodes(string vcdContent) {
		var codes = new List<string>();
		try {
			string cleaned = Regex.Replace(vcdContent, @",(\s*[\]\}])", "$1");
			using var doc = System.Text.Json.JsonDocument.Parse(cleaned);
			foreach (var item in doc.RootElement.EnumerateArray()) {
				if (!item.TryGetProperty("name", out var nameEl) || nameEl.GetString() != "DataTable") {
					continue;
				}
				if (!item.TryGetProperty("values", out var values) ||
				    !values.TryGetProperty("columns", out var columns)) {
					continue;
				}
				foreach (var col in columns.EnumerateArray()) {
					if (col.TryGetProperty("code", out var codeEl)) {
						string code = codeEl.GetString();
						if (!string.IsNullOrEmpty(code) && code.StartsWith("PDS_", StringComparison.Ordinal)) {
							codes.Add(code);
						}
					}
				}
			}
		} catch {
		}
		return codes;
	}

	private static void CollectModelPaths(string markerContent, HashSet<string> paths) {
		try {
			string cleaned = Regex.Replace(markerContent, @",(\s*[\]\}])", "$1");
			CollectPathsRecursive(cleaned, paths);
		} catch {
		}
	}

	private static void CollectPathsRecursive(string json, HashSet<string> paths) {
		using var doc = System.Text.Json.JsonDocument.Parse(json);
		CollectPathsFromElement(doc.RootElement, paths);
	}

	private static void CollectPathsFromElement(System.Text.Json.JsonElement element, HashSet<string> paths) {
		if (element.ValueKind == System.Text.Json.JsonValueKind.Object) {
			if (element.TryGetProperty("modelConfig", out var mc) &&
			    mc.TryGetProperty("path", out var pathEl) &&
			    pathEl.ValueKind == System.Text.Json.JsonValueKind.String) {
				paths.Add(pathEl.GetString());
			}
			foreach (var prop in element.EnumerateObject()) {
				CollectPathsFromElement(prop.Value, paths);
			}
		} else if (element.ValueKind == System.Text.Json.JsonValueKind.Array) {
			foreach (var item in element.EnumerateArray()) {
				CollectPathsFromElement(item, paths);
			}
		}
	}

	public static SchemaValidationResult ValidateJsSyntax(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			result.IsValid = false;
			result.Errors.Add("JS body is null or empty.");
			return result;
		}
		var stack = new Stack<(char bracket, int position)>();
		int i = 0;
		int length = jsBody.Length;
		while (i < length) {
			char c = jsBody[i];
			int next = TrySkipToken(jsBody, i, length, c, stack, result);
			if (!result.IsValid) {
				return result;
			}
			if (next != i) {
				i = next;
				continue;
			}
			i++;
		}
		if (stack.Count > 0) {
			var (bracket, pos) = stack.Pop();
			result.IsValid = false;
			result.Errors.Add($"Unclosed '{bracket}' at position {pos}.");
		}
		return result;
	}

	private static int TrySkipToken(string jsBody, int i, int length, char c,
		Stack<(char bracket, int position)> stack, SchemaValidationResult result) {
		if (c == '/' && i + 1 < length) {
			if (jsBody[i + 1] == '/') {
				return SkipLineComment(jsBody, i, length);
			}
			if (jsBody[i + 1] == '*') {
				return SkipBlockComment(jsBody, i, length, result);
			}
		}
		if (c == '\'' || c == '"') {
			return SkipStringLiteral(jsBody, i, length, c, result);
		}
		if (c == '`') {
			return SkipTemplateLiteral(jsBody, i, length, result);
		}
		if (c == '(' || c == '{' || c == '[') {
			stack.Push((c, i));
			return i + 1;
		}
		if (c == ')' || c == '}' || c == ']') {
			return ProcessClosingBracket(c, i, stack, result);
		}
		return i;
	}

	private static int SkipLineComment(string jsBody, int i, int length) {
		i += 2;
		while (i < length && jsBody[i] != '\n' && jsBody[i] != '\r') {
			i++;
		}
		return i;
	}

	private static int SkipBlockComment(string jsBody, int i, int length, SchemaValidationResult result) {
		int commentStart = i;
		i += 2;
		while (i + 1 < length) {
			if (jsBody[i] == '*' && jsBody[i + 1] == '/') {
				return i + 2;
			}
			i++;
		}
		result.IsValid = false;
		result.Errors.Add($"Unterminated block comment at position {commentStart}.");
		return i;
	}

	private static int SkipStringLiteral(string jsBody, int i, int length, char quote, SchemaValidationResult result) {
		int stringStart = i;
		i++;
		while (i < length) {
			if (jsBody[i] == '\\') {
				i += 2;
				continue;
			}
			if (jsBody[i] == quote) {
				return i + 1;
			}
			if (jsBody[i] == '\n' || jsBody[i] == '\r') {
				result.IsValid = false;
				result.Errors.Add($"Unterminated string literal at position {stringStart}.");
				return i;
			}
			i++;
		}
		return i;
	}

	private static int SkipTemplateLiteral(string jsBody, int i, int length, SchemaValidationResult result) {
		int templateStart = i;
		i++;
		while (i < length) {
			if (jsBody[i] == '\\') {
				i += 2;
				continue;
			}
			if (jsBody[i] == '`') {
				return i + 1;
			}
			i++;
		}
		if (i > length || (i == length && jsBody[length - 1] != '`')) {
			result.IsValid = false;
			result.Errors.Add($"Unterminated template literal at position {templateStart}.");
		}
		return i;
	}

	private static int ProcessClosingBracket(char c, int i,
		Stack<(char bracket, int position)> stack, SchemaValidationResult result) {
		if (stack.Count == 0) {
			result.IsValid = false;
			result.Errors.Add($"Unexpected closing '{c}' at position {i} with no matching opening bracket.");
			return i + 1;
		}
		var (openBracket, openPos) = stack.Pop();
		char expected = c switch {
			')' => '(',
			'}' => '{',
			']' => '[',
			_ => '\0'
		};
		if (openBracket != expected) {
			result.IsValid = false;
			result.Errors.Add($"Mismatched bracket: '{openBracket}' at position {openPos} closed by '{c}' at position {i}.");
		}
		return i + 1;
	}
}

public class SchemaValidationResult
{
	public bool IsValid { get; set; }
	public List<string> Errors { get; set; } = new List<string>();
}
