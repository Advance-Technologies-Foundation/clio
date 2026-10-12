using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// The short "what this write tool needs" answer returned when a write tool is called with an empty
/// argument object or without one of its required arguments (ENG-103025). It replaces a bare
/// "name is required." with the required arguments and one minimal valid call, so the next attempt
/// can be correct instead of a second probe.
/// </summary>
/// <remarks>
/// Kept short on purpose (<see cref="MaxLength"/> characters): the text travels in an error envelope on every
/// probe, and the full contract stays one <c>get-tool-contract</c> call away. The examples are real argument
/// objects and are pinned by tests that bind them to the tool's own args record, so they cannot drift into
/// advice the tool would refuse.
/// </remarks>
internal static class ToolMiniContract {

	private const string RegisteredEnvironmentLine = "environment-name (registered environment)";

	/// <summary>Upper bound for one rendered mini-contract, in characters (about 1.5 KB of ASCII).</summary>
	internal const int MaxLength = 1500;

	/// <summary>Minimal valid <c>args</c> object of <c>create-app</c>.</summary>
	internal const string CreateAppExample =
		"{\"environment-name\":\"<env>\",\"name\":\"Todo list\",\"code\":\"TodoList\"}";

	/// <summary>Minimal valid <c>args</c> object of <c>create-business-process</c>.</summary>
	internal const string CreateBusinessProcessExample =
		"{\"environment-name\":\"<env>\",\"descriptor\":{\"name\":\"UsrDemoProcess\",\"caption\":\"Demo process\","
		+ "\"packageName\":\"<package>\",\"elements\":[{\"name\":\"Start\",\"type\":\"startEvent\",\"caption\":\"Start\"},"
		+ "{\"name\":\"End\",\"type\":\"endEvent\",\"caption\":\"End\"}],\"flows\":[{\"source\":\"Start\",\"target\":\"End\"}]}}";

	/// <summary>Minimal valid <c>args</c> object of <c>modify-business-process</c>.</summary>
	internal const string ModifyBusinessProcessExample =
		"{\"environment-name\":\"<env>\",\"process-name\":\"UsrDemoProcess\","
		+ "\"operations\":[{\"op\":\"removeElement\",\"elementName\":\"NotifyAccountOwner\"}]}";

	/// <summary>Minimal valid <c>args</c> object of <c>create-entity-schema</c>.</summary>
	internal const string CreateEntitySchemaExample =
		"{\"environment-name\":\"<env>\",\"package-name\":\"<package>\",\"schema-name\":\"UsrOrder\","
		+ "\"title-localizations\":{\"en-US\":\"Order\"},\"columns\":[{\"name\":\"UsrAmount\",\"type\":\"Float\","
		+ "\"title-localizations\":{\"en-US\":\"Amount\"}}]}";

	/// <summary>
	/// Renders the mini-contract.
	/// </summary>
	/// <param name="toolName">The tool the caller invoked.</param>
	/// <param name="missing">The required arguments the call lacked; empty means the whole argument object was empty.</param>
	/// <param name="required">One line per required argument: name and what it is.</param>
	/// <param name="exampleArgs">A minimal valid argument object.</param>
	internal static string Build(
		string toolName, IReadOnlyList<string> missing, IReadOnlyList<string> required, string exampleArgs) {
		string noun = missing.Count == 1 ? "argument" : "arguments";
		string lead = missing.Count == 0
			? $"{toolName} was called with no arguments."
			: $"{toolName}: missing required {noun} " + string.Join(", ", missing.Select(name => $"'{name}'")) + ".";
		string text = $"{lead} Required: {string.Join("; ", required)}. Minimal valid arguments: {exampleArgs}";
		return text.Length <= MaxLength ? text : text[..MaxLength];
	}

	/// <summary>
	/// Appends <paramref name="contract"/> to a "... is required" refusal, keeping its exit code and its leading
	/// sentence exactly as before: callers and tests branch on both, so the mini-contract only ADDS to them.
	/// </summary>
	/// <param name="refusal">The refusal the tool already produces.</param>
	/// <param name="contract">The rendered mini-contract.</param>
	internal static CommandExecutionResult Decorate(CommandExecutionResult refusal, string contract) {
		string? text = refusal.Output?.FirstOrDefault()?.Value?.ToString();
		if (text is null || !text.Contains("is required", StringComparison.Ordinal)) {
			return refusal;
		}
		return refusal with { Output = [new Clio.Common.ErrorMessage($"{text} {contract}")] };
	}

	/// <summary>The mini-contract of <c>create-app</c>.</summary>
	internal static string ForCreateApp(IReadOnlyList<string> missing) => Build(ApplicationCreateTool.ApplicationCreateToolName,
		missing,
		[
			"name (display name)", "code (business part of the app code; the schema prefix is added for you)",
			RegisteredEnvironmentLine
		],
		CreateAppExample);

	/// <summary>The mini-contract of <c>create-business-process</c>.</summary>
	internal static string ForCreateBusinessProcess(IReadOnlyList<string> missing) => Build("create-business-process",
		missing,
		[
			RegisteredEnvironmentLine,
			"descriptor (JSON object: name, caption, packageName, elements[], flows[]); read get-guidance name=process-modeling for the rest"
		],
		CreateBusinessProcessExample);

	/// <summary>The mini-contract of <c>modify-business-process</c>.</summary>
	internal static string ForModifyBusinessProcess(IReadOnlyList<string> missing) => Build("modify-business-process",
		missing,
		[
			RegisteredEnvironmentLine, "process-name OR process-uid (exactly one)",
			"operations (JSON array of {op, ...} objects)"
		],
		ModifyBusinessProcessExample);

	/// <summary>The mini-contract of <c>create-entity-schema</c>.</summary>
	internal static string ForCreateEntitySchema(IReadOnlyList<string> missing) => Build("create-entity-schema",
		missing,
		[
			RegisteredEnvironmentLine, "package-name (existing package)",
			"schema-name (with the environment's prefix)", "title-localizations (object with an en-US value)",
			"columns (optional)"
		],
		CreateEntitySchemaExample);
}
