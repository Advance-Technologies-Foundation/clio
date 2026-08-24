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

	/// <summary>
	/// Gets or sets the directory that will receive the bundle folder. Defaults to the workspace root the
	/// current directory belongs to, or the current directory itself when there is no workspace above it.
	/// </summary>
	[Option('d', "destination", Required = false,
		HelpText = "Directory that will receive the bundle folder. Default: the workspace root, or the current directory when there is no workspace.")]
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
	private readonly IoFileSystem _ioFileSystem;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="ExportSchemaCommand"/> class.
	/// </summary>
	public ExportSchemaCommand(ISchemaTransferClient schemaTransferClient, ISchemaBundleStore schemaBundleStore,
		IoFileSystem ioFileSystem, EnvironmentSettings environmentSettings, ILogger logger) {
		_schemaTransferClient = schemaTransferClient;
		_schemaBundleStore = schemaBundleStore;
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
			EnsureNameIsUsableAsFolderName(schemaName);
			// Resolve the bundle path BEFORE any network call: export-schema is MCP-callable, so an explicit
			// destination can come from an agent rather than a shell.
			(string bundleDirectory, string pathError) = ResolveBundleDirectory(options, schemaName);
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

	/// <summary>
	/// Resolves the folder the bundle is written into.
	/// </summary>
	/// <param name="options">Parsed command options.</param>
	/// <param name="schemaName">Trimmed schema name; it becomes the bundle folder name.</param>
	/// <returns>The bundle folder with a <c>null</c> error, or <c>(null, error)</c> when the path is refused.</returns>
	/// <remarks>
	/// Same split as <c>GetClassicPageSourcesCommand.ResolveOutputPath</c>, and for the same reason.
	/// An EXPLICIT <c>--destination</c> may be agent-supplied, so it goes through
	/// <see cref="OutputPathConfinement.Resolve"/> — symlinks resolved, untrusted anchor dropped, write confined
	/// to the workspace or the OS temp dir. The DEFAULT is tool-owned and must NOT flow through that guard
	/// (<c>OutputPathConfinement</c> documents this itself): confinement drops an untrusted anchor, and the
	/// user's home directory counts as untrusted, so a plain <c>clio export-schema Foo</c> run from <c>$HOME</c>
	/// would be refused even though the help text promises the current directory. The default is anchored the way
	/// every other default output path in this repo is — workspace root if there is one, never bare <c>$HOME</c>.
	/// The cwd read happens under <see cref="McpServer.Tools.McpToolExecutionLock.CwdLock"/> because the MCP
	/// workspace tools PIN process cwd; an unsynchronized read could default one tenant's bundle into another
	/// tenant's pinned directory. Overwrite protection is not lost with the confinement guard:
	/// <see cref="ISchemaBundleStore.Write"/> refuses an existing bundle folder on both branches.
	/// </remarks>
	private (string path, string error) ResolveBundleDirectory(ExportSchemaOptions options, string schemaName) {
		if (!string.IsNullOrWhiteSpace(options.Destination)) {
			return OutputPathConfinement.Resolve(
				_ioFileSystem, System.IO.Path.Combine(options.Destination.Trim(), schemaName));
		}
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			string anchor = PageOutputDirectoryResolver.ResolveAnchor(
				_ioFileSystem,
				_ioFileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				null);
			return (System.IO.Path.Combine(anchor, schemaName), null);
		}
	}

	/// <summary>
	/// Refuses a schema name that cannot serve as the bundle folder name.
	/// </summary>
	/// <remarks>
	/// The name becomes the bundle folder name on BOTH branches of <see cref="ResolveBundleDirectory"/>, but only
	/// the explicit-destination branch is followed by <see cref="OutputPathConfinement.Resolve"/>. The
	/// tool-owned default deliberately is not (see that method's remarks for why), so a name carrying a path
	/// separator or a <c>..</c> segment would be combined straight into the anchor and escape it — and
	/// <c>export-schema</c> is MCP-callable, so the name can arrive from an agent rather than a shell. The check
	/// lives here, ahead of both branches, rather than patching only the unguarded one: a Creatio schema name is
	/// an identifier and never legitimately contains any of this, so refusing it early also gives the operator a
	/// message about the name instead of one about a path.
	/// </remarks>
	/// <param name="schemaName">Trimmed schema name.</param>
	/// <exception cref="InvalidOperationException">Thrown when the name cannot be a folder name.</exception>
	private static void EnsureNameIsUsableAsFolderName(string schemaName) {
		// Path.GetInvalidFileNameChars() is platform-specific — on Unix it lists only '/' and NUL, so '\\' is
		// named explicitly rather than relied upon, and the '.'/'..' segments are valid file-name characters
		// throughout and have to be rejected as whole names.
		bool isTraversalSegment = schemaName is "." or "..";
		bool hasSeparator = schemaName.IndexOfAny(['/', '\\']) >= 0;
		bool hasInvalidChar = schemaName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0;
		if (!isTraversalSegment && !hasSeparator && !hasInvalidChar) {
			return;
		}
		throw new InvalidOperationException(
			$"'{schemaName}' cannot be used as a schema name: it becomes the bundle folder name, so it must not "
			+ "contain a path separator, be a '.' or '..' segment, or carry a character that is invalid in a "
			+ "file name. Pass the plain schema name, and use --destination to choose where the bundle goes.");
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
