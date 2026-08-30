using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Creatio.ConflictResolver;

internal sealed class FlatDiffTranspiler
{
	private static readonly JsonSerializerOptions PrettyJsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};
	
	private static readonly JsonSerializerOptions InlineJsonOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static readonly Regex HeaderRegex = new(
		"^([=+\\-~])\\s+(\\S+)(?:\\s+(.*))?$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex HasBodySuffixRegex = new(
		"\\.\\{hasBody:(true|false)\\}$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly IReadOnlyDictionary<char, string> OperationTypeBySymbol =
		new Dictionary<char, string>
		{
			['='] = "Equal",
			['+'] = "Add",
			['-'] = "Remove",
			['~'] = "Reordering"
		};

	private static readonly IReadOnlyDictionary<string, char> SymbolByOperationType =
		new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
		{
			["Equal"] = '=',
			["Add"] = '+',
			["Remove"] = '-',
			["Reordering"] = '~'
		};

	public string Transform(string metadata)
	{
		var items = ParseFlatMetadata(metadata);
		var transformed = SerializeTransformedMetadata(items);

		// JsonSerializer (WriteIndented) emits Environment.NewLine, so the transformed JSON would be
		// CRLF on Windows / LF on Linux regardless of the source. Re-apply the source metadata's EOL
		// so Transform is platform-independent and round-trips (Restore detects the EOL from this output).
		return ApplyNewLine(transformed, DetectNewLine(metadata));
	}

	public string Restore(string transformedMetadata) {
		var items = ParseTransformedMetadata(transformedMetadata);
		return SerializeFlatMetadata(items, transformedMetadata);
	}

	private static IReadOnlyList<TransformedItem> ParseFlatMetadata(string metadata)
	{
		if (string.IsNullOrWhiteSpace(metadata))
		{
			throw new ArgumentException("Metadata content cannot be null or whitespace.", nameof(metadata));
		}

		var lines = SplitLines(metadata);
		var items = new List<TransformedItem>();
		for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
		{
			var line = lines[lineIndex];
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			var headerMatch = HeaderRegex.Match(line);
			if (!headerMatch.Success)
			{
				throw new FormatException($"Invalid metadata line format at line {lineIndex + 1}: '{line}'.");
			}

			var operation = headerMatch.Groups[1].Value[0];
			if (!OperationTypeBySymbol.TryGetValue(operation, out var operationType))
			{
				throw new FormatException($"Unsupported operation '{operation}' at line {lineIndex + 1}.");
			}

			var path = headerMatch.Groups[2].Value;
			var bodyLines = new List<string>();
			if (headerMatch.Groups[3].Success)
			{
				var bodyFirstLine = headerMatch.Groups[3].Value;
				if (!string.IsNullOrWhiteSpace(bodyFirstLine))
				{
					bodyLines.Add(bodyFirstLine);
					if (IsBlockStart(bodyFirstLine))
					{
						var depth = BracketDelta(bodyFirstLine);
						while (depth > 0)
						{
							lineIndex++;
							if (lineIndex >= lines.Count)
							{
								throw new FormatException($"Unterminated JSON block for path '{path}'.");
							}

							var bodyLine = lines[lineIndex];
							bodyLines.Add(bodyLine);
							depth += BracketDelta(bodyLine);
						}
					}
				}
			}

			var hasBody = bodyLines.Count > 0;
			JsonNode? body = null;
			if (hasBody)
			{
				var bodyText = string.Join("\n", bodyLines);
				try
				{
					body = JsonNode.Parse(bodyText);
				}
				catch (JsonException ex)
				{
					throw new FormatException($"Invalid JSON body for path '{path}' at line {lineIndex + 1}: {ex.Message}", ex);
				}
			}

			var uid = BuildTransformedUid(operation, path, hasBody, body);
			uid = AddHasBodyMarker(uid, hasBody);
			var inline = !hasBody || bodyLines.Count == 1;
			items.Add(new TransformedItem(
				operation,
				operationType,
				uid,
				inline,
				hasBody,
				body));
		}

		return items;
	}

	internal static IReadOnlyList<TransformedItem> ParseTransformedMetadata(string transformedMetadata)
	{
		if (string.IsNullOrWhiteSpace(transformedMetadata))
		{
			throw new ArgumentException("Transformed metadata content cannot be null or whitespace.", nameof(transformedMetadata));
		}

		JsonObject root;
		try
		{
			root = JsonNode.Parse(transformedMetadata) as JsonObject
			       ?? throw new FormatException("Transformed metadata must have an object root.");
		}
		catch (JsonException ex)
		{
			throw new FormatException($"Transformed metadata is not valid JSON: {ex.Message}", ex);
		}

		if (root["Items"] is not JsonArray itemsArray)
		{
			throw new FormatException("Transformed metadata must contain an 'Items' array.");
		}

		var items = new List<TransformedItem>(itemsArray.Count);
		for (var itemIndex = 0; itemIndex < itemsArray.Count; itemIndex++)
		{
			if (itemsArray[itemIndex] is not JsonObject itemObject)
			{
				throw new FormatException($"Item at index {itemIndex} must be an object.");
			}

			var operationType = ReadRequiredString(itemObject, "OperationType", itemIndex);
			if (!SymbolByOperationType.TryGetValue(operationType, out var operation))
			{
				throw new FormatException($"Unsupported operation type '{operationType}' at index {itemIndex}.");
			}

			var inline = ReadRequiredBoolean(itemObject, "Inline", itemIndex);
			var hasBody = itemObject.TryGetPropertyValue("Body", out var bodyNode);
			var uid = ReadRequiredString(itemObject, "UId", itemIndex);
			if (!TryStripHasBodyMarker(uid, out var strippedUid, out var hasBodyMarker))
			{
				throw new FormatException($"Invalid hasBody marker format in UId at item index {itemIndex}.");
			}

			if (hasBodyMarker is not null && hasBodyMarker.Value != hasBody)
			{
				throw new FormatException(
					$"UId hasBody marker mismatch at item index {itemIndex}. " +
					$"Marker='{hasBodyMarker.Value}', BodyExists='{hasBody}'.");
			}

			items.Add(new TransformedItem(
				operation,
				OperationTypeBySymbol[operation],
				strippedUid,
				inline,
				hasBody,
				hasBody ? bodyNode?.DeepClone() : null));
		}

		return items;
	}

	private static string SerializeTransformedMetadata(IReadOnlyList<TransformedItem> items)
	{
		var array = new JsonArray();
		foreach (var item in items)
		{
			var jsonItem = new JsonObject
			{
				["OperationType"] = item.OperationType,
				["UId"] = item.UId
			};

			if (item.HasBody)
			{
				jsonItem["Body"] = item.Body?.DeepClone();
			}

			jsonItem["Inline"] = item.Inline;
			array.Add(jsonItem);
		}

		var root = new JsonObject
		{
			["Items"] = array
		};

		return BoundedJsonSerializer.Serialize(root, PrettyJsonOptions);
	}

	internal static string SerializeFlatMetadata(IReadOnlyList<TransformedItem> items, string sourceContent)
	{
		var newLine = DetectNewLine(sourceContent);
		var outputLines = new List<string>();
		foreach (var item in items)
		{
			var path = BuildFlatPath(item);
			if (!item.HasBody)
			{
				outputLines.Add($"{item.Operation} {path}");
				continue;
			}

			var bodyText = SerializeBody(item.Body, item.Inline);
			var bodyLines = SplitLines(bodyText);
			if (bodyLines.Count == 1)
			{
				outputLines.Add($"{item.Operation} {path} {bodyLines[0]}");
				continue;
			}

			outputLines.Add($"{item.Operation} {path} {bodyLines[0]}");
			for (var i = 1; i < bodyLines.Count; i++)
			{
				outputLines.Add(bodyLines[i]);
			}
		}

		var output = string.Join(newLine, outputLines);
		if (HasTrailingNewLine(sourceContent))
		{
			output += newLine;
		}

		return output;
	}

	private static string BuildTransformedUid(char operation, string path, bool hasBody, JsonNode? body)
	{
		if (operation == '~')
		{
			return $"~{path}";
		}

		if (!hasBody || body is not JsonObject bodyObject || !TryGetUid(bodyObject, out var uid))
		{
			return path;
		}

		var suffix = $".[{JsonSerializer.Serialize(uid)}]";
		return path.EndsWith(suffix, StringComparison.Ordinal)
			? path
			: $"{path}{suffix}";
	}

	internal static string BuildFlatPath(TransformedItem item)
	{
		if (item.Operation == '~')
		{
			return item.UId.StartsWith("~", StringComparison.Ordinal)
				? item.UId.Substring(1)
				: item.UId;
		}

		if (!item.HasBody || item.Body is not JsonObject bodyObject || !TryGetUid(bodyObject, out var uid))
		{
			return item.UId;
		}

		return RemoveSyntheticUidSuffix(item.UId, uid);
	}

	internal static string AddHasBodyMarker(string uid, bool hasBody)
	{
		_ = TryStripHasBodyMarker(uid, out var strippedUid, out _);
		return $"{strippedUid}.{{hasBody:{hasBody.ToString().ToLowerInvariant()}}}";
	}

	private static bool TryStripHasBodyMarker(string uid, out string strippedUid, out bool? hasBody)
	{
		strippedUid = uid;
		hasBody = null;
		if (string.IsNullOrWhiteSpace(uid))
		{
			return true;
		}

		var match = HasBodySuffixRegex.Match(uid);
		if (!match.Success)
		{
			return true;
		}

		strippedUid = uid.Substring(0, match.Index);
		if (!bool.TryParse(match.Groups[1].Value, out var parsedHasBody))
		{
			return false;
		}

		hasBody = parsedHasBody;
		return true;
	}

	private static string RemoveSyntheticUidSuffix(string path, string uid)
	{
		if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(uid))
		{
			return path;
		}

		var suffixStart = path.LastIndexOf(".[", StringComparison.Ordinal);
		if (suffixStart < 0 || !path.EndsWith("]", StringComparison.Ordinal))
		{
			return path;
		}

		var token = path.Substring(suffixStart + 2, path.Length - suffixStart - 3).Trim();
		if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
		{
			token = token.Substring(1, token.Length - 2);
		}

		return string.Equals(token, uid, StringComparison.OrdinalIgnoreCase)
			? path.Substring(0, suffixStart)
			: path;
	}

	internal static bool TryGetUid(JsonObject bodyObject, out string uid)
	{
		uid = string.Empty;
		if (!bodyObject.TryGetPropertyValue("UId", out var uidNode) || uidNode is not JsonValue value)
		{
			return false;
		}

		if (!value.TryGetValue<string>(out var parsedUid) || string.IsNullOrWhiteSpace(parsedUid))
		{
			return false;
		}

		uid = parsedUid;
		return true;
	}

	private static string ReadRequiredString(JsonObject item, string propertyName, int itemIndex)
	{
		if (!item.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value || !value.TryGetValue<string>(out var text))
		{
			throw new FormatException($"Property '{propertyName}' must be a string at item index {itemIndex}.");
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			throw new FormatException($"Property '{propertyName}' cannot be empty at item index {itemIndex}.");
		}

		return text;
	}

	private static bool ReadRequiredBoolean(JsonObject item, string propertyName, int itemIndex)
	{
		if (!item.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value || !value.TryGetValue<bool>(out var parsed))
		{
			throw new FormatException($"Property '{propertyName}' must be a boolean at item index {itemIndex}.");
		}

		return parsed;
	}

	private static bool IsBlockStart(string bodyFirstLine)
	{
		if (string.IsNullOrWhiteSpace(bodyFirstLine))
		{
			return false;
		}

		var trimmed = bodyFirstLine.TrimStart();
		return trimmed.StartsWith("{", StringComparison.Ordinal) ||
		       trimmed.StartsWith("[", StringComparison.Ordinal);
	}

	private static int BracketDelta(string line)
	{
		var delta = 0;
		var inString = false;
		var escaped = false;
		for (var i = 0; i < line.Length; i++)
		{
			var ch = line[i];
			if (inString)
			{
				if (escaped)
				{
					escaped = false;
					continue;
				}

				if (ch == '\\')
				{
					escaped = true;
					continue;
				}

				if (ch == '"')
				{
					inString = false;
				}

				continue;
			}

			if (ch == '"')
			{
				inString = true;
				continue;
			}

			if (ch is '{' or '[')
			{
				delta++;
			}
			else if (ch is '}' or ']')
			{
				delta--;
			}
		}

		return delta;
	}

	internal static string SerializeBody(JsonNode? body, bool inline)
	{
		if (inline)
		{
			return body?.ToJsonString(InlineJsonOptions) ?? "null";
		}

		return JsonSerializer.Serialize(body, PrettyJsonOptions);
	}

	internal static IReadOnlyList<string> SplitLines(string content)
	{
		return NormalizeLineEndings(content)
			.Split('\n')
			.ToArray();
	}

	internal static string NormalizeLineEndings(string content)
	{
		return content.Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	// Re-apply a target EOL: collapse CRLF to LF, then expand to the requested newline.
	// Only changes EOL style, never the number of line breaks.
	internal static string ApplyNewLine(string content, string newLine)
	{
		var lf = NormalizeLineEndings(content);
		return newLine == "\n" ? lf : lf.Replace("\n", newLine, StringComparison.Ordinal);
	}

	internal static string DetectNewLine(string content)
	{
		return content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
	}

	internal static bool HasTrailingNewLine(string content)
	{
		return content.EndsWith("\r\n", StringComparison.Ordinal) ||
		       content.EndsWith("\n", StringComparison.Ordinal);
	}

	internal sealed record TransformedItem(
		char Operation,
		string OperationType,
		string UId,
		bool Inline,
		bool HasBody,
		JsonNode? Body);
}
