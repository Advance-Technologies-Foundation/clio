using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CommandLine;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Outcome of binding a free-form JSON args object to a CommandLineParser options type.
/// </summary>
/// <param name="Success">Whether binding produced a valid options instance.</param>
/// <param name="Options">The bound options instance when <paramref name="Success"/> is <c>true</c>.</param>
/// <param name="ErrorText">A user-friendly, parser-derived error message when binding fails.</param>
public sealed record ClioRunBindResult(bool Success, object Options, string ErrorText) {
	/// <summary>Creates a successful bind result.</summary>
	public static ClioRunBindResult Ok(object options) => new(true, options, null);

	/// <summary>Creates a failed bind result carrying the verbatim parser/validation error text.</summary>
	public static ClioRunBindResult Fail(string errorText) => new(false, null, errorText);
}

/// <summary>
/// Binds a free-form JSON args object (kebab-keyed, matching <c>[Option]</c> long names) to a
/// CommandLineParser options type, using the parser as the source of truth for names, aliases,
/// <c>Required</c>, and enum parsing — never raw <c>JsonSerializer.Deserialize&lt;TOptions&gt;</c>.
/// </summary>
public interface IClioRunArgBinder {
	/// <summary>
	/// Flattens <paramref name="args"/> to a <c>--kebab value</c> argv string and re-parses it into
	/// <paramref name="optionsType"/> with <see cref="Parser"/>, surfacing parse errors verbatim.
	/// </summary>
	/// <param name="verb">The canonical verb name (argv[0] the parser dispatches on).</param>
	/// <param name="optionsType">The resolved options type to bind into.</param>
	/// <param name="args">The free-form JSON args object; may be <c>null</c> / undefined.</param>
	/// <returns>A <see cref="ClioRunBindResult"/> describing success or the parse failure.</returns>
	ClioRunBindResult Bind(string verb, Type optionsType, JsonElement? args);
}

/// <summary>
/// Default <see cref="IClioRunArgBinder"/>: JSON object → argv → <c>Parser.ParseArguments</c>.
/// </summary>
public sealed class ClioRunArgBinder : IClioRunArgBinder {

	/// <inheritdoc />
	public ClioRunBindResult Bind(string verb, Type optionsType, JsonElement? args) {
		ArgumentException.ThrowIfNullOrWhiteSpace(verb);
		ArgumentNullException.ThrowIfNull(optionsType);

		List<string> argv;
		try {
			argv = BuildArgv(verb, args);
		}
		catch (ArgumentException ex) {
			return ClioRunBindResult.Fail($"Error: {ex.Message}");
		}

		// A throwaway parser instance with errors captured to a writer rather than Console, so the
		// verbatim parser text can be surfaced in the structured result instead of leaking to stdio.
		using System.IO.StringWriter errorWriter = new();
		Parser parser = new(settings => {
			settings.HelpWriter = errorWriter;
			settings.CaseInsensitiveEnumValues = true;
			settings.AutoHelp = true;
			settings.AutoVersion = false;
		});
		ParserResult<object> result = parser.ParseArguments(argv, optionsType);
		if (result is Parsed<object> parsed) {
			return ClioRunBindResult.Ok(parsed.Value);
		}

		IEnumerable<Error> errors = ((NotParsed<object>)result).Errors;
		string summary = SummarizeErrors(errors, errorWriter.ToString());
		return ClioRunBindResult.Fail($"Error: failed to bind arguments for '{verb}': {summary}");
	}

	private static List<string> BuildArgv(string verb, JsonElement? args) {
		List<string> argv = [verb];
		if (args is not { ValueKind: JsonValueKind.Object } obj) {
			if (args is { ValueKind: JsonValueKind.Null } or null or { ValueKind: JsonValueKind.Undefined }) {
				return argv;
			}
			throw new ArgumentException("'args' must be a JSON object whose keys are kebab-case option names.");
		}

		foreach (JsonProperty property in obj.EnumerateObject()) {
			string key = property.Name;
			if (string.IsNullOrWhiteSpace(key)) {
				throw new ArgumentException("Argument keys cannot be empty.");
			}
			AppendArgument(argv, key, property.Value);
		}
		return argv;
	}

	private static void AppendArgument(List<string> argv, string key, JsonElement value) {
		string flag = $"--{key}";
		switch (value.ValueKind) {
			case JsonValueKind.True:
				argv.Add(flag);
				break;
			case JsonValueKind.False:
				// Omit: an absent boolean flag binds to false.
				break;
			case JsonValueKind.Null or JsonValueKind.Undefined:
				// Omit null values so they fall back to the option's default.
				break;
			case JsonValueKind.Array:
				foreach (JsonElement element in value.EnumerateArray()) {
					argv.Add(flag);
					argv.Add(ScalarToString(element));
				}
				break;
			case JsonValueKind.Object:
				throw new ArgumentException(
					$"Argument '{key}' is a nested object; CLI options accept only scalars or arrays of scalars.");
			default:
				argv.Add(flag);
				argv.Add(ScalarToString(value));
				break;
		}
	}

	private static string ScalarToString(JsonElement element) =>
		element.ValueKind switch {
			JsonValueKind.String => element.GetString() ?? string.Empty,
			JsonValueKind.Number => element.GetRawText(),
			JsonValueKind.True => bool.TrueString,
			JsonValueKind.False => bool.FalseString,
			JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
			_ => throw new ArgumentException(
				$"Array elements must be scalars; got {element.ValueKind}.")
		};

	private static string SummarizeErrors(IEnumerable<Error> errors, string helpText) {
		List<string> parts = [];
		foreach (Error error in errors) {
			switch (error) {
				case UnknownOptionError unknown:
					parts.Add($"unknown argument '--{unknown.Token}'");
					break;
				case MissingRequiredOptionError missing:
					parts.Add($"missing required argument '{DescribeNameInfo(missing.NameInfo)}'");
					break;
				case BadFormatConversionError badFormat:
					parts.Add($"invalid value for '{DescribeNameInfo(badFormat.NameInfo)}'");
					break;
				case MissingValueOptionError missingValue:
					parts.Add($"missing value for '{DescribeNameInfo(missingValue.NameInfo)}'");
					break;
				case SetValueExceptionError setValue:
					parts.Add($"invalid value for '{DescribeNameInfo(setValue.NameInfo)}': {setValue.Exception.Message}");
					break;
				default:
					parts.Add(error.Tag.ToString());
					break;
			}
		}
		if (parts.Count == 0 && !string.IsNullOrWhiteSpace(helpText)) {
			parts.Add(helpText.Trim());
		}
		return parts.Count == 0
			? "invalid arguments"
			: string.Join("; ", parts.Distinct(StringComparer.Ordinal));
	}

	private static string DescribeNameInfo(NameInfo nameInfo) {
		if (!string.IsNullOrEmpty(nameInfo.LongName)) {
			return $"--{nameInfo.LongName}";
		}
		if (!string.IsNullOrEmpty(nameInfo.ShortName)) {
			return $"-{nameInfo.ShortName}";
		}
		return string.IsNullOrEmpty(nameInfo.NameText)
			? "(unnamed)"
			: nameInfo.NameText;
	}
}
