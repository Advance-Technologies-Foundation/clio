using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Reflection-derived registry of every MCP tool registered by clio, used by
/// <c>get-tool-contract</c> as a fallback so that a registered-but-uncurated tool returns a
/// contract synthesized from its own input schema instead of a <c>tool-not-found</c> error.
/// </summary>
internal static class McpToolSchemaCatalog {
	private const string StringType = "string";
	private const string NumberType = "number";
	private const string BooleanType = "boolean";
	private const string ArrayType = "array";
	private const string ObjectType = "object";

	private static readonly Lazy<IReadOnlyDictionary<string, ToolContractDefinition>> SchemaContracts =
		new(BuildSchemaContracts);

	private static readonly Lazy<IReadOnlyDictionary<string, string>> RawDescriptions =
		new(BuildRawDescriptions);

	// The registered-tool-name set is derived once from the (immutable) schema-contract keys and cached:
	// the set is fixed at process start, so re-materializing it on every getter access only wasted an
	// allocation per call.
	private static readonly Lazy<IReadOnlyCollection<string>> RegisteredToolNamesCache =
		new(() => SchemaContracts.Value.Keys.ToArray());

	/// <summary>
	/// Returns the stable set of every registered MCP tool name discovered by reflection.
	/// </summary>
	internal static IReadOnlyCollection<string> RegisteredToolNames => RegisteredToolNamesCache.Value;

	/// <summary>
	/// Tries to synthesize a contract from a registered tool's own input schema.
	/// </summary>
	internal static bool TryGetSchemaContract(string toolName, out ToolContractDefinition contract) =>
		SchemaContracts.Value.TryGetValue(toolName, out contract);

	/// <summary>
	/// Tries to read the tool's RAW reflected description (the <c>[Description]</c> on the MCP tool method)
	/// WITHOUT the uncurated "Auto-generated … no curated contract yet" note that
	/// <see cref="TryGetSchemaContract"/> appends. The compact discovery index uses this so a one-line
	/// purpose reflects only what the tool DOES, never the absence of curation; the full named-contract path
	/// keeps the note via <see cref="TryGetSchemaContract"/> (unchanged).
	/// </summary>
	/// <param name="toolName">The requested MCP tool name.</param>
	/// <param name="description">
	/// The raw tool description (possibly empty when the tool declares none) when the tool is registered;
	/// otherwise empty.
	/// </param>
	/// <returns><c>true</c> when a registered tool matched (even with an empty description); otherwise <c>false</c>.</returns>
	internal static bool TryGetRawDescription(string toolName, out string description) {
		if (RawDescriptions.Value.TryGetValue(toolName, out string raw)) {
			description = raw ?? string.Empty;
			return true;
		}
		description = string.Empty;
		return false;
	}

	private static IReadOnlyDictionary<string, ToolContractDefinition> BuildSchemaContracts() {
		Assembly assembly = typeof(McpToolSchemaCatalog).Assembly;
		Dictionary<string, ToolContractDefinition> contracts = new(StringComparer.OrdinalIgnoreCase);
		foreach (MethodInfo method in EnumerateToolMethods(assembly)) {
			McpServerToolAttribute toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
			string toolName = toolAttribute?.Name;
			if (string.IsNullOrWhiteSpace(toolName) || contracts.ContainsKey(toolName)) {
				continue;
			}
			contracts[toolName] = BuildSchemaContract(toolName, method, assembly);
		}
		return contracts;
	}

	private static IReadOnlyDictionary<string, string> BuildRawDescriptions() {
		Assembly assembly = typeof(McpToolSchemaCatalog).Assembly;
		Dictionary<string, string> descriptions = new(StringComparer.OrdinalIgnoreCase);
		foreach (MethodInfo method in EnumerateToolMethods(assembly)) {
			McpServerToolAttribute toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
			string toolName = toolAttribute?.Name;
			if (string.IsNullOrWhiteSpace(toolName) || descriptions.ContainsKey(toolName)) {
				continue;
			}
			descriptions[toolName] = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
		}
		return descriptions;
	}

	private static IEnumerable<MethodInfo> EnumerateToolMethods(Assembly assembly) {
		foreach (Type type in assembly.GetTypes()) {
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
				if (method.GetCustomAttribute<McpServerToolAttribute>() is not null) {
					yield return method;
				}
			}
		}
	}

	private static ToolContractDefinition BuildSchemaContract(string toolName, MethodInfo method, Assembly assembly) {
		string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
		string note = "Auto-generated from the tool input schema; no curated contract is available for this tool yet.";
		string fullDescription = string.IsNullOrWhiteSpace(description) ? note : $"{description}\n\n{note}";

		(IReadOnlyList<string> required, IReadOnlyList<ToolContractField> properties) =
			BuildInputSchema(method, assembly);

		return new ToolContractDefinition(
			toolName,
			fullDescription,
			new ToolInputSchemaContract(required, properties),
			BuildOutputContract(method),
			new ToolErrorContract([
				new ToolErrorCodeContract("tool-not-found", "Requested tool name is not registered by clio MCP."),
				new ToolErrorCodeContract("missing-required-parameter", "A required parameter is missing."),
				new ToolErrorCodeContract("invalid-parameter-type", "A parameter value type does not match the tool contract.")
			]),
			Aliases: [],
			Defaults: [],
			Examples: [],
			PreferredFlow: new ToolFlowHint([toolName], "Schema-only fallback contract; consult the tool description for sequencing."),
			FallbackFlow: [],
			Deprecations: []);
	}

	private static (IReadOnlyList<string> Required, IReadOnlyList<ToolContractField> Properties) BuildInputSchema(
		MethodInfo method, Assembly assembly) {
		Type argsType = method.GetParameters()
			.Select(parameter => parameter.ParameterType)
			.FirstOrDefault(parameterType =>
				parameterType.IsClass &&
				parameterType != typeof(string) &&
				parameterType.Assembly == assembly);
		if (argsType is null) {
			return ([], []);
		}

		// Positional records carry [property: JsonPropertyName] on the property but often place
		// [Required] / [Description] on the constructor parameter, so merge both sources.
		Dictionary<string, ParameterInfo> constructorParameters = argsType
			.GetConstructors()
			.OrderByDescending(constructor => constructor.GetParameters().Length)
			.FirstOrDefault()
			?.GetParameters()
			.GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
			?? new Dictionary<string, ParameterInfo>(StringComparer.OrdinalIgnoreCase);

		List<string> required = [];
		List<ToolContractField> properties = [];
		foreach (PropertyInfo property in argsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null ||
				property.GetCustomAttribute<JsonIgnoreAttribute>() is not null) {
				continue;
			}
			constructorParameters.TryGetValue(property.Name, out ParameterInfo parameter);
			string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
				?? parameter?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
				?? property.Name;
			string description = property.GetCustomAttribute<DescriptionAttribute>()?.Description
				?? parameter?.GetCustomAttribute<DescriptionAttribute>()?.Description
				?? string.Empty;
			properties.Add(new ToolContractField(name, MapClrType(property.PropertyType), description));
			bool isRequired = property.GetCustomAttribute<RequiredAttribute>() is not null
				|| parameter?.GetCustomAttribute<RequiredAttribute>() is not null;
			if (isRequired) {
				required.Add(name);
			}
		}
		return (required, properties);
	}

	private static ToolOutputContract BuildOutputContract(MethodInfo method) {
		Type returnType = UnwrapTask(method.ReturnType);
		bool isCommandResult = returnType == typeof(CommandExecutionResult);
		return new ToolOutputContract(
			isCommandResult ? "command-execution-result" : "tool-native-response",
			SuccessField: null,
			FailureSignals: isCommandResult
				? ["exit-code != 0", "execution-log-messages[*].message-type == Error"]
				: ["success == false"],
			Fields: []);
	}

	private static Type UnwrapTask(Type type) {
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.Task<>)) {
			return type.GetGenericArguments()[0];
		}
		return type;
	}

	private static string MapClrType(Type type) {
		Type underlying = Nullable.GetUnderlyingType(type) ?? type;
		if (underlying == typeof(string)) {
			return StringType;
		}
		if (underlying == typeof(bool)) {
			return BooleanType;
		}
		if (underlying.IsEnum) {
			return StringType;
		}
		if (underlying == typeof(byte) || underlying == typeof(sbyte) ||
			underlying == typeof(short) || underlying == typeof(ushort) ||
			underlying == typeof(int) || underlying == typeof(uint) ||
			underlying == typeof(long) || underlying == typeof(ulong) ||
			underlying == typeof(float) || underlying == typeof(double) ||
			underlying == typeof(decimal)) {
			return NumberType;
		}
		if (underlying != typeof(string) && typeof(IEnumerable).IsAssignableFrom(underlying)) {
			return ArrayType;
		}
		return ObjectType;
	}
}
