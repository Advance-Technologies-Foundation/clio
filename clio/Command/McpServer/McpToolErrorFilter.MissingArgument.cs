using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Refuses a call that leaves out an argument the tool requires. The refusal names the argument, the keys
/// the caller sent in its place and the arguments the tool takes, instead of the SDK's "The arguments
/// dictionary is missing a value for the required parameter 'environmentName'. (Parameter 'arguments')".
/// </summary>
/// <remarks>
/// <para>
/// The usual cause is a tool with flat parameters named in camelCase, such as <c>get-fsm-mode</c>'s
/// <c>environmentName</c>, called with the kebab-case spelling every args-record tool uses
/// (<c>environment-name</c>). The SDK's text named the parameter but not what was wrong with the call, and it
/// read like a failure inside clio. An agent retried with the same key.
/// </para>
/// <para>
/// What is required comes from the tool's own input schema, the contract <c>tools/list</c> advertises. The SDK
/// writes that schema by the rule it binds by: a parameter with no default value is required, a nullable one
/// included. It binds a flat parameter by its exact name, case included. So this refuses exactly the calls the
/// SDK would refuse before the tool body runs, and no other. Parameters the SDK fills from services or from the
/// request context are not in the schema, so they are never reported missing.
/// </para>
/// <para>
/// Caller-supplied text reaches the message only as key names, through
/// <see cref="McpToolArgumentSupport.DescribeCallerKey"/> and capped at
/// <see cref="McpToolArgumentSupport.MaxEchoedKeys"/>. A value never does.
/// </para>
/// </remarks>
public static partial class McpToolErrorFilter
{
	/// <summary>One argument a tool's input schema advertises.</summary>
	/// <param name="Name">The name the SDK binds it by.</param>
	/// <param name="Type">Its JSON type as the contract states it: string, boolean, object, ...</param>
	/// <param name="Required">Whether the schema lists it as required.</param>
	/// <param name="Description">Its description, when the schema carries one.</param>
	private sealed record SchemaArgument(string Name, string Type, bool Required, string? Description);

	/// <summary>
	/// Refuses the call when it leaves out an argument the tool's input schema marks required, or returns
	/// <see langword="false"/> so the call proceeds.
	/// </summary>
	/// <param name="context">The call, with <see cref="RequestContext{T}.MatchedPrimitive"/> set to the tool it
	/// will invoke. A <c>clio-run</c> dispatch passes the retargeted child context.</param>
	/// <param name="result">The refusal, when the call is refused.</param>
	/// <returns><see langword="true"/> when the call must not run.</returns>
	/// <remarks>
	/// Runs after every other argument check, so a payload one of them already explains keeps that
	/// explanation. It reads the schema only, so a failure of its own would mean a schema it cannot read; that
	/// yields no refusal and the SDK's own binding decides, as before.
	/// </remarks>
	internal static bool TryCreateMissingArgumentError(
		RequestContext<CallToolRequestParams> context,
		[NotNullWhen(true)] out CallToolResult? result) {
		result = null;
		if (context.Params is not { } parameters || context.MatchedPrimitive is not McpServerTool tool) {
			return false;
		}
		try {
			string? message = BuildMissingArgumentMessage(context, parameters, tool);
			if (message is null) {
				return false;
			}
			result = CreateJsonErrorResult(message);
			return true;
		}
		catch (Exception diagnosticFailure) when (!McpExceptionPolicy.IsUnrecoverable(diagnosticFailure)) {
			return false;
		}
	}

	private static string? BuildMissingArgumentMessage(
		RequestContext<CallToolRequestParams> context,
		CallToolRequestParams parameters,
		McpServerTool tool) {
		JsonElement schema = tool.ProtocolTool.InputSchema;
		if (schema.ValueKind != JsonValueKind.Object) {
			return null;
		}
		List<string> required = McpToolRegistrySchemaContract.ReadRequired(schema);
		if (required.Count == 0) {
			return null;
		}
		// Ordinal on purpose: the SDK copies the arguments into its own ordinal dictionary before it binds, so
		// a key that differs only in case is not the parameter, whatever comparer the caller's dictionary had.
		List<string> sentKeys = [.. parameters.Arguments?.Keys ?? []];
		HashSet<string> sent = new(sentKeys, StringComparer.Ordinal);
		List<string> missing = [.. required.Where(name => !sent.Contains(name))];
		if (missing.Count == 0) {
			return null;
		}

		IReadOnlyList<SchemaArgument> arguments = ReadSchemaArguments(schema, required);
		HashSet<string> known = new(arguments.Select(argument => argument.Name), StringComparer.Ordinal);
		List<string> unsupplied = [.. arguments.Select(argument => argument.Name).Where(name => !sent.Contains(name))];
		List<string> unknown = [.. sentKeys.Where(key => !known.Contains(key))];
		string toolLabel = parameters.Name ?? UnknownToolName;
		string first = missing[0];

		StringBuilder message = new(
			$"invalid-parameter-type: argument '{first}' for MCP tool '{toolLabel}' is required and was not sent.");
		if (missing.Count > 1) {
			message.Append(" Also missing: ")
				.Append(string.Join(", ", missing.Skip(1).Select(name => $"'{name}'")))
				.Append('.');
		}
		if (unknown.Count > 0) {
			// Only the keys shown get a suggestion: the edit distance is not free, and a payload of many keys
			// would otherwise pay it for names the message never prints.
			int hidden = unknown.Count - Math.Min(unknown.Count, McpToolArgumentSupport.MaxEchoedKeys);
			message.Append(unknown.Count == 1 ? " Unknown argument " : " Unknown arguments ")
				.Append(string.Join(", ", unknown.Take(McpToolArgumentSupport.MaxEchoedKeys)
					.Select(key => DescribeUnknownArgument(key, unsupplied))))
				.Append(hidden > 0 ? $" and {hidden} more." : ".");
		}
		// Quoted as every hint name is, so the redaction on the clio-run and durable paths leaves it alone; see
		// QuoteLocation.
		if (FirstSentence(arguments.FirstOrDefault(argument => argument.Name == first)?.Description) is { } about) {
			message.Append($" About {QuoteLocation(first)}: {about}");
		}
		if (DescribeArgumentShape(context, first) is { } shape) {
			message.Append($" Expected {QuoteLocation(first)}: {shape}.");
		}
		message.Append($" {toolLabel} takes: {DescribeArgumentList(arguments)}.")
			.Append(" Argument names are matched exactly, case included. Nothing ran.");
		return message.ToString();
	}

	/// <summary>The schema's arguments in their declared order, each marked required or not.</summary>
	private static IReadOnlyList<SchemaArgument> ReadSchemaArguments(JsonElement schema, IReadOnlyList<string> required) {
		if (!schema.TryGetProperty("properties", out JsonElement properties)
			|| properties.ValueKind != JsonValueKind.Object) {
			return [.. required.Select(name => new SchemaArgument(name, string.Empty, true, null))];
		}
		HashSet<string> requiredSet = new(required, StringComparer.Ordinal);
		return [.. properties.EnumerateObject().Select(property => new SchemaArgument(
			property.Name,
			McpToolRegistrySchemaContract.ReadType(property.Value),
			requiredSet.Contains(property.Name),
			property.Value.ValueKind == JsonValueKind.Object
				&& property.Value.TryGetProperty("description", out JsonElement description)
				&& description.ValueKind == JsonValueKind.String
					? description.GetString()
					: null))];
	}

	/// <summary>
	/// The arguments a tool takes, in one line: <c>environmentName (string, required), verbose (boolean)</c>,
	/// capped at <see cref="MaxShapeMembers"/>.
	/// </summary>
	private static string DescribeArgumentList(IReadOnlyList<SchemaArgument> arguments) {
		if (arguments.Count == 0) {
			return "no arguments";
		}
		string list = string.Join(", ", arguments.Take(MaxShapeMembers).Select(DescribeArgument));
		return arguments.Count > MaxShapeMembers ? $"{list} and {arguments.Count - MaxShapeMembers} more" : list;
	}

	private static string DescribeArgument(SchemaArgument argument) {
		List<string> traits = [];
		if (!string.IsNullOrEmpty(argument.Type)) {
			traits.Add(argument.Type);
		}
		if (argument.Required) {
			traits.Add("required");
		}
		return traits.Count == 0 ? argument.Name : $"{argument.Name} ({string.Join(", ", traits)})";
	}

	/// <summary>
	/// The expected object of a missing composite argument - <c>an object with entity (required), filters, ...</c>
	/// - or <see langword="null"/> when the argument is a scalar, whose type the argument list already gives.
	/// </summary>
	private static string? DescribeArgumentShape(RequestContext<CallToolRequestParams> context, string argumentName) {
		if (!TryGetToolMethod(context, out MethodInfo? method)) {
			return null;
		}
		ParameterInfo? parameter = method.GetParameters().FirstOrDefault(candidate =>
			McpToolArgumentSupport.IsBindableToolParameter(candidate)
			&& string.Equals(GetArgumentName(candidate), argumentName, StringComparison.Ordinal));
		return parameter is null ? null : DescribeShape(UnwrapNullable(parameter.ParameterType));
	}

	/// <summary>
	/// Renders one unknown key the caller sent, with the argument it most likely meant among those it did NOT
	/// send: <c>'environment-name' (did you mean 'environmentName'?)</c>.
	/// </summary>
	private static string DescribeUnknownArgument(string key, IReadOnlyList<string> unsupplied) {
		string rendered = $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'";
		string? suggestion = SuggestName(key, unsupplied);
		return suggestion is null ? rendered : $"{rendered} (did you mean '{suggestion}'?)";
	}
}
