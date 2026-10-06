using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("update-entity-schema", HelpText = "Apply batch column operations to a remote Creatio entity schema")]
public class UpdateEntitySchemaOptions : RemoteCommandOptions
{
	[Option("package", Required = false, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("package-name", Required = false, Hidden = true, HelpText = "Alias for --package")]
	public string? PackageNameAlias {
		get => Package;
		set { if (!string.IsNullOrEmpty(value)) Package = value; }
	}

	[Option("schema-name", Required = false, HelpText = "Entity schema name")]
	public string SchemaName { get; set; }

	[Option("operation", Required = false,
		HelpText = "Structured operation JSON. Pass several values after one --operation; the flag itself cannot be repeated.")]
	public IEnumerable<string> Operations { get; set; }

	[Option("operations", Required = false,
		HelpText = "JSON array of operations, e.g. '[{\"action\":\"add\",...}]'. Applied after the --operation values.")]
	public string? OperationsJson { get; set; }

	/// <summary>
	/// Path of a UTF-8 file holding a JSON array of operations (same format as <see cref="OperationsJson"/>).
	/// Lets multi-line operation JSON reach clio from shells that mangle quotes, such as cmd.exe and
	/// Windows PowerShell 5.1.
	/// </summary>
	[Option("operations-file", Required = false,
		HelpText = "Path to a UTF-8 file with a JSON array of operations (same format as --operations; multi-line allowed). Applied after --operation and --operations.")]
	public string? OperationsFile { get; set; }

	[Option("caption-culture", Required = false, HelpText = "Override the culture used for written column captions/descriptions (e.g. en-US, uk-UA). Precedence: this override > the connected user's profile culture > en-US. Supplying it skips the profile-culture lookup.")]
	public string? CaptionCulture { get; set; }
}

internal sealed record UpdateEntitySchemaOperationDefinition
{
	[JsonPropertyName("action")]
	public string Action { get; init; }

	[JsonPropertyName("column-name")]
	public string ColumnName { get; init; }

	/// <summary>
	/// Alias of <see cref="ColumnName"/>, the field name the MCP tool and create-entity-schema use.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("new-name")]
	public string NewName { get; init; }

	[JsonPropertyName("type")]
	public string Type { get; init; }

	[JsonPropertyName("title")]
	public string Title { get; init; }

	[JsonPropertyName("title-localizations")]
	public Dictionary<string, string>? TitleLocalizations { get; init; }

	[JsonPropertyName("description")]
	public string Description { get; init; }

	[JsonPropertyName("description-localizations")]
	public Dictionary<string, string>? DescriptionLocalizations { get; init; }

	[JsonPropertyName("reference-schema-name")]
	public string ReferenceSchemaName { get; init; }

	[JsonPropertyName("required")]
	public bool? Required { get; init; }

	[JsonPropertyName("indexed")]
	public bool? Indexed { get; init; }

	[JsonPropertyName("cloneable")]
	public bool? Cloneable { get; init; }

	[JsonPropertyName("track-changes")]
	public bool? TrackChanges { get; init; }

	[JsonPropertyName("default-value")]
	public string DefaultValue { get; init; }

	[JsonPropertyName("default-value-source")]
	public string DefaultValueSource { get; init; }

	[JsonPropertyName("default-value-config")]
	public EntitySchemaDefaultValueConfig? DefaultValueConfig { get; init; }

	[JsonPropertyName("multiline-text")]
	public bool? MultilineText { get; init; }

	[JsonPropertyName("localizable-text")]
	public bool? LocalizableText { get; init; }

	[JsonPropertyName("accent-insensitive")]
	public bool? AccentInsensitive { get; init; }

	[JsonPropertyName("masked")]
	public bool? Masked { get; init; }

	[JsonPropertyName("format-validated")]
	public bool? FormatValidated { get; init; }

	[JsonPropertyName("use-seconds")]
	public bool? UseSeconds { get; init; }

	[JsonPropertyName("simple-lookup")]
	public bool? SimpleLookup { get; init; }

	[JsonPropertyName("cascade")]
	public bool? Cascade { get; init; }

	[JsonPropertyName("do-not-control-integrity")]
	public bool? DoNotControlIntegrity { get; init; }

	[JsonPropertyName("usage-type")]
	public string UsageType { get; init; }
}

public class UpdateEntitySchemaCommand : Command<UpdateEntitySchemaOptions>
{
	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	/// <summary>
	/// Longest user-supplied name an error message echoes before it is cut off.
	/// </summary>
	private const int MaxEchoedNameLength = 64;

	/// <summary>
	/// Longest user-supplied file path an error message echoes before it is cut off (the Windows MAX_PATH).
	/// </summary>
	private const int MaxEchoedPathLength = 260;

	/// <summary>
	/// Every top-level field an operation object may carry; anything else is rejected rather than ignored.
	/// Internal so tests can prove every field the MCP tool emits is accepted here.
	/// </summary>
	internal static readonly string[] KnownOperationFields = typeof(UpdateEntitySchemaOperationDefinition)
		.GetProperties(BindingFlags.Public | BindingFlags.Instance)
		.Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
		.Where(name => !string.IsNullOrEmpty(name))
		.ToArray();

	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;
	private readonly IOptionSuggestionService _suggestionService;
	private readonly IFileSystem _fileSystem;

	public UpdateEntitySchemaCommand(IRemoteEntitySchemaColumnManager columnManager, ILogger logger,
		IOptionSuggestionService suggestionService, IFileSystem fileSystem) {
		_columnManager = columnManager;
		_logger = logger;
		_suggestionService = suggestionService;
		_fileSystem = fileSystem;
	}

	public override int Execute(UpdateEntitySchemaOptions options) {
		try {
			options.Operations = ResolveOperations(options);
			Validate(options);
			List<ModifyEntitySchemaColumnOptions> operations = [.. BuildColumnMutations(options)];
			foreach (ModifyEntitySchemaColumnOptions operation in operations) {
				ModifyEntitySchemaColumnCommand.ValidateOptions(operation);
			}
			_columnManager.ModifyColumns(operations);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	// Operation sources are additive and applied in a fixed order: --operation, --operations, --operations-file.
	private IEnumerable<string> ResolveOperations(UpdateEntitySchemaOptions options) {
		List<string> ops = (options.Operations ?? []).ToList();
		if (!string.IsNullOrWhiteSpace(options.OperationsJson)) {
			ops.AddRange(ParseOperationsArray(options.OperationsJson, "--operations value"));
		}
		if (!string.IsNullOrWhiteSpace(options.OperationsFile)) {
			string source = $"--operations-file '{SanitizeForMessage(options.OperationsFile, MaxEchoedPathLength)}'";
			ops.AddRange(ParseOperationsArray(ReadOperationsFile(options.OperationsFile, source), source));
		}
		return ops;
	}

	private string ReadOperationsFile(string path, string source) {
		if (!_fileSystem.ExistsFile(path)) {
			throw new InvalidOperationException($"{source} was not found.");
		}
		// A BOM is tolerated even when the reader does not strip it (Windows PowerShell 5.1 writes one by default).
		return _fileSystem.ReadAllText(path).TrimStart((char)0xFEFF);
	}

	private static IEnumerable<string> ParseOperationsArray(string json, string source) {
		List<JsonElement>? operations;
		try {
			operations = JsonSerializer.Deserialize<List<JsonElement>>(json);
		} catch (JsonException exception) {
			throw new InvalidOperationException($"{source} is not a valid JSON array of operations.", exception);
		}
		return operations?.Select(element => element.GetRawText())
			?? throw new InvalidOperationException($"{source} is not a valid JSON array of operations.");
	}

	private IEnumerable<ModifyEntitySchemaColumnOptions> BuildColumnMutations(UpdateEntitySchemaOptions options) {
		int index = 0;
		foreach (string rawOperation in options.Operations) {
			if (string.IsNullOrWhiteSpace(rawOperation)) {
				throw new InvalidOperationException($"Operation payload at index {index} is empty.");
			}

			UpdateEntitySchemaOperationDefinition operation = ParseOperation(rawOperation, index);
			string columnName = ResolveColumnName(operation, index);
			string? normalizedScalarTitle = NormalizeTitle(operation.Title);
			TitleLocalizationNormalizationResult titleNormalization =
				EntitySchemaDesignerSupport.NormalizeTitleLocalizations(
					operation.TitleLocalizations,
					normalizedScalarTitle,
					"title-localizations");

			yield return new ModifyEntitySchemaColumnOptions {
				Environment = options.Environment,
				Package = options.Package,
				SchemaName = options.SchemaName,
				CaptionCulture = options.CaptionCulture,
				Action = operation.Action,
				ColumnName = columnName,
				NewName = operation.NewName,
				Type = operation.Type,
				Title = titleNormalization.EffectiveTitle,
				TitleLocalizations = titleNormalization.Localizations,
				Description = operation.Description,
				DescriptionLocalizations = operation.DescriptionLocalizations,
				ReferenceSchemaName = operation.ReferenceSchemaName,
				Required = operation.Required,
				Indexed = operation.Indexed,
				Cloneable = operation.Cloneable,
				TrackChanges = operation.TrackChanges,
				DefaultValue = operation.DefaultValue,
				DefaultValueSource = operation.DefaultValueSource,
				DefaultValueConfig = operation.DefaultValueConfig,
				MultilineText = operation.MultilineText,
				LocalizableText = operation.LocalizableText,
				AccentInsensitive = operation.AccentInsensitive,
				Masked = operation.Masked,
				FormatValidated = operation.FormatValidated,
				UseSeconds = operation.UseSeconds,
				SimpleLookup = operation.SimpleLookup,
				Cascade = operation.Cascade,
				DoNotControlIntegrity = operation.DoNotControlIntegrity,
				UsageType = operation.UsageType
			};
			index++;
		}
	}

	// Parses the payload once: unknown fields are rejected BEFORE typed deserialization, so a misspelled field is
	// reported even when another field of the same payload has a value of the wrong type.
	private UpdateEntitySchemaOperationDefinition ParseOperation(string rawOperation, int index) {
		JsonDocument document;
		try {
			document = JsonDocument.Parse(rawOperation);
		} catch (JsonException exception) {
			throw new InvalidOperationException($"Operation payload at index {index} is not valid JSON.", exception);
		}
		using (document) {
			JsonElement root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Null) {
				throw new InvalidOperationException($"Operation payload at index {index} is empty.");
			}
			if (root.ValueKind != JsonValueKind.Object) {
				throw new InvalidOperationException($"Operation payload at index {index} must be a JSON object.");
			}
			RejectUnknownFields(root, index);
			try {
				return root.Deserialize<UpdateEntitySchemaOperationDefinition>(JsonOptions)
					?? throw new InvalidOperationException($"Operation payload at index {index} is empty.");
			} catch (JsonException exception) {
				// The text already parsed, so this is a well-formed value of the wrong type, not malformed JSON.
				string path = SanitizeForMessage(string.IsNullOrEmpty(exception.Path) ? "$" : exception.Path);
				throw new InvalidOperationException(
					$"Operation payload at index {index} has an invalid value at JSON path '{path}'.", exception);
			}
		}
	}

	private void RejectUnknownFields(JsonElement root, int index) {
		foreach (JsonProperty property in root.EnumerateObject()) {
			string name = ReadPropertyName(property, index);
			if (KnownOperationFields.Contains(name, StringComparer.OrdinalIgnoreCase)) {
				continue;
			}
			string suggestion = _suggestionService.SuggestName(name, KnownOperationFields);
			string hint = suggestion is null ? string.Empty : $" Did you mean '{suggestion}'?";
			throw new InvalidOperationException(
				$"Operation payload at index {index} has unknown field '{SanitizeForMessage(name)}'.{hint}");
		}
	}

	// JsonDocument.Parse accepts an escaped lone surrogate such as "\uD800" in a field name; decoding it then throws
	// a serializer InvalidOperationException whose text would otherwise reach the user as the command error.
	private static string ReadPropertyName(JsonProperty property, int index) {
		try {
			return property.Name;
		} catch (InvalidOperationException exception) {
			throw new InvalidOperationException($"Operation payload at index {index} is not valid JSON.", exception);
		}
	}

	/// <summary>
	/// Makes a user-supplied name safe to echo in an error message: the value is read as Unicode scalar values, so
	/// control characters (terminal escape sequences, line breaks) and invisible format characters (bidirectional
	/// overrides such as U+202E, zero-width characters such as U+200B, non-BMP tag characters such as U+E0041) are
	/// removed whether they occupy one UTF-16 unit or a surrogate pair, and a lone surrogate, which is not a character
	/// at all, is dropped. The result is cut to <paramref name="maxLength"/> UTF-16 units (by default
	/// <see cref="MaxEchoedNameLength"/>) without splitting a surrogate pair.
	/// </summary>
	internal static string SanitizeForMessage(string value, int maxLength = MaxEchoedNameLength) {
		StringBuilder printable = new(value.Length);
		ReadOnlySpan<char> remaining = value;
		while (!remaining.IsEmpty) {
			OperationStatus status = Rune.DecodeFromUtf16(remaining, out Rune rune, out int consumed);
			remaining = remaining[consumed..];
			if (status != OperationStatus.Done || Rune.IsControl(rune)
				|| Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) {
				continue;
			}
			printable.Append(rune.ToString());
		}
		if (printable.Length <= maxLength) {
			return printable.ToString();
		}
		int cut = char.IsHighSurrogate(printable[maxLength - 1]) ? maxLength - 1 : maxLength;
		return printable.ToString(0, cut) + "...";
	}

	private static string ResolveColumnName(UpdateEntitySchemaOperationDefinition operation, int index) {
		bool hasColumnName = !string.IsNullOrWhiteSpace(operation.ColumnName);
		bool hasName = !string.IsNullOrWhiteSpace(operation.Name);
		if (hasColumnName && hasName && !string.Equals(operation.ColumnName, operation.Name, StringComparison.Ordinal)) {
			throw new InvalidOperationException(
				$"Operation payload at index {index} sets both 'column-name' ('{SanitizeForMessage(operation.ColumnName)}') and its alias 'name' ('{SanitizeForMessage(operation.Name)}'). Supply only one.");
		}
		return hasName && !hasColumnName ? operation.Name : operation.ColumnName;
	}

	private static string? NormalizeTitle(string? title) {
		if (string.IsNullOrWhiteSpace(title)) {
			return null;
		}
		return title.Trim();
	}

	private static void Validate(UpdateEntitySchemaOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new InvalidOperationException("Package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}
		if (options.Operations == null) {
			throw new InvalidOperationException("At least one operation is required (use --operation, --operations or --operations-file).");
		}

		using IEnumerator<string> enumerator = options.Operations.GetEnumerator();
		if (!enumerator.MoveNext()) {
			throw new InvalidOperationException("At least one operation is required (use --operation, --operations or --operations-file).");
		}
	}
}
