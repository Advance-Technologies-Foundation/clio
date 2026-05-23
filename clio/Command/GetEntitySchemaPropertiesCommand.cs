using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for reading remote entity schema properties.
/// </summary>
[Verb("get-entity-schema-properties", HelpText = "Get properties from a remote Creatio entity schema")]
public class GetEntitySchemaPropertiesOptions : RemoteCommandOptions
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

	[Option("name", Required = false, Hidden = true, HelpText = "Alias for --schema-name")]
	public string? SchemaNameAlias {
		get => SchemaName;
		set { if (!string.IsNullOrEmpty(value)) SchemaName = value; }
	}
}

/// <summary>
/// Prints summary properties and grouped column listings for a remote entity schema.
/// </summary>
public class GetEntitySchemaPropertiesCommand : Command<GetEntitySchemaPropertiesOptions>
{
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public GetEntitySchemaPropertiesCommand(IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_columnManager = columnManager;
		_logger = logger;
	}

	public override int Execute(GetEntitySchemaPropertiesOptions options) {
		try {
			EntitySchemaPropertiesInfo properties = GetSchemaProperties(options);
			WriteSchemaProperties(properties);
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	internal EntitySchemaPropertiesInfo GetSchemaProperties(GetEntitySchemaPropertiesOptions options) {
		Validate(options);
		return _columnManager.GetSchemaProperties(options);
	}

	private static void Validate(GetEntitySchemaPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new ArgumentException("Package is required.", nameof(options.Package));
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new ArgumentException("Schema name is required.", nameof(options.SchemaName));
		}
	}

	private void WriteSchemaProperties(EntitySchemaPropertiesInfo properties) {
		_logger.WriteInfo("Entity schema properties");
		_logger.WriteInfo($"Name: {properties.Name}");
		_logger.WriteInfo($"Title: {FormatText(properties.Title)}");
		_logger.WriteInfo($"Description: {FormatText(properties.Description)}");
		_logger.WriteInfo($"Package: {properties.PackageName}");
		_logger.WriteInfo($"Parent schema: {FormatText(properties.ParentSchemaName)}");
		_logger.WriteInfo($"Extend parent: {FormatBoolean(properties.ExtendParent)}");
		_logger.WriteInfo($"Primary column: {FormatText(properties.PrimaryColumnName)}");
		_logger.WriteInfo($"Primary display column: {FormatText(properties.PrimaryDisplayColumnName)}");
		_logger.WriteInfo($"Own columns: {properties.OwnColumnCount}");
		_logger.WriteInfo($"Inherited columns: {properties.InheritedColumnCount}");
		_logger.WriteInfo($"Indexes: {properties.IndexesCount}");
		_logger.WriteInfo($"Track changes in DB: {FormatBoolean(properties.TrackChangesInDb)}");
		_logger.WriteInfo($"DB view: {FormatBoolean(properties.DbView)}");
		_logger.WriteInfo($"SSP available: {FormatBoolean(properties.SspAvailable)}");
		_logger.WriteInfo($"Virtual: {FormatBoolean(properties.Virtual)}");
		_logger.WriteInfo($"Use record deactivation: {FormatBoolean(properties.UseRecordDeactivation)}");
		_logger.WriteInfo($"Show in advanced mode: {FormatBoolean(properties.ShowInAdvancedMode)}");
		_logger.WriteInfo($"Administrated by operations: {FormatBoolean(properties.AdministratedByOperations)}");
		_logger.WriteInfo($"Administrated by columns: {FormatBoolean(properties.AdministratedByColumns)}");
		_logger.WriteInfo($"Administrated by records: {FormatBoolean(properties.AdministratedByRecords)}");
		_logger.WriteInfo($"Use deny record rights: {FormatBoolean(properties.UseDenyRecordRights)}");
		_logger.WriteInfo($"Use live editing: {FormatBoolean(properties.UseLiveEditing)}");
		WriteColumnGroup("Own columns", properties.Columns?.Where(column => column.Source == "own") ?? []);
		WriteColumnGroup("Inherited columns", properties.Columns?.Where(column => column.Source == "inherited") ?? []);
	}

	private void WriteColumnGroup(string title, IEnumerable<EntitySchemaPropertyColumnInfo> columns) {
		List<EntitySchemaPropertyColumnInfo> columnList = columns.ToList();
		_logger.WriteInfo(title);
		if (columnList.Count == 0) {
			_logger.WriteInfo("<none>");
			return;
		}
		foreach (EntitySchemaPropertyColumnInfo column in columnList) {
			_logger.WriteInfo(FormatColumn(column));
		}
	}

	private static string FormatColumn(EntitySchemaPropertyColumnInfo column) {
		return
			$"- {column.Name} | title: {FormatText(column.Title)} | type: {column.Type} | required: {FormatBoolean(column.Required)} | indexed: {FormatBoolean(column.Indexed)} | reference schema: {FormatText(column.ReferenceSchemaName)} | description: {FormatText(column.Description)}";
	}

	private static string FormatBoolean(bool value) {
		return value ? "true" : "false";
	}

	private static string FormatText(string? value) {
		return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
	}
}
