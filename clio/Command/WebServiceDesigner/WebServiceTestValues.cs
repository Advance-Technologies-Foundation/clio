using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// One value of a test call as <c>CallServiceSchemaService.Execute</c> takes it (Studio's Send test request sends the
/// same shape): <see cref="Value"/> carries the text of a scalar parameter or, for an array of scalars, the list of
/// item texts; <see cref="Nested"/> carries an array of objects, one list of field values per item. Measured on a
/// .NET Framework stand: a text sent for an array goes out one item per character, objects sent in <c>value</c> go
/// out as an empty array, and <c>nested</c> on an array of scalars sends the type name of a dictionary.
/// </summary>
public sealed record ServiceCallParameter {
	/// <summary>The parameter code.</summary>
	[JsonPropertyName("code")]
	public string Code { get; init; }

	/// <summary>A <see cref="string"/>, or an <see cref="IReadOnlyList{T}"/> of strings for an array of scalars.</summary>
	[JsonPropertyName("value")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public object Value { get; init; }

	/// <summary>An array of objects: per item, the values of its fields.</summary>
	[JsonPropertyName("nested")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<IReadOnlyList<ServiceCallParameter>> Nested { get; init; }
}

/// <summary>
/// The parameters of a test call built from the values an agent gives, and every parameter code that got a value.
/// </summary>
/// <param name="Parameters">What the platform's test call is sent.</param>
/// <param name="SentCodes">The codes given a value, at any level of an array item; read by the masking.</param>
public sealed record WebServiceTestCall(IReadOnlyList<ServiceCallParameter> Parameters,
	IReadOnlyCollection<string> SentCodes);

/// <summary>
/// Checks test-call values against a method's request parameter tree and turns them into the platform's test-call
/// parameters. A scalar parameter takes one text, number or boolean; an array of scalars a JSON array of them; an
/// array of objects a JSON array of objects whose keys are the item's field codes, each field taking a value by the
/// same rules (arrays inside items included). A code the method does not have is refused at every level, because
/// the platform drops it silently; null leaves a value out, so the platform fills the parameter's default.
/// </summary>
public static class WebServiceTestValues {

	private const string ObjectType = "Object";

	/// <summary>Builds the test call, or throws one <see cref="ArgumentException"/> naming every problem.</summary>
	/// <param name="method">The called method.</param>
	/// <param name="values">The values by top-level parameter code; may be null.</param>
	/// <returns>The parameters to send and the codes given a value.</returns>
	public static WebServiceTestCall Build(ServiceMethodDto method,
		IReadOnlyDictionary<string, JsonElement> values) {
		var errors = new WebServiceDefinitionBuilder.Errors();
		var sentCodes = new HashSet<string>(StringComparer.Ordinal);
		IReadOnlyCollection<ServiceParameterDto> topLevel = Parameters(method?.Request?.Parameters);
		List<string> unknown = (values ?? new Dictionary<string, JsonElement>()).Keys
			.Where(code => Find(topLevel, code) is null).ToList();
		if (unknown.Count > 0) {
			errors.Add($"parameters: {Quoted(unknown)} is not a top-level request parameter of method "
				+ $"'{method?.Name}'; valid codes: {ValidCodes(topLevel)}.");
		}
		var parameters = new List<ServiceCallParameter>();
		foreach ((string code, JsonElement value) in values ?? new Dictionary<string, JsonElement>()) {
			ServiceParameterDto parameter = Find(topLevel, code);
			ServiceCallParameter built = parameter is null
				? null
				: BuildValue(parameter, value, $"parameters.{code}", errors, sentCodes);
			if (built is not null) {
				parameters.Add(built);
			}
		}
		errors.ThrowIfAny();
		return new WebServiceTestCall(parameters, sentCodes);
	}

	private static ServiceCallParameter BuildValue(ServiceParameterDto parameter, JsonElement value, string where,
		WebServiceDefinitionBuilder.Errors errors, ISet<string> sentCodes) {
		if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) {
			return null;
		}
		bool objectType = string.Equals(parameter.DataValueTypeName, ObjectType, StringComparison.OrdinalIgnoreCase);
		ServiceCallParameter built = (parameter.IsArray, objectType) switch {
			(true, true) => ObjectArray(parameter, value, where, errors, sentCodes),
			(true, false) => ScalarArray(parameter, value, where, errors),
			(false, true) => Refuse(errors, $"{where}: a single Object parameter cannot be given a value in a test call "
				+ "(the platform fills only an array of objects); leave it out."),
			_ => Scalar(parameter, value, where, errors)
		};
		if (built is not null) {
			sentCodes.Add(parameter.Name);
		}
		return built;
	}

	private static ServiceCallParameter Scalar(ServiceParameterDto parameter, JsonElement value, string where,
		WebServiceDefinitionBuilder.Errors errors) {
		string text = Text(value);
		return text is null
			? Refuse(errors, $"{where}: takes one text, number or boolean value, not {Kind(value)}.")
			: new ServiceCallParameter { Code = parameter.Name, Value = text };
	}

	private static ServiceCallParameter ScalarArray(ServiceParameterDto parameter, JsonElement value, string where,
		WebServiceDefinitionBuilder.Errors errors) {
		if (value.ValueKind != JsonValueKind.Array) {
			return Refuse(errors, $"{where}: is an array of {parameter.DataValueTypeName}; send a JSON array such as "
				+ "[\"a\", \"b\"], not " + Kind(value)
				+ (value.ValueKind == JsonValueKind.String ? " (the platform would send one item per character)." : "."));
		}
		var texts = new List<string>();
		int index = 0;
		bool valid = true;
		foreach (JsonElement item in value.EnumerateArray()) {
			string text = Text(item);
			if (text is null) {
				errors.Add($"{where}[{index}]: an item of an array of {parameter.DataValueTypeName} is one text, number or "
					+ $"boolean, not {Kind(item)}" + (item.ValueKind == JsonValueKind.Null ? " (use \"\" for an empty item)." : "."));
				valid = false;
			}
			texts.Add(text);
			index++;
		}
		return valid ? new ServiceCallParameter { Code = parameter.Name, Value = texts } : null;
	}

	private static ServiceCallParameter ObjectArray(ServiceParameterDto parameter, JsonElement value, string where,
		WebServiceDefinitionBuilder.Errors errors, ISet<string> sentCodes) {
		if (value.ValueKind != JsonValueKind.Array) {
			return Refuse(errors, $"{where}: is an array of objects; send a JSON array of objects by field code, such as "
				+ $"[{{\"{FirstCode(parameter)}\": \"...\"}}], not {Kind(value)}"
				+ (value.ValueKind == JsonValueKind.Object ? " (wrap a single item in an array)." : "."));
		}
		IReadOnlyCollection<ServiceParameterDto> fields = Parameters(parameter.ItemProperties);
		var items = new List<IReadOnlyList<ServiceCallParameter>>();
		int index = 0;
		foreach (JsonElement item in value.EnumerateArray()) {
			string itemWhere = $"{where}[{index++}]";
			if (item.ValueKind != JsonValueKind.Object) {
				errors.Add($"{itemWhere}: an item of an array of objects is a JSON object by field code, not {Kind(item)}.");
				continue;
			}
			items.Add(Item(parameter, fields, item, itemWhere, errors, sentCodes));
		}
		return new ServiceCallParameter { Code = parameter.Name, Nested = items };
	}

	private static List<ServiceCallParameter> Item(ServiceParameterDto parameter,
		IReadOnlyCollection<ServiceParameterDto> fields, JsonElement item, string where,
		WebServiceDefinitionBuilder.Errors errors, ISet<string> sentCodes) {
		var built = new List<ServiceCallParameter>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var unknown = new List<string>();
		foreach (JsonProperty property in item.EnumerateObject()) {
			ServiceParameterDto field = Find(fields, property.Name);
			if (field is null) {
				unknown.Add(property.Name);
				continue;
			}
			// The platform refuses a code given twice in one item ("An item with the same key has already been added").
			if (!seen.Add(property.Name)) {
				errors.Add($"{where}: '{property.Name}' is given twice.");
				continue;
			}
			ServiceCallParameter value = BuildValue(field, property.Value, $"{where}.{property.Name}", errors, sentCodes);
			if (value is not null) {
				built.Add(value);
			}
		}
		if (unknown.Count > 0) {
			errors.Add($"{where}: {Quoted(unknown.Distinct(StringComparer.Ordinal))} is not a field of '{parameter.Name}' "
				+ $"items; valid codes: {ValidCodes(fields)}.");
		}
		return built;
	}

	private static ServiceCallParameter Refuse(WebServiceDefinitionBuilder.Errors errors, string message) {
		errors.Add(message);
		return null;
	}

	/// <summary>
	/// The platform's test call takes text and converts it to the parameter's type, so a JSON number or boolean is sent
	/// as its JSON text (1.5, true) and an agent that sends an amount as a number is not refused.
	/// </summary>
	private static string Text(JsonElement value) => value.ValueKind switch {
		JsonValueKind.String => value.GetString(),
		JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
		_ => null
	};

	private static string Kind(JsonElement value) => value.ValueKind switch {
		JsonValueKind.Object => "an object",
		JsonValueKind.Array => "an array",
		JsonValueKind.String => "a text",
		JsonValueKind.Number => "a number",
		JsonValueKind.True or JsonValueKind.False => "a boolean",
		_ => "null"
	};

	private static IReadOnlyCollection<ServiceParameterDto> Parameters(ServiceItemCollection<ServiceParameterDto> items) =>
		items?.Items.Values.Where(p => p is not null).ToList() ?? [];

	private static ServiceParameterDto Find(IReadOnlyCollection<ServiceParameterDto> parameters, string code) =>
		parameters.FirstOrDefault(p => string.Equals(p.Name, code, StringComparison.Ordinal));

	private static string FirstCode(ServiceParameterDto parameter) =>
		Parameters(parameter.ItemProperties).Select(p => p.Name).Order(StringComparer.Ordinal).FirstOrDefault() ?? "code";

	private static string ValidCodes(IReadOnlyCollection<ServiceParameterDto> parameters) =>
		parameters.Count == 0 ? "none" : string.Join(", ", parameters.Select(p => p.Name).Order(StringComparer.Ordinal));

	private static string Quoted(IEnumerable<string> codes) => string.Join(", ", codes.Select(code => $"'{code}'"));
}
