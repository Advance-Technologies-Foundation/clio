using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools;

namespace Clio.Command.McpServer;

/// <summary>
/// Explains an argument binding failure at the place System.Text.Json reports it: which member is wrong,
/// what the contract expects there, and an example when the argument documents one.
/// </summary>
/// <remarks>
/// <para>
/// Before this, every binding failure below an argument read "argument 'filters' ... contains a value that
/// does not match the documented shape", and a payload that omitted a <c>required</c> member read "argument
/// 'args' ... must be an object" although it was one. Neither named the member or the expected shape, so an
/// agent that guessed a wrong shape (<c>{"column": ..., "operator": ...}</c> for a filter condition, an object
/// for a JSON-text <c>resources</c> string, <c>entity-set</c> for <c>entity</c>) had nothing to correct it
/// with but another guess.
/// </para>
/// <para>
/// The serializer's own <see cref="JsonException.Path"/> says WHERE the failure is; this walks the caller's
/// JSON and the declared CLR type down that path together and describes the node it reaches. It never
/// re-implements binding: a node it cannot explain (a custom converter, a path it cannot follow) yields no
/// finding, and the caller keeps the generic message. Only member NAMES the caller sent are echoed, through
/// <see cref="McpToolArgumentSupport.DescribeCallerKey"/>, never a value: a value can be a secret.
/// </para>
/// </remarks>
public static partial class McpToolErrorFilter
{
	/// <summary>Most member names one expected-shape line lists before it says how many more there are.</summary>
	private const int MaxShapeMembers = 12;

	/// <summary>Longest example quoted from an argument's description; a longer one is left out, never cut.</summary>
	private const int MaxQuotedExampleChars = 300;

	/// <summary>Longest description of the failing member quoted in the message.</summary>
	private const int MaxQuotedDescriptionChars = 200;

	private const string ExampleMarker = "Example:";

	/// <summary>Wire members per CLR type, computed once per type; see <see cref="GetWireMembers"/>.</summary>
	private static readonly ConcurrentDictionary<Type, IReadOnlyList<WireMember>> WireMembersByType = new();

	/// <summary>One member a caller can supply on the wire.</summary>
	/// <param name="Name">The wire name.</param>
	/// <param name="Property">The bound property.</param>
	/// <param name="Required">Whether the serializer refuses a payload that omits it.</param>
	private sealed record WireMember(string Name, PropertyInfo Property, bool Required);

	/// <summary>One step of a <see cref="JsonException.Path"/>: a property name or an array index.</summary>
	private sealed record PathSegment(string? PropertyName, int? Index);

	/// <summary>What the failing node is and what the contract expects there.</summary>
	/// <param name="Location">The node, relative to the argument: <c>filters.all[0]</c>, or the argument name itself.</param>
	/// <param name="IsNamedArgument">Whether the node IS the argument the message names, not something inside it.</param>
	/// <param name="Expected">The JSON kind the contract expects, when the caller sent another kind.</param>
	/// <param name="Received">The JSON kind the caller sent, when it is the wrong one.</param>
	/// <param name="MissingRequired">Required members the node's object omits.</param>
	/// <param name="UnknownMembers">Rendered names the node's object carries but the contract does not know.</param>
	/// <param name="ExpectedShape">The expected node in one line, for an object or an array of objects.</param>
	/// <param name="MemberDescription">The failing member's own description, first sentence.</param>
	/// <param name="JsonTextExpected">Whether the member takes a JSON document as TEXT and got the JSON itself.</param>
	/// <param name="ExampleOwner">The argument the example belongs to.</param>
	/// <param name="Example">An example quoted from that argument's description.</param>
	private sealed record ShapeFinding(
		string Location,
		bool IsNamedArgument,
		string? Expected,
		string? Received,
		IReadOnlyList<string> MissingRequired,
		IReadOnlyList<string> UnknownMembers,
		string? ExpectedShape,
		string? MemberDescription,
		bool JsonTextExpected,
		string? ExampleOwner,
		string? Example);

	/// <summary>
	/// Builds the message for a binding failure <see cref="DiagnoseShape"/> could explain, or returns
	/// <see langword="null"/> so the caller keeps the generic message.
	/// </summary>
	private static string? BuildShapeFindingMessage(
		string toolLabel,
		Type parameterType,
		string argumentName,
		string preciseArgumentName,
		JsonElement argumentValue,
		Exception exception) {
		ShapeFinding? finding = DiagnoseShape(parameterType, argumentName, argumentValue, exception);
		if (finding is null) {
			return null;
		}
		StringBuilder message = new($"invalid-parameter-type: argument '{preciseArgumentName}' for MCP tool '{toolLabel}' ");
		if (finding.IsNamedArgument) {
			message.Append(finding.MissingRequired.Count > 0
				? $"is missing required {DescribeMembers(finding.MissingRequired)}."
				: $"must be {finding.Expected}. Received {finding.Received}.");
		} else {
			message.Append("contains a value that does not match the documented shape. ");
			message.Append($"At {finding.Location}: ");
			message.Append(finding.MissingRequired.Count > 0
				? $"missing required {DescribeMembers(finding.MissingRequired)}."
				: $"expected {finding.Expected}, received {finding.Received}.");
		}
		if (finding.UnknownMembers.Count > 0) {
			string label = finding.UnknownMembers.Count == 1 ? "Unknown member" : "Unknown members";
			message.Append($" {label} {McpToolArgumentSupport.JoinCallerKeys(finding.UnknownMembers)}.");
		}
		if (finding.JsonTextExpected) {
			message.Append(" This member takes a JSON document as TEXT: send it as one JSON string, not as a nested JSON value.");
		}
		if (finding.MemberDescription is not null) {
			message.Append($" About {finding.Location}: {finding.MemberDescription}");
		}
		if (finding.ExpectedShape is not null) {
			message.Append($" Expected {finding.Location}: {finding.ExpectedShape}.");
		}
		if (finding.Example is not null) {
			message.Append($" Example {finding.ExampleOwner}: {finding.Example}");
		}
		return message.ToString();
	}

	/// <summary>
	/// Follows <paramref name="exception"/>'s JSON path through the caller's value and the declared type, and
	/// describes the node it reaches. <see langword="null"/> when the path cannot be followed or the node shows
	/// nothing this can name.
	/// </summary>
	private static ShapeFinding? DiagnoseShape(
		Type parameterType,
		string argumentName,
		JsonElement argumentValue,
		Exception exception) {
		if (exception is not JsonException { Path: { } path } || !TryParseJsonPath(path, out List<PathSegment> segments)) {
			return null;
		}
		Type type = UnwrapNullable(parameterType);
		JsonElement node = argumentValue;
		PropertyInfo? member = null;
		PropertyInfo? topLevelMember = null;
		for (int step = 0; step < segments.Count; step++) {
			if (!TryStep(ref type, ref node, ref member, segments[step])) {
				return null;
			}
			if (step == 0) {
				topLevelMember = member;
			}
		}

		// The argument a message names: its first property when the parameter is a composite record (what
		// GetPreciseArgumentName reports), otherwise the parameter itself.
		bool startsWithMember = segments.Count > 0 && segments[0].PropertyName is not null;
		string location = FormatLocation(argumentName, segments, startsWithMember);
		bool isNamedArgument = segments.Count == 0 || (startsWithMember && segments.Count == 1);
		(string? exampleOwner, string? example) = topLevelMember is null
			? (null, null)
			: (location.Split('.', '[')[0], ExtractExample(topLevelMember.GetCustomAttribute<DescriptionAttribute>()?.Description));

		string? expected = DescribeExpectedKind(type);
		if (expected is null) {
			return null;
		}
		if (!KindMatches(type, node)) {
			string? description = member?.GetCustomAttribute<DescriptionAttribute>()?.Description;
			return new ShapeFinding(
				location,
				isNamedArgument,
				expected,
				DescribeReceivedKind(node.ValueKind),
				[],
				[],
				DescribeShape(type),
				FirstSentence(description),
				expected == "a string" && node.ValueKind is JsonValueKind.Object or JsonValueKind.Array
					&& description?.Contains("JSON", StringComparison.Ordinal) == true,
				exampleOwner,
				example);
		}
		if (node.ValueKind != JsonValueKind.Object || !IsCompositeRecord(type)) {
			return null;
		}
		IReadOnlyList<WireMember> members = GetWireMembers(type);
		List<string> supplied = [.. node.EnumerateObject().Select(property => property.Name)];
		List<string> missing = [.. members
			.Where(candidate => candidate.Required
				&& !supplied.Contains(candidate.Name, StringComparer.OrdinalIgnoreCase))
			.Select(candidate => candidate.Name)];
		if (missing.Count == 0) {
			// The serializer refused this object for a reason this cannot see (a converter, a member it binds
			// differently): naming nothing is better than naming the wrong thing.
			return null;
		}
		List<WireMember> unsupplied = [.. members
			.Where(candidate => !supplied.Contains(candidate.Name, StringComparer.OrdinalIgnoreCase))];
		List<string> unknown = [.. supplied
			.Where(name => !members.Any(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
			.Select(name => DescribeUnknownMember(name, unsupplied))];
		return new ShapeFinding(
			location,
			isNamedArgument,
			null,
			null,
			missing,
			unknown,
			DescribeShape(type),
			null,
			false,
			exampleOwner,
			example);
	}

	/// <summary>
	/// Moves one path segment down, in the JSON and in the declared type together. False when the step does
	/// not exist on either side - the path then describes something this cannot explain.
	/// </summary>
	private static bool TryStep(ref Type type, ref JsonElement node, ref PropertyInfo? member, PathSegment segment) {
		if (segment.Index is { } index) {
			if (node.ValueKind != JsonValueKind.Array || index >= node.GetArrayLength()
				|| GetEnumerableElementType(type) is not { } elementType) {
				return false;
			}
			node = node[index];
			type = UnwrapNullable(elementType);
			return true;
		}
		if (node.ValueKind != JsonValueKind.Object
			|| !TryGetPropertyIgnoreCase(node, segment.PropertyName!, out JsonElement child)) {
			return false;
		}
		if (GetDictionaryValueType(type) is { } valueType) {
			node = child;
			type = UnwrapNullable(valueType);
			return true;
		}
		if (!IsCompositeRecord(type)) {
			return false;
		}
		WireMember? wireMember = GetWireMembers(type).FirstOrDefault(candidate =>
			string.Equals(candidate.Name, segment.PropertyName, StringComparison.OrdinalIgnoreCase));
		if (wireMember is null) {
			return false;
		}
		node = child;
		member = wireMember.Property;
		type = UnwrapNullable(wireMember.Property.PropertyType);
		return true;
	}

	/// <summary>
	/// Parses the serializer's JSON path - <c>$</c>, <c>$.filters.all[0]</c>, <c>$['odd name'][2]</c> - into
	/// segments. False for any other text, so an unexpected path yields no finding rather than a wrong one.
	/// </summary>
	private static bool TryParseJsonPath(string path, out List<PathSegment> segments) {
		segments = [];
		if (!path.StartsWith('$')) {
			return false;
		}
		int position = 1;
		while (position < path.Length) {
			if (path[position] == '.') {
				int end = path.IndexOfAny(['.', '['], position + 1);
				end = end < 0 ? path.Length : end;
				if (end == position + 1) {
					return false;
				}
				segments.Add(new PathSegment(path[(position + 1)..end], null));
				position = end;
			} else if (path[position] == '[' && position + 1 < path.Length && path[position + 1] == '\'') {
				int end = path.IndexOf("']", position + 2, StringComparison.Ordinal);
				if (end < 0) {
					return false;
				}
				segments.Add(new PathSegment(path[(position + 2)..end], null));
				position = end + 2;
			} else if (path[position] == '[') {
				int end = path.IndexOf(']', position + 1);
				if (end < 0 || !int.TryParse(path.AsSpan(position + 1, end - position - 1), out int index) || index < 0) {
					return false;
				}
				segments.Add(new PathSegment(null, index));
				position = end + 1;
			} else {
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// The node relative to the argument the message names: <c>filters.all[0]</c> under a composite record,
	/// <c>select[1]</c> for a flat parameter, or the argument name itself.
	/// </summary>
	private static string FormatLocation(string argumentName, IReadOnlyList<PathSegment> segments, bool startsWithMember) {
		StringBuilder location = new(startsWithMember ? string.Empty : argumentName);
		foreach (PathSegment segment in segments) {
			if (segment.Index is { } index) {
				location.Append('[').Append(index).Append(']');
			} else {
				if (location.Length > 0) {
					location.Append('.');
				}
				location.Append(McpToolArgumentSupport.DescribeCallerKey(segment.PropertyName!));
			}
		}
		return location.Length == 0 ? argumentName : location.ToString();
	}

	/// <summary>
	/// The JSON kind a type is bound from, as the messages word it, or <see langword="null"/> for a type that
	/// accepts any JSON (<see cref="JsonElement"/>, <see cref="JsonNode"/>, <see cref="object"/>).
	/// </summary>
	private static string? DescribeExpectedKind(Type type) {
		if (AcceptsAnyJson(type)) {
			return null;
		}
		if (IsStringLike(type)) {
			return "a string";
		}
		if (type == typeof(bool)) {
			return "a boolean";
		}
		if (IsNumber(type)) {
			return "a number";
		}
		if (IsJsonObjectContract(type)) {
			return "an object";
		}
		if (GetEnumerableElementType(type) is not null) {
			return "an array";
		}
		return "an object";
	}

	private static string DescribeReceivedKind(JsonValueKind kind) => kind switch {
		JsonValueKind.Object => "an object",
		JsonValueKind.Array => "an array",
		JsonValueKind.String => "a string",
		JsonValueKind.Number => "a number",
		JsonValueKind.True or JsonValueKind.False => "a boolean",
		JsonValueKind.Null => "null",
		_ => "an unsupported JSON value"
	};

	/// <summary>Whether the node's JSON kind is one the type is bound from, as the MCP serializer options bind it.</summary>
	private static bool KindMatches(Type type, JsonElement node) {
		if (node.ValueKind == JsonValueKind.Null) {
			return !type.IsValueType;
		}
		if (IsStringLike(type)) {
			return node.ValueKind == JsonValueKind.String
				|| (type.IsEnum && node.ValueKind == JsonValueKind.Number);
		}
		if (type == typeof(bool)) {
			return node.ValueKind is JsonValueKind.True or JsonValueKind.False;
		}
		if (IsNumber(type)) {
			return node.ValueKind == JsonValueKind.Number
				|| (node.ValueKind == JsonValueKind.String
					&& SerializerOptions.NumberHandling.HasFlag(JsonNumberHandling.AllowReadingFromString));
		}
		if (IsJsonObjectContract(type)) {
			return node.ValueKind == JsonValueKind.Object;
		}
		if (GetEnumerableElementType(type) is not null) {
			return node.ValueKind == JsonValueKind.Array;
		}
		return node.ValueKind == JsonValueKind.Object;
	}

	/// <summary>
	/// The expected node in one line - <c>an object with field (required), op, value, in</c> or <c>an array of
	/// objects with ...</c> - or <see langword="null"/> for any other type, whose kind already says it all.
	/// </summary>
	private static string? DescribeShape(Type type) {
		if (IsCompositeRecord(type)) {
			return $"an object with {DescribeMemberList(type)}";
		}
		if (GetEnumerableElementType(type) is { } elementType && IsCompositeRecord(UnwrapNullable(elementType))) {
			return $"an array of objects with {DescribeMemberList(UnwrapNullable(elementType))}";
		}
		return null;
	}

	/// <summary>A composite record's wire names, required ones marked, capped at <see cref="MaxShapeMembers"/>.</summary>
	private static string DescribeMemberList(Type type) {
		IReadOnlyList<WireMember> members = GetWireMembers(type);
		string list = string.Join(", ", members.Take(MaxShapeMembers)
			.Select(candidate => candidate.Required ? $"{candidate.Name} (required)" : candidate.Name));
		return members.Count > MaxShapeMembers ? $"{list} and {members.Count - MaxShapeMembers} more" : list;
	}

	private static string DescribeMembers(IReadOnlyList<string> names) =>
		(names.Count == 1 ? "member " : "members ") + string.Join(", ", names.Select(name => $"'{name}'"));

	/// <summary>
	/// Renders one unknown member the caller sent, with the closest member it may have meant among those it
	/// did NOT send: <c>'entity-set' (did you mean 'entity'?)</c>.
	/// </summary>
	private static string DescribeUnknownMember(string name, IReadOnlyList<WireMember> unsupplied) {
		string rendered = $"'{McpToolArgumentSupport.DescribeCallerKey(name)}'";
		string? suggestion = SuggestWireName(name, unsupplied);
		return suggestion is null ? rendered : $"{rendered} (did you mean '{suggestion}'?)";
	}

	/// <summary>
	/// The member an unknown name most likely means: one the name starts with or that starts with the name
	/// (<c>entity-set</c> - <c>entity</c>, <c>operator</c> - <c>op</c>), otherwise one within two edits. Nothing
	/// when neither holds: a semantic guess (<c>column</c> for <c>field</c>) is left to the expected shape.
	/// </summary>
	private static string? SuggestWireName(string name, IReadOnlyList<WireMember> candidates) {
		string normalized = NormalizeMemberName(name);
		if (normalized.Length < 2) {
			return null;
		}
		WireMember? prefixMatch = candidates
			.Where(candidate => NormalizeMemberName(candidate.Name) is { Length: >= 2 } known
				&& (normalized.StartsWith(known, StringComparison.Ordinal) || known.StartsWith(normalized, StringComparison.Ordinal)))
			.OrderByDescending(candidate => NormalizeMemberName(candidate.Name).Length)
			.FirstOrDefault();
		if (prefixMatch is not null) {
			return prefixMatch.Name;
		}
		return candidates
			.Select(candidate => (candidate.Name,
				Distance: McpToolArgumentSupport.LevenshteinDistance(normalized, NormalizeMemberName(candidate.Name))))
			.Where(scored => scored.Distance <= 2 && scored.Distance * 2 < normalized.Length)
			.OrderBy(scored => scored.Distance)
			.Select(scored => scored.Name)
			.FirstOrDefault();
	}

	private static string NormalizeMemberName(string name) =>
		new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

	/// <summary>
	/// The <c>Example:</c> tail of a description, up to the end of its sentence, or <see langword="null"/> when
	/// there is none or it is longer than <see cref="MaxQuotedExampleChars"/>: a cut example is a wrong one.
	/// </summary>
	private static string? ExtractExample(string? description) {
		int marker = description?.IndexOf(ExampleMarker, StringComparison.Ordinal) ?? -1;
		if (marker < 0) {
			return null;
		}
		int start = marker + ExampleMarker.Length;
		int end = ToolContractCatalog.FindSentenceEnd(description!, start);
		string example = description![start..(end < 0 ? description.Length : end + 1)].Trim();
		return example.Length is > 0 and <= MaxQuotedExampleChars ? example : null;
	}

	private static string? FirstSentence(string? description) {
		if (string.IsNullOrWhiteSpace(description)) {
			return null;
		}
		int end = ToolContractCatalog.FindSentenceEnd(description, 0);
		string sentence = (end < 0 ? description : description[..(end + 1)]).Trim();
		return ToolContractShortForm.BuildLead(sentence, MaxQuotedDescriptionChars);
	}

	/// <summary>
	/// The members a caller can supply for a composite record, with the names the serializer reads them by.
	/// </summary>
	/// <remarks>
	/// The same supplyable set as <see cref="GetJsonPropertyNames"/>, plus the property and whether the
	/// serializer requires it: a C# <c>required</c> member or <see cref="JsonRequiredAttribute"/>. A positional
	/// record parameter is NOT required - the options leave RespectRequiredConstructorParameters off, so a
	/// missing one binds to its default - and so is never reported missing.
	/// </remarks>
	private static IReadOnlyList<WireMember> GetWireMembers(Type type) =>
		WireMembersByType.GetOrAdd(type, static key => key
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(IsWireContractProperty)
			.Select(property => new WireMember(
				GetJsonPropertyName(property)
					?? SerializerOptions.PropertyNamingPolicy?.ConvertName(property.Name)
					?? property.Name,
				property,
				property.GetCustomAttribute<RequiredMemberAttribute>() is not null
					|| property.GetCustomAttribute<JsonRequiredAttribute>() is not null))
			.ToArray());

	private static bool IsCompositeRecord(Type type) =>
		type.IsClass && type != typeof(string) && !AcceptsAnyJson(type) && !IsJsonObjectContract(type)
		&& GetEnumerableElementType(type) is null && GetWireMembers(type).Count > 0;

	private static bool AcceptsAnyJson(Type type) =>
		type == typeof(JsonElement) || type == typeof(JsonDocument) || type == typeof(object)
		|| typeof(JsonNode).IsAssignableFrom(type);

	private static bool IsStringLike(Type type) =>
		type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime)
		|| type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly)
		|| type == typeof(TimeSpan) || type == typeof(Uri) || type.IsEnum;

	private static bool IsNumber(Type type) =>
		(type.IsPrimitive && type != typeof(bool) && type != typeof(char)) || type == typeof(decimal);

	private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

	/// <summary>The element type of an array or <see cref="IEnumerable{T}"/>; null for a string or a dictionary.</summary>
	private static Type? GetEnumerableElementType(Type type) {
		if (type == typeof(string) || IsJsonObjectContract(type) || AcceptsAnyJson(type)) {
			return null;
		}
		if (type.IsArray) {
			return type.GetElementType();
		}
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
			return type.GetGenericArguments()[0];
		}
		return type.GetInterfaces()
			.FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			?.GetGenericArguments()[0];
	}

	/// <summary>The value type of a string-keyed dictionary contract, or null for any other type.</summary>
	private static Type? GetDictionaryValueType(Type type) {
		if (!IsJsonObjectContract(type)) {
			return null;
		}
		Type? dictionary = IsDictionaryDefinition(type) ? type : type.GetInterfaces().FirstOrDefault(IsDictionaryDefinition);
		return dictionary?.GetGenericArguments()[1] ?? typeof(object);
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement node, string name, out JsonElement value) {
		foreach (JsonProperty property in node.EnumerateObject()) {
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
				value = property.Value;
				return true;
			}
		}
		value = default;
		return false;
	}
}
