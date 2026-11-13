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
using System.Text.RegularExpressions;
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
/// re-implements binding: a node it cannot explain - a converter-bound or polymorphic type, a path it cannot
/// follow - yields no finding, and the caller keeps the generic message. It runs only on a call that has
/// already failed, and a failure of its own is swallowed for the same reason.
/// </para>
/// <para>
/// Caller-supplied text reaches the message only as member names and dictionary keys, through
/// <see cref="McpToolArgumentSupport.DescribeCallerKey"/> and capped at
/// <see cref="McpToolArgumentSupport.MaxEchoedKeys"/>; a VALUE never does, since a value can be a secret.
/// Member names in a location and an expected shape are the record's own names - its <c>[JsonPropertyName]</c>,
/// else the property name, the same list the flat-argument classifier uses - not the caller's spelling.
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

	/// <summary>
	/// Longest unknown key a "did you mean" suggestion is computed for. The edit distance is quadratic in the
	/// key's length, and a key this long is not a misspelling of a member name.
	/// </summary>
	private const int MaxSuggestedKeyLength = 64;

	private const string ExampleMarker = "Example:";

	/// <summary>
	/// A member description that says the member holds a JSON document as text: "JSON object string",
	/// "JSON array of ...", "JSON text".
	/// </summary>
	private static readonly Regex JsonTextMember = new(@"\bJSON\s+(object|array|document|text|string)\b",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	/// <summary>Wire members per CLR type, computed once per type; see <see cref="GetWireMembers"/>.</summary>
	private static readonly ConcurrentDictionary<Type, IReadOnlyList<WireMember>> WireMembersByType = new();

	/// <summary>One member a caller can supply on the wire.</summary>
	/// <param name="Name">The record's name for the member: its <c>[JsonPropertyName]</c>, else the property name.</param>
	/// <param name="Property">The bound property.</param>
	/// <param name="Required">Whether the serializer refuses a payload that omits it.</param>
	private sealed record WireMember(string Name, PropertyInfo Property, bool Required);

	/// <summary>One step of a <see cref="JsonException.Path"/>: a property name or an array index.</summary>
	private sealed record PathSegment(string? PropertyName, int? Index);

	/// <summary>One step of the walk, as the message shows it.</summary>
	/// <param name="Display">The step in a location: the record's member name, <c>[0]</c> or <c>['key']</c>.</param>
	/// <param name="Member">The record member the step entered; null for an array index or a dictionary key.</param>
	private sealed record WalkStep(string Display, PropertyInfo? Member);

	/// <summary>What the failing node is and what the contract expects there.</summary>
	/// <param name="NamedArgument">The argument the lead sentence names: the first member, or the parameter.</param>
	/// <param name="Location">The node, relative to that argument: <c>filters.all[0]</c>, or the argument itself.</param>
	/// <param name="IsNamedArgument">Whether the node IS the named argument, not something inside it.</param>
	/// <param name="Expected">The JSON kind the contract expects, when the caller sent another kind.</param>
	/// <param name="Received">The JSON kind the caller sent, when it is the wrong one.</param>
	/// <param name="MissingRequired">Required members the node's object omits.</param>
	/// <param name="UnknownMembers">Rendered unknown names, at most <see cref="McpToolArgumentSupport.MaxEchoedKeys"/>.</param>
	/// <param name="UnknownMemberCount">How many unknown names the node's object carries in all.</param>
	/// <param name="ExpectedShape">The expected node in one line, for an object or an array of objects.</param>
	/// <param name="MemberDescription">The failing member's own description, first sentence.</param>
	/// <param name="JsonTextExpected">Whether the member takes a JSON document as TEXT and got the JSON itself.</param>
	/// <param name="Example">An example quoted from the named argument's description.</param>
	private sealed record ShapeFinding(
		string NamedArgument,
		string Location,
		bool IsNamedArgument,
		string? Expected,
		string? Received,
		IReadOnlyList<string> MissingRequired,
		IReadOnlyList<string> UnknownMembers,
		int UnknownMemberCount,
		string? ExpectedShape,
		string? MemberDescription,
		bool JsonTextExpected,
		string? Example);

	/// <summary>Where a finding sits and what describes it, worked out from the walk alone.</summary>
	/// <param name="NamedArgument">The argument the lead sentence names.</param>
	/// <param name="Location">The failing node, relative to that argument.</param>
	/// <param name="IsNamedArgument">Whether the node IS the named argument.</param>
	/// <param name="Example">The example documented on the named argument.</param>
	/// <param name="MemberDescription">The failing member's own description, when the node is a member.</param>
	private sealed record FindingContext(
		string NamedArgument,
		string Location,
		bool IsNamedArgument,
		string? Example,
		string? MemberDescription);

	/// <summary>
	/// Builds the message for a binding failure <see cref="DiagnoseShape"/> could explain, or returns
	/// <see langword="null"/> so the caller keeps the generic message.
	/// </summary>
	private static string? BuildShapeFindingMessage(
		string toolLabel,
		Type parameterType,
		string argumentName,
		JsonElement argumentValue,
		Exception exception) {
		ShapeFinding? finding = TryDiagnoseShape(parameterType, argumentName, argumentValue, exception);
		if (finding is null) {
			return null;
		}
		StringBuilder message = new(
			$"invalid-parameter-type: argument '{finding.NamedArgument}' for MCP tool '{toolLabel}' ");
		AppendFailure(message, finding);
		AppendDetails(message, finding);
		return message.ToString();
	}

	/// <summary>
	/// <see cref="DiagnoseShape"/>, with its own failure turned into "no finding": the diagnosis explains a
	/// failure and must never become one. On the direct path it runs outside the filter's redacted try, so a
	/// throw here would reach the SDK's default handler as raw text.
	/// </summary>
	private static ShapeFinding? TryDiagnoseShape(
		Type parameterType, string argumentName, JsonElement argumentValue, Exception exception) {
		try {
			return DiagnoseShape(parameterType, argumentName, argumentValue, exception);
		}
		catch (Exception diagnosticFailure) when (!McpExceptionPolicy.IsUnrecoverable(diagnosticFailure)) {
			return null;
		}
	}

	/// <summary>The sentence that says what is wrong: about the named argument itself, or about a node inside it.</summary>
	private static void AppendFailure(StringBuilder message, ShapeFinding finding) {
		bool missing = finding.MissingRequired.Count > 0;
		if (finding.IsNamedArgument) {
			message.Append(missing
				? $"is missing required {DescribeMembers(finding.MissingRequired)}."
				: $"must be {finding.Expected}. Received {finding.Received}.");
			return;
		}
		message.Append("contains a value that does not match the documented shape. ");
		message.Append($"At {QuoteLocation(finding.Location)}: ");
		message.Append(missing
			? $"missing required {DescribeMembers(finding.MissingRequired)}."
			: $"expected {finding.Expected}, received {finding.Received}.");
	}

	/// <summary>The sentences that help correct the call: unknown members, hints, the expected shape, an example.</summary>
	private static void AppendDetails(StringBuilder message, ShapeFinding finding) {
		if (finding.UnknownMemberCount > 0) {
			int hidden = finding.UnknownMemberCount - finding.UnknownMembers.Count;
			message.Append(finding.UnknownMemberCount == 1 ? " Unknown member " : " Unknown members ");
			message.Append(string.Join(", ", finding.UnknownMembers));
			message.Append(hidden > 0 ? $" and {hidden} more." : ".");
		}
		if (finding.JsonTextExpected) {
			message.Append(" This member takes a JSON document as TEXT: send it as one JSON string, not as a nested JSON value.");
		}
		if (finding.MemberDescription is not null) {
			message.Append($" About {QuoteLocation(finding.Location)}: {finding.MemberDescription}");
		}
		if (finding.ExpectedShape is not null) {
			message.Append($" Expected {QuoteLocation(finding.Location)}: {finding.ExpectedShape}.");
		}
		if (finding.Example is not null) {
			message.Append($" Example {QuoteLocation(finding.NamedArgument)}: {finding.Example}");
		}
	}

	/// <summary>
	/// A location or member name as a hint shows it before its colon: in backticks, <c>`filters.all[0]`</c>.
	/// </summary>
	/// <remarks>
	/// Not cosmetic. A refusal that leaves through <c>clio-run</c> or the durable long-tail handler passes
	/// <c>ClioRunExecutor.RedactFailureContent</c>, whose credential rule reads a bare <c>password: ...</c>,
	/// <c>host: ...</c> or <c>uId: ...</c> as a secret pair: "About password: Direct password paired with uri"
	/// arrived as "About password=[redacted] password paired with uri". A quoted name is not that shape. Backticks
	/// rather than single quotes, because a location already carries <c>['key']</c> for a dictionary key.
	/// </remarks>
	private static string QuoteLocation(string location) => $"`{location}`";

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
		List<WalkStep> steps = [];
		foreach (PathSegment segment in segments) {
			WalkStep? step = Step(ref type, ref node, segment);
			if (step is null) {
				return null;
			}
			steps.Add(step);
		}
		string? expected = DescribeExpectedKind(type);
		if (expected is null) {
			return null;
		}
		FindingContext context = DescribeContext(argumentName, steps);
		if (!KindMatches(type, node)) {
			return KindMismatchFinding(context, type, node, expected);
		}
		return node.ValueKind == JsonValueKind.Object && IsCompositeRecord(type)
			? MissingMemberFinding(context, type, node)
			: null;
	}

	/// <summary>
	/// The argument a message names - the parameter's first member when the walk entered one (what
	/// GetPreciseArgumentName reports), otherwise the parameter itself, a flat array or dictionary parameter -
	/// and the texts that describe the failing node.
	/// </summary>
	private static FindingContext DescribeContext(string argumentName, IReadOnlyList<WalkStep> steps) {
		PropertyInfo? firstMember = steps.Count > 0 ? steps[0].Member : null;
		bool startsWithMember = firstMember is not null;
		string namedArgument = startsWithMember ? steps[0].Display : argumentName;
		// Only a node that IS a member has a description of its own: an array element or a dictionary value is
		// not described by its container's text.
		PropertyInfo? lastMember = steps.Count > 0 ? steps[^1].Member : null;
		return new FindingContext(
			NamedArgument: namedArgument,
			Location: FormatLocation(namedArgument, steps, startsWithMember),
			IsNamedArgument: steps.Count == 0 || (startsWithMember && steps.Count == 1),
			Example: ExtractExample(firstMember?.GetCustomAttribute<DescriptionAttribute>()?.Description),
			MemberDescription: lastMember?.GetCustomAttribute<DescriptionAttribute>()?.Description);
	}

	/// <summary>A node of the wrong JSON kind: both kinds, the expected shape and the member's own text.</summary>
	private static ShapeFinding KindMismatchFinding(FindingContext context, Type type, JsonElement node, string expected) {
		string? description = context.MemberDescription;
		bool jsonTextExpected = type == typeof(string) && node.ValueKind is JsonValueKind.Object or JsonValueKind.Array
			&& description is not null && JsonTextMember.IsMatch(description);
		return new ShapeFinding(
			NamedArgument: context.NamedArgument,
			Location: context.Location,
			IsNamedArgument: context.IsNamedArgument,
			Expected: expected,
			Received: DescribeReceivedKind(node.ValueKind),
			MissingRequired: [],
			UnknownMembers: [],
			UnknownMemberCount: 0,
			ExpectedShape: DescribeShape(type),
			MemberDescription: FirstSentence(description),
			JsonTextExpected: jsonTextExpected,
			Example: context.Example);
	}

	/// <summary>
	/// An object that lacks a required member: the missing ones, the unknown keys it carries with suggestions,
	/// and the expected shape. <see langword="null"/> when nothing is missing - the serializer refused the object
	/// for a reason this cannot see (a converter, a member it binds differently), and naming nothing is better
	/// than naming the wrong thing.
	/// </summary>
	private static ShapeFinding? MissingMemberFinding(FindingContext context, Type type, JsonElement node) {
		IReadOnlyList<WireMember> members = GetWireMembers(type);
		HashSet<string> memberNames = new(members.Select(candidate => candidate.Name), StringComparer.OrdinalIgnoreCase);
		HashSet<string> supplied = new(StringComparer.OrdinalIgnoreCase);
		List<string> unknown = [];
		foreach (JsonProperty property in node.EnumerateObject()) {
			supplied.Add(property.Name);
			if (!memberNames.Contains(property.Name)) {
				unknown.Add(property.Name);
			}
		}
		List<string> missing = [.. members
			.Where(candidate => candidate.Required && !supplied.Contains(candidate.Name))
			.Select(candidate => candidate.Name)];
		if (missing.Count == 0) {
			return null;
		}
		List<WireMember> unsupplied = [.. members.Where(candidate => !supplied.Contains(candidate.Name))];
		return new ShapeFinding(
			NamedArgument: context.NamedArgument,
			Location: context.Location,
			IsNamedArgument: context.IsNamedArgument,
			Expected: null,
			Received: null,
			MissingRequired: missing,
			UnknownMembers: [.. unknown.Take(McpToolArgumentSupport.MaxEchoedKeys)
				.Select(name => DescribeUnknownMember(name, unsupplied))],
			UnknownMemberCount: unknown.Count,
			ExpectedShape: DescribeShape(type),
			MemberDescription: null,
			JsonTextExpected: false,
			Example: context.Example);
	}

	/// <summary>
	/// Moves one path segment down, in the JSON and in the declared type together. <see langword="null"/> when
	/// the step does not exist on either side, or leads into a member read through its own converter - the
	/// path then describes something this cannot explain.
	/// </summary>
	private static WalkStep? Step(ref Type type, ref JsonElement node, PathSegment segment) {
		if (segment.Index is { } index) {
			if (node.ValueKind != JsonValueKind.Array || index >= node.GetArrayLength()
				|| GetEnumerableElementType(type) is not { } elementType) {
				return null;
			}
			node = node[index];
			type = UnwrapNullable(elementType);
			return new WalkStep($"[{index}]", null);
		}
		if (segment.PropertyName is not { } name || node.ValueKind != JsonValueKind.Object) {
			return null;
		}
		if (GetDictionaryValueType(type) is { } valueType) {
			// A dictionary key is the caller's own text, matched exactly as the serializer stores it, and shown
			// bracketed so it cannot read as the server's prose.
			if (!TryGetProperty(node, name, StringComparison.Ordinal, out JsonElement value)) {
				return null;
			}
			node = value;
			type = UnwrapNullable(valueType);
			return new WalkStep($"['{McpToolArgumentSupport.DescribeCallerKey(name)}']", null);
		}
		WireMember? member = IsCompositeRecord(type)
			? GetWireMembers(type).FirstOrDefault(candidate =>
				string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
			: null;
		if (member is null || member.Property.GetCustomAttribute<JsonConverterAttribute>() is not null
			|| !TryGetProperty(node, name, StringComparison.OrdinalIgnoreCase, out JsonElement child)) {
			return null;
		}
		node = child;
		type = UnwrapNullable(member.Property.PropertyType);
		return new WalkStep(member.Name, member.Property);
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
			PathSegment? segment = ReadSegment(path, ref position);
			if (segment is null) {
				return false;
			}
			segments.Add(segment);
		}
		return true;
	}

	/// <summary>One segment at <paramref name="position"/>, which moves past it; <see langword="null"/> for any other text.</summary>
	private static PathSegment? ReadSegment(string path, ref int position) {
		if (path[position] == '.') {
			return ReadDottedName(path, ref position);
		}
		if (path[position] != '[') {
			return null;
		}
		return position + 1 < path.Length && path[position + 1] == '\''
			? ReadQuotedName(path, ref position)
			: ReadIndex(path, ref position);
	}

	/// <summary><c>.name</c>, up to the next <c>.</c> or <c>[</c>.</summary>
	private static PathSegment? ReadDottedName(string path, ref int position) {
		int end = path.IndexOfAny(['.', '['], position + 1);
		end = end < 0 ? path.Length : end;
		if (end == position + 1) {
			return null;
		}
		PathSegment segment = new(path[(position + 1)..end], null);
		position = end;
		return segment;
	}

	/// <summary><c>['name']</c>, the form the serializer uses for a name with special characters.</summary>
	private static PathSegment? ReadQuotedName(string path, ref int position) {
		int end = path.IndexOf("']", position + 2, StringComparison.Ordinal);
		if (end < 0) {
			return null;
		}
		PathSegment segment = new(path[(position + 2)..end], null);
		position = end + 2;
		return segment;
	}

	/// <summary><c>[0]</c>, an array index.</summary>
	private static PathSegment? ReadIndex(string path, ref int position) {
		int end = path.IndexOf(']', position + 1);
		if (end < 0 || !int.TryParse(path.AsSpan(position + 1, end - position - 1), out int index) || index < 0) {
			return null;
		}
		position = end + 1;
		return new PathSegment(null, index);
	}

	/// <summary>
	/// The node relative to the argument the message names: <c>filters.all[0]</c> under a composite record,
	/// <c>select[1]</c> or <c>labels['en']</c> for a flat parameter, or the argument name itself.
	/// </summary>
	private static string FormatLocation(string namedArgument, IReadOnlyList<WalkStep> steps, bool startsWithMember) {
		StringBuilder location = new(namedArgument);
		foreach (WalkStep step in steps.Skip(startsWithMember ? 1 : 0)) {
			location.Append(step.Member is null ? step.Display : "." + step.Display);
		}
		return location.ToString();
	}

	/// <summary>
	/// The JSON kind a type is bound from, as the messages word it, or <see langword="null"/> when this cannot
	/// say: a type that accepts any JSON (<see cref="JsonElement"/>, <see cref="JsonNode"/>, <see cref="object"/>),
	/// one read through its own converter, or a class with no wire members of its own.
	/// </summary>
	private static string? DescribeExpectedKind(Type type) {
		if (AcceptsAnyJson(type) || HasOwnConverter(type)) {
			return null;
		}
		if (type == typeof(byte[]) || IsStringLike(type)) {
			return DescribeStringFormat(type);
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
		return IsCompositeRecord(type) || IsPolymorphic(type) ? "an object" : null;
	}

	/// <summary>
	/// A string-bound type with its format, because a value of the right JSON kind can still fail on it: "must
	/// be a string" is no correction for <c>{"id": "nope"}</c> when the member is a GUID.
	/// </summary>
	private static string DescribeStringFormat(Type type) {
		if (type.IsEnum) {
			string[] names = Enum.GetNames(type);
			string list = string.Join(", ", names.Take(MaxShapeMembers));
			return names.Length > MaxShapeMembers
				? $"a string naming one of {list} and {names.Length - MaxShapeMembers} more"
				: $"a string naming one of {list}";
		}
		return type switch {
			_ when type == typeof(Guid) => "a GUID string",
			_ when type == typeof(DateTime) || type == typeof(DateTimeOffset) => "an ISO 8601 date-time string",
			_ when type == typeof(DateOnly) => "an ISO 8601 date string",
			_ when type == typeof(TimeOnly) => "an ISO 8601 time string",
			_ when type == typeof(TimeSpan) => "a time-span string such as \"00:05:00\"",
			_ when type == typeof(Uri) => "a URI string",
			_ when type == typeof(char) => "a one-character string",
			_ when type == typeof(byte[]) => "a base64 string",
			_ => "a string"
		};
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
		if (type == typeof(byte[]) || IsStringLike(type)) {
			return node.ValueKind == JsonValueKind.String
				|| (type.IsEnum && node.ValueKind == JsonValueKind.Number);
		}
		if (type == typeof(bool)) {
			return node.ValueKind is JsonValueKind.True or JsonValueKind.False;
		}
		if (IsNumber(type)) {
			// The options read a number from a string too, but only from a string that holds one: "many" for
			// top is still the wrong kind.
			return node.ValueKind == JsonValueKind.Number
				|| (node.ValueKind == JsonValueKind.String
					&& SerializerOptions.NumberHandling.HasFlag(JsonNumberHandling.AllowReadingFromString)
					&& decimal.TryParse(node.GetString(), System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out _));
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
	/// objects with ...</c> - or <see langword="null"/> for any other type, whose kind already says it all, and
	/// for a polymorphic one, whose members depend on its discriminator.
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
	private static string? SuggestWireName(string name, IReadOnlyList<WireMember> candidates) =>
		SuggestName(name, [.. candidates.Select(candidate => candidate.Name)]);

	/// <summary>
	/// <see cref="SuggestWireName"/> over plain names, shared with the missing-argument refusal. Names are
	/// compared with letters and digits only, lower-cased, so <c>environment-name</c> finds
	/// <c>environmentName</c>.
	/// </summary>
	private static string? SuggestName(string name, IReadOnlyList<string> candidates) {
		if (name.Length > MaxSuggestedKeyLength) {
			return null;
		}
		string normalized = NormalizeMemberName(name);
		if (normalized.Length < 2) {
			return null;
		}
		string? prefixMatch = candidates
			.Where(candidate => NormalizeMemberName(candidate) is { Length: >= 2 } known
				&& (normalized.StartsWith(known, StringComparison.Ordinal) || known.StartsWith(normalized, StringComparison.Ordinal)))
			.OrderByDescending(candidate => NormalizeMemberName(candidate).Length)
			.FirstOrDefault();
		if (prefixMatch is not null) {
			return prefixMatch;
		}
		return candidates
			.Select(candidate => (Name: candidate, Known: NormalizeMemberName(candidate)))
			.Where(scored => Math.Abs(scored.Known.Length - normalized.Length) <= 2)
			.Select(scored => (scored.Name, Distance: McpToolArgumentSupport.LevenshteinDistance(normalized, scored.Known)))
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
		if (description is null) {
			return null;
		}
		int marker = description.IndexOf(ExampleMarker, StringComparison.Ordinal);
		if (marker < 0) {
			return null;
		}
		int start = marker + ExampleMarker.Length;
		int end = ToolContractCatalog.FindSentenceEnd(description, start);
		string example = description[start..(end < 0 ? description.Length : end + 1)].Trim();
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
	/// The members a caller can supply for a type, with the names the serializer reads them by, computed once
	/// per type. The one definition behind <see cref="GetJsonPropertyNames"/> as well.
	/// </summary>
	/// <remarks>
	/// Required means what the serializer enforces: a C# <c>required</c> member or
	/// <see cref="JsonRequiredAttribute"/>. A positional record parameter is NOT required - the options leave
	/// RespectRequiredConstructorParameters off, so a missing one binds to its default - and so is never
	/// reported missing.
	/// </remarks>
	private static IReadOnlyList<WireMember> GetWireMembers(Type type) =>
		WireMembersByType.GetOrAdd(type, static key => key
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(IsWireContractProperty)
			.Select(property => new WireMember(
				GetJsonPropertyName(property) ?? property.Name,
				property,
				property.GetCustomAttribute<RequiredMemberAttribute>() is not null
					|| property.GetCustomAttribute<JsonRequiredAttribute>() is not null))
			.ToArray());

	/// <summary>
	/// A class bound member by member: not a collection, not a JSON-anything type, not polymorphic (its members
	/// depend on the discriminator) and not read through its own converter.
	/// </summary>
	private static bool IsCompositeRecord(Type type) =>
		type.IsClass && type != typeof(string) && !AcceptsAnyJson(type) && !IsJsonObjectContract(type)
		&& GetEnumerableElementType(type) is null && !IsPolymorphic(type) && !HasOwnConverter(type)
		&& GetWireMembers(type).Count > 0;

	private static bool IsPolymorphic(Type type) =>
		type.GetCustomAttribute<JsonPolymorphicAttribute>() is not null
		|| type.GetCustomAttributes<JsonDerivedTypeAttribute>().Any();

	/// <summary>
	/// Whether the type is read through a converter of its own - an attribute on the type, or one the MCP
	/// options carry - except an enum, whose string converter is what makes it "a string".
	/// </summary>
	private static bool HasOwnConverter(Type type) =>
		!type.IsEnum
		&& (type.GetCustomAttribute<JsonConverterAttribute>() is not null
			|| SerializerOptions.Converters.Any(converter => converter.CanConvert(type)));

	/// <remarks>
	/// <see cref="JsonNode"/> and <see cref="JsonValue"/> themselves, not every subclass: a
	/// <see cref="JsonObject"/> or <see cref="JsonArray"/> member takes exactly one JSON kind.
	/// </remarks>
	private static bool AcceptsAnyJson(Type type) =>
		type == typeof(JsonElement) || type == typeof(JsonDocument) || type == typeof(object)
		|| type == typeof(JsonNode) || type == typeof(JsonValue);

	private static bool IsStringLike(Type type) =>
		type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime)
		|| type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly)
		|| type == typeof(TimeSpan) || type == typeof(Uri) || type.IsEnum;

	private static bool IsNumber(Type type) =>
		(type.IsPrimitive && type != typeof(bool) && type != typeof(char)) || type == typeof(decimal);

	private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

	/// <summary>
	/// The element type of an array or <see cref="IEnumerable{T}"/>; null for a string, a byte array (base64
	/// text on the wire) or a dictionary.
	/// </summary>
	private static Type? GetEnumerableElementType(Type type) {
		if (type == typeof(string) || type == typeof(byte[]) || IsJsonObjectContract(type) || AcceptsAnyJson(type)) {
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

	private static bool TryGetProperty(JsonElement node, string name, StringComparison comparison, out JsonElement value) {
		foreach (JsonProperty property in node.EnumerateObject()) {
			if (string.Equals(property.Name, name, comparison)) {
				value = property.Value;
				return true;
			}
		}
		value = default;
		return false;
	}
}
