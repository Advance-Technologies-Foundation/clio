using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for modifying a remote entity schema column.
/// </summary>
[Verb("modify-entity-schema-column", HelpText = "Add, modify, or remove a column in a remote Creatio entity schema")]
public class ModifyEntitySchemaColumnOptions : RemoteCommandOptions
{
	[Option("package", Required = false, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("package-name", Required = false, Hidden = true, HelpText = "Alias for --package")]
	public string? PackageNameAlias {
		get => Package;
		set { if (!string.IsNullOrEmpty(value)) Package = value; }
	}

	[Option("schema-name", Required = true, HelpText = "Entity schema name")]
	public string SchemaName { get; set; }

	[Option("action", Required = true, HelpText = "Column action: add, modify, or remove")]
	public string Action { get; set; }

	[Option("column-name", Required = true, HelpText = "Target column name")]
	public string ColumnName { get; set; }

	[Option("new-name", Required = false, HelpText = "New column name for rename operations")]
	public string NewName { get; set; }

	[Option("type", Required = false, HelpText = """
												 Column type. Supported values:
												 Guid, Integer, Float, Boolean, Date, DateTime, Time, Lookup,
												 Text, ShortText, MediumText, LongText, MaxSizeText,
												 Text50, Text250, Text500, TextUnlimited, PhoneNumber, WebLink, Email, RichText, 
												 Decimal0, Decimal1, Decimal2, Decimal3, Decimal4, Decimal8, 
												 Currency0, Currency1, Currency2, Currency3
												 """)]
	public string Type { get; set; }

	[Option("title", Required = false, HelpText = "Column title/caption")]
	public string Title { get; set; }

	public IReadOnlyDictionary<string, string>? TitleLocalizations { get; set; }

	[Option("description", Required = false, HelpText = "Column description")]
	public string Description { get; set; }

	public IReadOnlyDictionary<string, string>? DescriptionLocalizations { get; set; }

	[Option("reference-schema", Required = false, HelpText = "Lookup reference schema name")]
	public string ReferenceSchemaName { get; set; }

	[Option("required", Required = false, HelpText = "Set required flag")]
	public bool? Required { get; set; }

	[Option("indexed", Required = false, HelpText = "Set indexed flag")]
	public bool? Indexed { get; set; }

	[Option("cloneable", Required = false, HelpText = "Set make-copy flag")]
	public bool? Cloneable { get; set; }

	[Option("track-changes", Required = false, HelpText = "Set update-change-log flag")]
	public bool? TrackChanges { get; set; }

	[Option("default-value", Required = false, HelpText = "Set a constant default value")]
	public string DefaultValue { get; set; }

	[Option("default-value-source", Required = false, HelpText = "Default value source: Const or None")]
	public string DefaultValueSource { get; set; }

	/// <summary>
	/// Gets or sets the structured default value metadata used by MCP mutation flows.
	/// </summary>
	public EntitySchemaDefaultValueConfig? DefaultValueConfig { get; set; }

	[Option("multiline-text", Required = false, HelpText = "Set multi-line text flag")]
	public bool? MultilineText { get; set; }

	[Option("localizable-text", Required = false, HelpText = "Set localizable text flag")]
	public bool? LocalizableText { get; set; }

	[Option("accent-insensitive", Required = false, HelpText = "Set accent-insensitive flag")]
	public bool? AccentInsensitive { get; set; }

	[Option("masked", Required = false, HelpText = "Set masked flag")]
	public bool? Masked { get; set; }

	[Option("format-validated", Required = false, HelpText = "Set format-validated flag")]
	public bool? FormatValidated { get; set; }

	[Option("use-seconds", Required = false, HelpText = "Set use-seconds flag")]
	public bool? UseSeconds { get; set; }

	[Option("simple-lookup", Required = false, HelpText = "Set simple-lookup flag")]
	public bool? SimpleLookup { get; set; }

	[Option("cascade", Required = false, HelpText = "Set cascade-connection flag")]
	public bool? Cascade { get; set; }

	[Option("do-not-control-integrity", Required = false, HelpText = "Set do-not-control-integrity flag")]
	public bool? DoNotControlIntegrity { get; set; }
}

/// <summary>
/// Adds, updates, or removes a remote entity schema column.
/// </summary>
public class ModifyEntitySchemaColumnCommand : Command<ModifyEntitySchemaColumnOptions>
{
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public ModifyEntitySchemaColumnCommand(IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_columnManager = columnManager;
		_logger = logger;
	}

	public override int Execute(ModifyEntitySchemaColumnOptions options) {
		try {
			ValidateOptions(options);
			_columnManager.ModifyColumn(options);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	internal static void ValidateOptions(ModifyEntitySchemaColumnOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new ArgumentException("Package is required.", nameof(options.Package));
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new ArgumentException("Schema name is required.", nameof(options.SchemaName));
		}
		if (string.IsNullOrWhiteSpace(options.Action)) {
			throw new ArgumentException("Action is required.", nameof(options.Action));
		}
		if (!Enum.TryParse(options.Action, true, out EntitySchemaColumnAction action)) {
			throw new ArgumentException("Action must be one of: add, modify, remove.", nameof(options.Action));
		}
		if (string.IsNullOrWhiteSpace(options.ColumnName)) {
			throw new ArgumentException("Column name is required.", nameof(options.ColumnName));
		}
		if (action == EntitySchemaColumnAction.Add && string.IsNullOrWhiteSpace(options.Type)) {
			throw new ArgumentException("--type is required for add action.", nameof(options.Type));
		}
		if (action == EntitySchemaColumnAction.Remove && HasMutableOptions(options)) {
			throw new ArgumentException("Remove action does not accept column property options.", nameof(options.Action));
		}
		if (action == EntitySchemaColumnAction.Modify && !HasMutableOptions(options)) {
			throw new ArgumentException(
				"Modify action requires at least one property option to change.",
				nameof(options.Action));
		}
	}

	private static bool HasMutableOptions(ModifyEntitySchemaColumnOptions options) {
		return !string.IsNullOrWhiteSpace(options.NewName)
			|| !string.IsNullOrWhiteSpace(options.Type)
			|| !string.IsNullOrWhiteSpace(options.Title)
			|| options.TitleLocalizations?.Count > 0
			|| !string.IsNullOrWhiteSpace(options.Description)
			|| options.DescriptionLocalizations?.Count > 0
			|| !string.IsNullOrWhiteSpace(options.ReferenceSchemaName)
			|| options.Required.HasValue
			|| options.Indexed.HasValue
			|| options.Cloneable.HasValue
			|| options.TrackChanges.HasValue
			|| !string.IsNullOrWhiteSpace(options.DefaultValueSource)
			|| options.DefaultValue != null
			|| options.DefaultValueConfig != null
			|| options.MultilineText.HasValue
			|| options.LocalizableText.HasValue
			|| options.AccentInsensitive.HasValue
			|| options.Masked.HasValue
			|| options.FormatValidated.HasValue
			|| options.UseSeconds.HasValue
			|| options.SimpleLookup.HasValue
			|| options.Cascade.HasValue
			|| options.DoNotControlIntegrity.HasValue;
	}
}
