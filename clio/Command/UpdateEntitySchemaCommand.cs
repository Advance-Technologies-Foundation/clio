using System;
using System.Collections.Generic;
using System.Linq;
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
		HelpText = "Structured operation JSON. Repeat the option for multiple values.")]
	public IEnumerable<string> Operations { get; set; }

	[Option("operations", Required = false,
		HelpText = "JSON array of operations, e.g. '[{\"action\":\"add\",...}]'. Alternative to repeating --operation.")]
	public string? OperationsJson { get; set; }
}

internal sealed record UpdateEntitySchemaOperationDefinition
{
	[JsonPropertyName("action")]
	public string Action { get; init; }

	[JsonPropertyName("column-name")]
	public string ColumnName { get; init; }

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
}

public class UpdateEntitySchemaCommand : Command<UpdateEntitySchemaOptions>
{
	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public UpdateEntitySchemaCommand(IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_columnManager = columnManager;
		_logger = logger;
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

	private static IEnumerable<string> ResolveOperations(UpdateEntitySchemaOptions options) {
		List<string> ops = (options.Operations ?? []).ToList();
		if (!string.IsNullOrWhiteSpace(options.OperationsJson)) {
			List<JsonElement> fromJson = JsonSerializer.Deserialize<List<JsonElement>>(options.OperationsJson)
				?? throw new InvalidOperationException("--operations value is not a valid JSON array.");
			ops.AddRange(fromJson.Select(e => e.GetRawText()));
		}
		return ops;
	}

	private static IEnumerable<ModifyEntitySchemaColumnOptions> BuildColumnMutations(UpdateEntitySchemaOptions options) {
		int index = 0;
		foreach (string rawOperation in options.Operations) {
			if (string.IsNullOrWhiteSpace(rawOperation)) {
				throw new InvalidOperationException($"Operation payload at index {index} is empty.");
			}

			UpdateEntitySchemaOperationDefinition operation;
			try {
				operation = JsonSerializer.Deserialize<UpdateEntitySchemaOperationDefinition>(rawOperation, JsonOptions)
					?? throw new InvalidOperationException($"Operation payload at index {index} is empty.");
			} catch (JsonException exception) {
				throw new InvalidOperationException($"Operation payload at index {index} is not valid JSON.", exception);
			}
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
				Action = operation.Action,
				ColumnName = operation.ColumnName,
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
				DoNotControlIntegrity = operation.DoNotControlIntegrity
			};
			index++;
		}
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
			throw new InvalidOperationException("At least one operation is required (use --operation or --operations).");
		}

		using IEnumerator<string> enumerator = options.Operations.GetEnumerator();
		if (!enumerator.MoveNext()) {
			throw new InvalidOperationException("At least one operation is required (use --operation or --operations).");
		}
	}
}
