using System;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for creating a new lookup entity schema in Creatio.
/// </summary>
[Verb("create-lookup", HelpText = "Create a lookup entity schema in a remote Creatio package")]
public sealed class CreateLookupOptions : RemoteCommandOptions
{
	[Option("package", Required = true, HelpText = "Target package name")]
	public string Package { get; set; } = string.Empty;

	[Option("name", Required = true, HelpText = "Schema name (max 22 characters)")]
	public string SchemaName { get; set; } = string.Empty;

	[Option("title", Required = true, HelpText = "Schema title")]
	public string Title { get; set; } = string.Empty;

	[Option("column", Required = false, HelpText = "Column spec <name>:<type>[:<title>] or JSON. Repeat for multiple columns. BaseLookup already provides Name and Description.")]
	public System.Collections.Generic.IEnumerable<string> Columns { get; set; } = [];
}

/// <summary>
/// Creates a lookup entity schema in a remote Creatio package and registers it in the lookup catalog.
/// </summary>
public sealed class CreateLookupCommand(
	CreateEntitySchemaCommand createEntitySchemaCommand,
	ILookupRegistrationService lookupRegistrationService,
	ILogger logger)
	: Command<CreateLookupOptions>
{
	private const string BaseLookupParentSchemaName = "BaseLookup";

	/// <inheritdoc />
	public override int Execute(CreateLookupOptions options)
	{
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}
			if (string.IsNullOrWhiteSpace(options.Package)) {
				throw new InvalidOperationException("Package is required.");
			}
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				throw new InvalidOperationException("Schema name is required.");
			}
			if (options.SchemaName.Trim().Length > 22) {
				throw new InvalidOperationException("Schema name must not exceed 22 characters.");
			}
			if (string.IsNullOrWhiteSpace(options.Title)) {
				throw new InvalidOperationException("Schema title is required.");
			}

			CreateEntitySchemaOptions entitySchemaOptions = new() {
				Package = options.Package,
				SchemaName = options.SchemaName,
				Title = options.Title,
				ParentSchemaName = BaseLookupParentSchemaName,
				ExtendParent = false,
				Columns = options.Columns,
				Environment = options.Environment
			};

			int exitCode = createEntitySchemaCommand.Execute(entitySchemaOptions);
			if (exitCode != 0) {
				return exitCode;
			}

			lookupRegistrationService.EnsureLookupRegistration(options.Package, options.SchemaName, options.Title);
			logger.WriteInfo($"Lookup '{options.SchemaName}' registered in the lookup catalog.");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}
