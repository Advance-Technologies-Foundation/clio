using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("create-entity-schema", HelpText = "Create an entity schema in a remote Creatio package")]
public class CreateEntitySchemaOptions : RemoteCommandOptions
{
	internal const string ReplacementNameMismatchMessage = "A replacement schema must have the same name as its parent.";
	/// <summary>
	/// Parent schema applied when <c>--parent</c> is omitted (and the schema is not a replacement schema).
	/// A parentless root schema gets a prefixed primary column (e.g. <c>UsrId</c> instead of <c>Id</c>) and is
	/// unreachable over OData in both directions, so a missing parent defaults to this fully usable base (ENG-94424).
	/// </summary>
	public const string DefaultParentSchemaName = "BaseEntity";

	[Option("package", Required = false, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("package-name", Required = false, Hidden = true, HelpText = "Alias for --package")]
	public string? PackageNameAlias {
		get => Package;
		set { if (!string.IsNullOrEmpty(value)) Package = value; }
	}

	/// <summary>Schema name. Required unless the hidden <c>--schema-name</c> alias supplies it.</summary>
	[Option("name", Required = false, HelpText = "Schema name. Required.")]
	public string SchemaName { get; set; }

	/// <summary>
	/// Hidden alias of <c>--name</c>, the spelling the other entity-schema commands use. Kept separate from
	/// <see cref="SchemaName"/> so the command can report a missing name and a conflict between the two.
	/// </summary>
	[Option("schema-name", Required = false, Hidden = true, HelpText = "Alias for --name")]
	public string? SchemaNameAlias { get; set; }

	[Option("title", Required = true, HelpText = "Schema title")]
	public string Title { get; set; }

	public IReadOnlyDictionary<string, string>? TitleLocalizations { get; set; }

	/// <summary>Parent name; defaults to the schema name for replacements and BaseEntity otherwise.</summary>
	[Option("parent", Required = false, HelpText = "Parent schema name. Defaults to the schema name for replacements, or BaseEntity otherwise")]
	public string ParentSchemaName { get; set; }

	/// <summary>Whether to create a same-name replacement in the target package.</summary>
	[Option("extend-parent", Required = false, Default = false, HelpText = "Create a same-name replacement schema in the target package")]
	public bool ExtendParent { get; set; }

	/// <summary>
	/// Gets or sets whether the created entity schema is virtual and therefore has no physical database table.
	/// </summary>
	[Option("is-virtual", Required = false, Default = false,
		HelpText = "Create a virtual entity schema without a physical database table")]
	public bool IsVirtual { get; set; }

	/// <summary>Gets or sets whether the schema maps to a separately provisioned database view.</summary>
	[Option("is-db-view", Required = false,
		HelpText = "Map the entity to a database view; no table or SQL view is generated")]
	public bool? IsDBView { get; set; }

	/// <summary>Gets or sets column specs, each containing a legacy definition, JSON object, or non-empty JSON array.</summary>
	[Option("column", Required = false, HelpText = "Column spec <name>:<type>[:<title>[:<refSchema>]] or a JSON object/array with name/type/title/reference-schema-name/required/default-value-source/default-value. Repeat the option for multiple columns.")]
	public IEnumerable<string> Columns { get; set; }

	[Option("caption-culture", Required = false, HelpText = "Override the culture used for generated captions/labels (e.g. en-US, uk-UA). Precedence: this override > the connected user's profile culture > en-US. Supplying it skips the profile-culture lookup.")]
	public string? CaptionCulture { get; set; }
}

public class CreateEntitySchemaCommand : Command<CreateEntitySchemaOptions>
{
	private readonly IRemoteEntitySchemaCreator _remoteEntitySchemaCreator;
	private readonly ILogger _logger;

	public CreateEntitySchemaCommand(IRemoteEntitySchemaCreator remoteEntitySchemaCreator, ILogger logger)
	{
		_remoteEntitySchemaCreator = remoteEntitySchemaCreator;
		_logger = logger;
	}

	public override int Execute(CreateEntitySchemaOptions options)
	{
		try {
			Validate(options);
			NormalizeParentSchema(options);
			_remoteEntitySchemaCreator.Create(options);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(CreateEntitySchemaOptions options)
	{
		if (options == null) {
			throw new InvalidOperationException("Command options are required.");
		}
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new InvalidOperationException("Package is required.");
		}
		ResolveSchemaName(options);
		if (string.IsNullOrWhiteSpace(options.Title)) {
			throw new InvalidOperationException("Schema title is required.");
		}
		if (options.ExtendParent && !string.IsNullOrWhiteSpace(options.ParentSchemaName)
			&& !string.Equals(options.SchemaName, options.ParentSchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(CreateEntitySchemaOptions.ReplacementNameMismatchMessage);
		}
	}

	// --name is not parser-required because --schema-name may supply it instead; the presence and agreement of the
	// two spellings are enforced here, before any remote call.
	private static void ResolveSchemaName(CreateEntitySchemaOptions options)
	{
		bool hasName = !string.IsNullOrWhiteSpace(options.SchemaName);
		bool hasAlias = !string.IsNullOrWhiteSpace(options.SchemaNameAlias);
		if (!hasName && !hasAlias) {
			throw new InvalidOperationException("Schema name is required. Supply --name (or its alias --schema-name).");
		}
		if (hasName && hasAlias
			&& !string.Equals(options.SchemaName, options.SchemaNameAlias, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"Schema name is set by both --name ('{UpdateEntitySchemaCommand.SanitizeForMessage(options.SchemaName)}') "
				+ $"and its alias --schema-name ('{UpdateEntitySchemaCommand.SanitizeForMessage(options.SchemaNameAlias)}'). Supply only one.");
		}
		if (!hasName) {
			options.SchemaName = options.SchemaNameAlias;
		}
	}

	// Single source of truth for parent defaulting across every execution path (CLI and MCP). Defaults a root
	// schema's parent to DefaultParentSchemaName when --parent was omitted; without a parent the created schema
	// gets a prefixed primary column (e.g. UsrId) and cannot be used over OData (ENG-94424). An explicit parent
	// is preserved; a replacement defaults to the same-name base schema.
	private static void NormalizeParentSchema(CreateEntitySchemaOptions options)
	{
		if (string.IsNullOrWhiteSpace(options.ParentSchemaName)) {
			options.ParentSchemaName = options.ExtendParent
				? options.SchemaName : CreateEntitySchemaOptions.DefaultParentSchemaName;
		}
	}
}
