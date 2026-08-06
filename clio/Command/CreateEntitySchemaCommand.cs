using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("create-entity-schema", HelpText = "Create an entity schema in a remote Creatio package")]
public class CreateEntitySchemaOptions : RemoteCommandOptions
{
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

	[Option("name", Required = true, HelpText = "Schema name")]
	public string SchemaName { get; set; }

	[Option("title", Required = true, HelpText = "Schema title")]
	public string Title { get; set; }

	public IReadOnlyDictionary<string, string>? TitleLocalizations { get; set; }

	[Option("parent", Required = false, HelpText = "Parent schema name")]
	public string ParentSchemaName { get; set; }

	[Option("extend-parent", Required = false, Default = false, HelpText = "Create replacement schema")]
	public bool ExtendParent { get; set; }

	/// <summary>
	/// Gets or sets whether the created entity schema is virtual and therefore has no physical database table.
	/// </summary>
	[Option("is-virtual", Required = false, Default = false,
		HelpText = "Create a virtual entity schema without a physical database table")]
	public bool IsVirtual { get; set; }

	[Option("column", Required = false, HelpText = "Column spec <name>:<type>[:<title>[:<refSchema>]] or JSON with name/type/title/reference-schema-name/required/default-value-source/default-value. Repeat the option for multiple columns.")]
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
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.Title)) {
			throw new InvalidOperationException("Schema title is required.");
		}
		if (options.ExtendParent && string.IsNullOrWhiteSpace(options.ParentSchemaName)) {
			throw new InvalidOperationException("--extend-parent requires --parent.");
		}
	}

	/// <summary>
	/// Defaults a root schema's parent to <see cref="CreateEntitySchemaOptions.DefaultParentSchemaName"/> when
	/// <c>--parent</c> was omitted, mirroring the create-entity-schema MCP tool. Without a parent the created
	/// schema gets a prefixed primary column (e.g. <c>UsrId</c>) and cannot be used over OData (ENG-94424).
	/// An explicit parent and replacement schemas (<c>--extend-parent</c>) are left untouched.
	/// </summary>
	private static void NormalizeParentSchema(CreateEntitySchemaOptions options)
	{
		if (!options.ExtendParent && string.IsNullOrWhiteSpace(options.ParentSchemaName)) {
			options.ParentSchemaName = CreateEntitySchemaOptions.DefaultParentSchemaName;
		}
	}
}
