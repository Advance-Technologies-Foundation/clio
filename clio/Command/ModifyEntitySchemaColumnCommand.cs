using System;
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
	[Option("package", Required = true, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("schema-name", Required = true, HelpText = "Entity schema name")]
	public string SchemaName { get; set; }

	[Option("action", Required = true, HelpText = "Column action: add, modify, or remove")]
	public string Action { get; set; }

	[Option("column-name", Required = true, HelpText = "Target column name")]
	public string ColumnName { get; set; }

	[Option("new-name", Required = false, HelpText = "New column name for rename operations")]
	public string NewName { get; set; }

	[Option("type", Required = false, HelpText = "Column type. Supported values: Guid, Text, Integer, Boolean, DateTime, Lookup")]
	public string Type { get; set; }

	[Option("title", Required = false, HelpText = "Column title/caption")]
	public string Title { get; set; }

	[Option("description", Required = false, HelpText = "Column description")]
	public string Description { get; set; }

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
			Validate(options);
			_columnManager.ModifyColumn(options);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(ModifyEntitySchemaColumnOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new InvalidOperationException("Package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.Action)) {
			throw new InvalidOperationException("Action is required.");
		}
		if (!Enum.TryParse(options.Action, true, out EntitySchemaColumnAction action)) {
			throw new InvalidOperationException("Action must be one of: add, modify, remove.");
		}
		if (string.IsNullOrWhiteSpace(options.ColumnName)) {
			throw new InvalidOperationException("Column name is required.");
		}
		if (action == EntitySchemaColumnAction.Add && string.IsNullOrWhiteSpace(options.Type)) {
			throw new InvalidOperationException("--type is required for add action.");
		}
		if (action == EntitySchemaColumnAction.Remove && HasMutableOptions(options)) {
			throw new InvalidOperationException("Remove action does not accept column property options.");
		}
		if (action == EntitySchemaColumnAction.Modify && !HasMutableOptions(options)) {
			throw new InvalidOperationException("Modify action requires at least one property option to change.");
		}
	}

	private static bool HasMutableOptions(ModifyEntitySchemaColumnOptions options) {
		return !string.IsNullOrWhiteSpace(options.NewName)
			|| !string.IsNullOrWhiteSpace(options.Type)
			|| !string.IsNullOrWhiteSpace(options.Title)
			|| !string.IsNullOrWhiteSpace(options.Description)
			|| !string.IsNullOrWhiteSpace(options.ReferenceSchemaName)
			|| options.Required.HasValue
			|| options.Indexed.HasValue
			|| options.Cloneable.HasValue
			|| options.TrackChanges.HasValue
			|| options.DefaultValue != null
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
