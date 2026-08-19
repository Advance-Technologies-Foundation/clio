using System;
using System.Reflection;
using Clio.Command.SchemaTransfer;
using Clio.Common;
using CommandLine;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>
/// Options of <c>export-schema</c>.
/// </summary>
[Verb("export-schema", Aliases = ["schema-export"],
	HelpText = "Export a single schema from a Creatio environment into a reviewable bundle folder")]
[RequiresPackage("cliogate", "2.0.0.46",
	Hint = "Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) to install/update cliogate.")]
public class ExportSchemaOptions : EnvironmentOptions {

	/// <summary>Gets or sets the name of the schema to export.</summary>
	[Value(0, MetaName = "SchemaName", Required = true, HelpText = "Schema name")]
	public string SchemaName { get; set; }

	/// <summary>
	/// Gets or sets the package that owns the layer to export. Required when the name exists in more than one
	/// package.
	/// </summary>
	// No short 'p': EnvironmentOptions already binds -p to --password, and a duplicate short name makes the
	// parser throw "Sequence contains more than one matching element" for the whole verb.
	[Option("package-name", Required = false,
		HelpText = "Package that owns the schema layer to export. Required when the name is ambiguous.")]
	public string PackageName { get; set; }

	/// <summary>Gets or sets the schema manager, to narrow an ambiguous name further.</summary>
	[Option("manager-name", Required = false,
		HelpText = "Schema manager to narrow the lookup to, for example AddonSchemaManager")]
	public string ManagerName { get; set; }

	/// <summary>Gets or sets the directory that will receive the bundle folder. Defaults to the current directory.</summary>
	[Option('d', "destination", Required = false,
		HelpText = "Directory that will receive the bundle folder. Default: the current directory.")]
	public string Destination { get; set; }
}

/// <summary>
/// Exports one schema into a bundle folder that can be reviewed and then applied with <c>import-schema</c>.
/// </summary>
/// <remarks>
/// Read-only against the environment. The whole point of the command is that a one-schema fix can be handed
/// over as a one-schema artifact instead of a whole package.
/// </remarks>
public class ExportSchemaCommand : Command<ExportSchemaOptions> {

	private readonly ISchemaTransferClient _schemaTransferClient;
	private readonly ISchemaBundleStore _schemaBundleStore;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IoFileSystem _ioFileSystem;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="ExportSchemaCommand"/> class.
	/// </summary>
	public ExportSchemaCommand(ISchemaTransferClient schemaTransferClient, ISchemaBundleStore schemaBundleStore,
		IWorkingDirectoriesProvider workingDirectoriesProvider, IoFileSystem ioFileSystem,
		EnvironmentSettings environmentSettings, ILogger logger) {
		_schemaTransferClient = schemaTransferClient;
		_schemaBundleStore = schemaBundleStore;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_ioFileSystem = ioFileSystem;
		_environmentSettings = environmentSettings;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(ExportSchemaOptions options) {
		try {
			string schemaName = options.SchemaName?.Trim();
			if (string.IsNullOrWhiteSpace(schemaName)) {
				throw new InvalidOperationException("Schema name cannot be empty.");
			}
			// Confine the bundle path BEFORE any network call: export-schema is MCP-callable, so the destination
			// can come from an agent rather than a shell. Resolve symlinks, drop an untrusted anchor, keep the
			// write inside the workspace or the OS temp dir, and refuse a target that already exists — the same
			// guard get-schema applies to its --output-file.
			string destination = string.IsNullOrWhiteSpace(options.Destination)
				? _workingDirectoriesProvider.CurrentDirectory
				: options.Destination.Trim();
			(string bundleDirectory, string pathError) = OutputPathConfinement.Resolve(
				_ioFileSystem, System.IO.Path.Combine(destination, schemaName));
			if (pathError != null) {
				throw new InvalidOperationException(pathError);
			}
			_logger.WriteInfo($"Exporting schema '{schemaName}'...");
			(SchemaLayerDto schema, string schemaData) = _schemaTransferClient.Export(
				schemaName, options.PackageName, options.ManagerName);
			SchemaBundle bundle = new(BuildDescriptor(schema), schemaData);
			_schemaBundleStore.Write(bundleDirectory, bundle);
			_logger.WriteInfo(
				$"Exported '{schema.SchemaName}' (uId={schema.SchemaUId}, manager={schema.ManagerName}) "
				+ $"from package '{schema.PackageName}' to '{bundleDirectory}'.");
			return 0;
		}
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	private SchemaBundleDescriptor BuildDescriptor(SchemaLayerDto schema) =>
		new() {
			SchemaName = schema?.SchemaName,
			SchemaUId = schema?.SchemaUId,
			Caption = schema?.Caption,
			ManagerName = schema?.ManagerName,
			SourcePackageName = schema?.PackageName,
			SourceEnvironmentUrl = _environmentSettings?.Uri,
			ExportedOnUtc = DateTime.UtcNow,
			ClioVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString()
		};
}
