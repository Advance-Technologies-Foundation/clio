using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Converts MCP tool invocation failures that happen before tool method execution into readable tool results.
/// </summary>
public static class McpToolErrorFilter
{
	// Placeholder surfaced in error text when the MCP request carries no tool name (context.Params?.Name is null).
	private const string UnknownToolName = "<unknown>";

	private static readonly JsonSerializerOptions SerializerOptions = BindingsModule.CreateMcpSerializerOptions();

	/// <summary>
	/// Wraps call-tool execution and returns deserialization diagnostics as an MCP error result.
	/// </summary>
	/// <param name="next">Next call-tool handler in the MCP request pipeline.</param>
	/// <returns>Wrapped call-tool handler.</returns>
	public static McpRequestHandler<CallToolRequestParams, CallToolResult> HandleCallToolErrors(
		McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
		async (context, cancellationToken) => {
			if (TryCreateArgumentDeserializationError(context, out CallToolResult? argumentErrorResult)) {
				return argumentErrorResult;
			}
			if (TryCreateMissingCompositeArgumentHint(context, out CallToolResult? hintResult)) {
				return hintResult;
			}
			try {
				// ENG-93373: bound retry-safe (read-only, or the get-page local-write read; never idempotent server writes) tools by a wall-clock
				// response deadline so a stalled Creatio read can never hang indefinitely. On expiry the helper
				// returns a structured error-class=creatio-timeout result telling the agent the call is safe to
				// retry. Destructive tools are excluded — they own their own timeout contract — and fall through
				// to the unbounded call below. Only MATCHED (advertised) tools are classified here; the unmatched
				// long-tail is bounded by the durable handler (McpDurableCallToolHandler) using the same gate.
				if (IsRetrySafeMatchedTool(context)) {
					return await McpReadResponseDeadline.RunAsync(
						context.Params?.Name ?? UnknownToolName,
						token => next(context, token),
						cancellationToken).ConfigureAwait(false);
				}
				return await next(context, cancellationToken);
			}
			catch (OperationCanceledException) {
				// Honour cooperative cancellation/timeout — let the host see a cancellation, not a tool error.
				throw;
			}
			catch (Exception ex) {
				// Without this, an unhandled tool-method exception reaches the SDK's default handler, which
				// returns a generic "An error occurred invoking '<tool>'" with no detail — so an agent cannot
				// see WHY the call failed (e.g. "Environment ... not found") and cannot self-correct. Surface
				// the real (inner-most) message as a structured error result for EVERY tool uniformly — but
				// redacted, because this text lands in the model/host transcript and inner-most messages
				// routinely carry absolute paths, request URIs (target hosts), and credentials.
				return CreateJsonErrorResult(
					$"MCP tool '{context.Params?.Name ?? UnknownToolName}' failed: {SensitiveErrorTextRedactor.Redact(GetSurfacedMessage(ex))}");
			}
		};

	// True when the matched (advertised) tool is retry-safe and therefore eligible for the read-response
	// deadline (ENG-93373). MatchedPrimitive is null for an unmatched name — those are bounded by the
	// durable handler instead, so this returns false and the call falls through unbounded here.
	private static bool IsRetrySafeMatchedTool(RequestContext<CallToolRequestParams> context) =>
		context.MatchedPrimitive is McpServerTool tool
		&& McpReadDeadlineGate.IsRetrySafe(tool.ProtocolTool.Name, tool.ProtocolTool.Annotations);

	// Message selection lives in Clio.Common.SurfacedExceptionMessage, shared with the nested clio-run
	// dispatcher so both MCP error paths surface the same text (ENG-93365).
	private static string GetSurfacedMessage(Exception exception) =>
		Clio.Common.SurfacedExceptionMessage.Resolve(exception);

	internal static bool TryCreateArgumentDeserializationError(
		RequestContext<CallToolRequestParams> context,
		out CallToolResult? result) {
		result = null;
		if (context.Params?.Arguments is not { } arguments) {
			return false;
		}

		if (!TryGetToolMethod(context, out MethodInfo? method)) {
			return false;
		}

		foreach (ParameterInfo parameter in method.GetParameters()) {
			string argumentName = GetArgumentName(parameter);
			if (!arguments.TryGetValue(argumentName, out JsonElement argumentValue)) {
				continue;
			}
			//JsonElement.Deserialize happily returns null for a reference type, so {"args":null} never
			//threw and the tool ran with a null composite argument - odata-read answered with a typed
			//NRE-derived success:false while the same call through clio-run reported a missing required
			//argument. A required, non-nullable parameter rejects an explicit JSON null here so both
			//paths return the same invalid-parameter-type contract. Optional and nullable parameters
			//are untouched: null is a legitimate value for them.
			if (argumentValue.ValueKind == JsonValueKind.Null && IsRequiredNonNullable(parameter)) {
				result = CreateJsonErrorResult(BuildNullArgumentErrorMessage(
					context.Params.Name, parameter.ParameterType, argumentName));
				return true;
			}
			try {
				argumentValue.Deserialize(parameter.ParameterType, SerializerOptions);
			}
			catch (Exception ex) when (IsDeserializationException(ex)) {
				result = CreateJsonErrorResult(BuildDeserializationErrorMessage(
					context.Params.Name,
					parameter.ParameterType,
					argumentName,
					ex));
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// True when the parameter must carry a value: it is marked <see cref="RequiredAttribute"/>, has no
	/// default, and its type is not a nullable one. A nullable or defaulted parameter accepts null.
	/// </summary>
	private static bool IsRequiredNonNullable(ParameterInfo parameter) {
		if (parameter.GetCustomAttribute<RequiredAttribute>() is null || parameter.HasDefaultValue) {
			return false;
		}
		Type type = parameter.ParameterType;
		if (Nullable.GetUnderlyingType(type) is not null) {
			return false;
		}
		//A reference type declared as nullable (`Args?`) is annotated rather than a distinct CLR type,
		//so the nullable context of the declaration is what tells them apart.
		return new NullabilityInfoContext().Create(parameter).WriteState != NullabilityState.Nullable;
	}

	private static string BuildNullArgumentErrorMessage(string toolName, Type parameterType, string argumentName) =>
		$"invalid-parameter-type: argument '{argumentName}' for MCP tool '{toolName}' must be "
		+ $"{GetExpectedJsonType(parameterType, argumentName)}. Received a JSON null, and the argument "
		+ "is required.";

	private static CallToolResult CreateJsonErrorResult(string message) {
		return new CallToolResult {
			IsError = true,
			Content = [
				new TextContentBlock {
					Text = message
				}
			]
		};
	}

	private static string GetArgumentName(ParameterInfo parameter) =>
		parameter.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
		?? parameter.Name
		?? string.Empty;

	private static string BuildDeserializationErrorMessage(
		string? toolName,
		Type parameterType,
		string? argumentName,
		Exception exception) {
		string preciseArgumentName = GetPreciseArgumentName(parameterType, argumentName, exception);
		string toolLabel = toolName ?? UnknownToolName;
		string message;
		if (string.IsNullOrWhiteSpace(argumentName)) {
			message = $"invalid-parameter-type: arguments for MCP tool '{toolLabel}' must match the documented shape "
				+ $"(expected {GetExpectedJsonType(parameterType, preciseArgumentName)}).";
		} else if (IsNestedBindingFailure(parameterType, exception)) {
			// The named property's OWN value is not what failed: the caller did send the array or object
			// the contract asks for, and the incompatible value sits somewhere inside it. Reporting the
			// CLR type of the outer property here ("must be an array") names a correction the caller has
			// already made, so the message says where to look instead.
			message = $"invalid-parameter-type: argument '{preciseArgumentName}' for MCP tool '{toolLabel}' "
				+ "contains a value that does not match the documented shape.";
		} else {
			message = $"invalid-parameter-type: argument '{preciseArgumentName}' for MCP tool '{toolLabel}' "
				+ $"must be {GetExpectedJsonType(parameterType, preciseArgumentName)}.";
		}
		return $"{message} Received an incompatible JSON value.";
	}

	/// <summary>
	/// True when the failing JSON path continues BELOW the named property, so the incompatible value is
	/// nested inside it rather than being that property's own value.
	/// </summary>
	/// <remarks>
	/// <see cref="GetPreciseArgumentName"/> reports only the first path segment, so
	/// <c>$.rules[0].actions[0].type</c> is reported as <c>rules</c>. Without this distinction the message
	/// then describes the CLR type of <c>rules</c>, which the caller already supplied correctly.
	/// </remarks>
	private static bool IsNestedBindingFailure(Type parameterType, Exception exception) {
		if (!parameterType.IsClass || parameterType == typeof(string)
			|| exception is not JsonException { Path: { } path }
			|| !path.StartsWith("$.", StringComparison.Ordinal)) {
			return false;
		}
		string remainder = path[2..];
		int boundary = remainder.IndexOfAny(['.', '[']);
		if (boundary < 0) {
			return false;
		}
		// Only when the first segment actually resolved to a documented property. Otherwise the reported
		// name is the outer argument itself and "must be <type>" remains the accurate advice.
		return GetJsonPropertyNames(parameterType)
			.Contains(remainder[..boundary], StringComparer.OrdinalIgnoreCase);
	}

	private static string GetPreciseArgumentName(Type parameterType, string? argumentName, Exception exception) {
		if (parameterType.IsClass && parameterType != typeof(string)
			&& exception is JsonException { Path: { } path }
			&& path.StartsWith("$.", StringComparison.Ordinal)) {
			string propertyName = path[2..].Split(['.', '['], 2)[0];
			if (GetJsonPropertyNames(parameterType).Contains(propertyName, StringComparer.OrdinalIgnoreCase)) {
				return propertyName;
			}
		}
		return argumentName ?? string.Empty;
	}

	private static string GetExpectedJsonType(Type parameterType, string argumentName) {
		PropertyInfo? property = parameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(candidate =>
				(GetJsonPropertyName(candidate) ?? candidate.Name).Equals(argumentName, StringComparison.OrdinalIgnoreCase));
		Type type = property?.PropertyType ?? parameterType;
		if (Nullable.GetUnderlyingType(type) is { } underlyingType) {
			type = underlyingType;
		}
		if (type == typeof(string)) return "a string";
		if (type == typeof(bool)) return "a boolean";
		// A dictionary IS an IEnumerable, so it has to be recognized BEFORE the enumerable branch below.
		// Without this, a contract taking Dictionary<string, JsonElement> — clio-run's own `args` — was
		// reported as "an array", telling the caller to resend the very shape that had just failed.
		if (IsJsonObjectContract(type)) return "an object";
		if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return "an array";
		if (type.IsPrimitive || type == typeof(decimal)) return "a number";
		return "an object";
	}

	/// <summary>
	/// True when the CLR type is carried on the wire as a JSON object rather than a JSON array.
	/// </summary>
	private static bool IsJsonObjectContract(Type type) =>
		typeof(System.Collections.IDictionary).IsAssignableFrom(type)
		//Type.GetInterfaces() lists the interfaces a type implements, never the type itself, so a
		//property declared AS IReadOnlyDictionary<string,string> - ApplicationCreateArgs'
		//title-localizations, for one - fell through to the IEnumerable branch and was reported as
		//"an array". The declared type has to be tested on its own before the implemented ones.
		|| IsDictionaryDefinition(type)
		|| type.GetInterfaces().Any(IsDictionaryDefinition);

	private static bool IsDictionaryDefinition(Type candidate) =>
		candidate.IsGenericType
		&& (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
			|| candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

	private static string? GetJsonPropertyName(PropertyInfo property) =>
		property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;

	private static bool IsDeserializationException(Exception exception) =>
		exception is JsonException or NotSupportedException;

	/// <summary>
	/// Detects when a caller sends arguments flat (e.g. <c>{"environment-name": "..."}</c>) instead of
	/// wrapping them in the composite parameter object (e.g. <c>{"args": {"environment-name": "..."}}</c>).
	/// </summary>
	internal static bool TryCreateMissingCompositeArgumentHint(
		RequestContext<CallToolRequestParams> context,
		out CallToolResult? result) {
		result = null;
		if (context.Params?.Arguments is not { Count: > 0 } arguments) {
			return false;
		}

		if (!TryGetToolMethod(context, out MethodInfo? method)) {
			return false;
		}

		return TryDetectFlatArgsMismatch(context.Params.Name, method, arguments, out result);
	}

	/// <summary>
	/// Extracts the tool implementation <see cref="MethodInfo"/> from the matched MCP primitive.
	/// </summary>
	private static bool TryGetToolMethod(
		RequestContext<CallToolRequestParams> context,
		[NotNullWhen(true)] out MethodInfo? method) {
		method = context.MatchedPrimitive is McpServerTool tool
			? tool.Metadata.OfType<MethodInfo>().FirstOrDefault()
			: null;
		return method is not null;
	}

	/// <summary>
	/// Core detection: checks whether <paramref name="arguments"/> contains flat keys that belong
	/// inside a composite method parameter instead of at the top level.
	/// </summary>
	internal static bool TryDetectFlatArgsMismatch(
		string? toolName,
		MethodInfo method,
		IDictionary<string, JsonElement> arguments,
		out CallToolResult? result) {
		result = null;

		foreach (ParameterInfo parameter in method.GetParameters()) {
			string argumentName = GetArgumentName(parameter);

			if (arguments.ContainsKey(argumentName)) {
				continue;
			}

			if (IsFrameworkParameter(parameter.ParameterType)) {
				continue;
			}

			List<string> propertyNames = GetJsonPropertyNames(parameter.ParameterType);
			if (propertyNames.Count == 0) {
				continue;
			}

			List<string> matchedKeys = propertyNames
				.Where(arguments.ContainsKey)
				.ToList();

			if (matchedKeys.Count > 0) {
				result = CreateJsonErrorResult(
					BuildMissingWrapperMessage(toolName, argumentName, propertyNames, matchedKeys));
				return true;
			}
		}

		return false;
	}

	private static bool IsFrameworkParameter(Type type) =>
		type == typeof(CancellationToken)
		|| type.Namespace?.StartsWith("ModelContextProtocol", StringComparison.Ordinal) == true;

	private static List<string> GetJsonPropertyNames(Type type) {
		if (!type.IsClass || type == typeof(string)) {
			return [];
		}
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(IsWireContractProperty)
			.Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
			.ToList();
	}

	private static bool IsWireContractProperty(PropertyInfo property) =>
		property.GetCustomAttribute<JsonExtensionDataAttribute>() is null
		&& property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always;

	private static string BuildMissingWrapperMessage(
		string? toolName, string wrapperName, List<string> allProperties, List<string> matchedKeys) {
		string flatKeysDisplay = string.Join(", ", matchedKeys.Select(k => $"\"{k}\""));
		string exampleInner = string.Join(", ", allProperties.Select(k => $"\"{k}\": \"...\""));
		return $"Tool '{toolName ?? UnknownToolName}' expects arguments wrapped inside "
			+ $"an \"{wrapperName}\" object, but received {flatKeysDisplay} at the top level. "
			+ $"Correct format: {{\"{wrapperName}\": {{{exampleInner}}}}}";
	}
}
