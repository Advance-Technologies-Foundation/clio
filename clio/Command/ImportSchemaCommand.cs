using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.SchemaTransfer;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options of <c>import-schema</c>.
/// </summary>
[Verb("import-schema", Aliases = ["schema-import"],
	HelpText = "Import a schema bundle produced by export-schema into a package of a Creatio environment")]
[RequiresPackage("cliogate", "2.0.0.46",
	Hint = "Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) to install/update cliogate.")]
public class ImportSchemaOptions : EnvironmentOptions {

	/// <summary>
	/// Gets or sets the bundle to import: either a folder produced by <c>export-schema</c> or the
	/// <c>schema-data.json</c> inside one.
	/// </summary>
	[Value(0, MetaName = "Path", Required = true,
		HelpText = "Bundle folder produced by export-schema, or its schema-data.json")]
	public string Path { get; set; }

	/// <summary>Gets or sets the package that will own the imported schema.</summary>
	// No short 'p': EnvironmentOptions already binds -p to --password, and a duplicate short name makes the
	// parser throw "Sequence contains more than one matching element" for the whole verb.
	[Option("package-name", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to report the planned action without writing anything.
	/// </summary>
	[Option("dry-run", Required = false,
		HelpText = "Report what the import would do (create, replace, or new layer) and write nothing")]
	public bool DryRun { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to proceed when the schema name is already owned by a different
	/// package.
	/// </summary>
	[Option("allow-new-layer", Required = false,
		HelpText = "Proceed when the schema name already exists in a different package, creating a new layer")]
	public bool AllowNewLayer { get; set; }
}

/// <summary>
/// What an import would do to the target environment.
/// </summary>
public enum SchemaImportAction {

	/// <summary>The schema does not exist on the target; the import creates it.</summary>
	Create,

	/// <summary>The schema already exists in the target package; the import replaces that layer.</summary>
	Replace,

	/// <summary>The schema exists in other packages only; the import adds a layer in the target package.</summary>
	NewLayer
}

/// <summary>
/// Writes a schema bundle into a package of a Creatio environment, creating or replacing exactly one schema.
/// </summary>
public class ImportSchemaCommand : Command<ImportSchemaOptions> {

	private readonly ISchemaTransferClient _schemaTransferClient;
	private readonly ISchemaBundleStore _schemaBundleStore;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="ImportSchemaCommand"/> class.
	/// </summary>
	public ImportSchemaCommand(ISchemaTransferClient schemaTransferClient, ISchemaBundleStore schemaBundleStore,
		ILogger logger) {
		_schemaTransferClient = schemaTransferClient;
		_schemaBundleStore = schemaBundleStore;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(ImportSchemaOptions options) {
		try {
			string targetPackage = options.PackageName?.Trim();
			if (string.IsNullOrWhiteSpace(targetPackage)) {
				throw new InvalidOperationException("Target package name cannot be empty.");
			}
			SchemaBundle bundle = _schemaBundleStore.Read(options.Path?.Trim());
			string schemaName = bundle.Descriptor?.SchemaName;
			if (string.IsNullOrWhiteSpace(schemaName)) {
				throw new InvalidOperationException(
					"The bundle does not name a schema; it is not a bundle produced by 'clio export-schema'.");
			}
			IReadOnlyList<SchemaLayerDto> existingLayers =
				_schemaTransferClient.FindLayers(schemaName, bundle.Descriptor.ManagerName);
			SchemaImportAction action = ResolveAction(bundle.Descriptor, targetPackage, existingLayers,
				options.AllowNewLayer);
			_logger.WriteInfo(Describe(action, schemaName, targetPackage, existingLayers));
			if (options.DryRun) {
				_logger.WriteInfo("Dry run: nothing was written.");
				return 0;
			}
			string importResult = _schemaTransferClient.Import(bundle.SchemaData, targetPackage);
			_logger.WriteInfo(
				$"Imported schema '{schemaName}' (uId={bundle.Descriptor.SchemaUId}) into package '{targetPackage}'.");
			if (!string.IsNullOrWhiteSpace(importResult)) {
				_logger.WriteInfo($"Platform importer: {importResult}");
			}
			_logger.WriteWarning(
				"The schema is saved but not built. Run 'clio compile-configuration' when it carries source code, "
				+ "and 'clio update-db-structure' when it changes the database structure.");
			return 0;
		}
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Decides what the import would do, and refuses the case that silently creates an unintended layer.
	/// </summary>
	/// <remarks>
	/// Creating a same-named schema in a second package is sometimes exactly what the operator wants and
	/// sometimes the <c>IU_Name_Manager_Package</c> duplicate-key defect this feature was written for, and the
	/// two are indistinguishable from here — so it is refused by default and named in the message.
	/// </remarks>
	private static SchemaImportAction ResolveAction(SchemaBundleDescriptor bundleIdentity, string targetPackage,
		IReadOnlyList<SchemaLayerDto> existingLayers, bool allowNewLayer) {
		string schemaName = bundleIdentity.SchemaName;
		if (existingLayers.Count == 0) {
			return SchemaImportAction.Create;
		}
		IReadOnlyList<SchemaLayerDto> targetPackageLayers = existingLayers
			.Where(layer => string.Equals(layer.PackageName, targetPackage, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (targetPackageLayers.Count > 0) {
			EnsureOneLayerIsTheSameSchema(bundleIdentity, targetPackage, targetPackageLayers);
			return SchemaImportAction.Replace;
		}
		if (!allowNewLayer) {
			throw new InvalidOperationException(
				$"Schema '{schemaName}' already exists in package(s) {DescribePackages(existingLayers)}, "
				+ $"not in '{targetPackage}'. Importing it here would create an additional layer. "
				+ $"Re-run with --package-name of the owning package to replace it, "
				+ $"or with --allow-new-layer to create the layer deliberately.");
		}
		return SchemaImportAction.NewLayer;
	}

	/// <summary>
	/// Refuses a REPLACE unless one of the target package's own layers is the schema in the bundle.
	/// </summary>
	/// <remarks>
	/// Matching the package alone is not enough to call an import a replacement. A boxed layer, or one restored
	/// from another environment, can own the same name in the target package under a different <c>UId</c> — and
	/// the platform importer preserves the bundle's <c>UId</c>, so writing it there produces a second row with
	/// the same (name, manager, package) triple and the <c>IU_Name_Manager_Package</c> duplicate key rejects it.
	/// Reporting "Plan: REPLACE" and a successful <c>--dry-run</c> for that case is exactly what makes the plan
	/// untrustworthy, so the mismatch is named here instead.
	/// <para>
	/// Every layer the package owns is considered, not just the first: the uniqueness constraint is
	/// (name, manager, package), so one package can legitimately own this name twice under two managers — and
	/// when the bundle carries no <c>ManagerName</c> the gate's lookup does not narrow by manager, so both come
	/// back. Testing only the first match would refuse (or replace) against an arbitrary one of them.
	/// </para>
	/// </remarks>
	private static void EnsureOneLayerIsTheSameSchema(SchemaBundleDescriptor bundleIdentity, string targetPackage,
		IReadOnlyList<SchemaLayerDto> targetPackageLayers) {
		if (targetPackageLayers.Any(layer => !UIdsDisagree(bundleIdentity.SchemaUId, layer.SchemaUId)
			&& !ManagersDisagree(bundleIdentity.ManagerName, layer.ManagerName))) {
			return;
		}
		string owned = string.Join(", ", targetPackageLayers
			.Select(layer => $"uId={OrUnknown(layer.SchemaUId)} (manager {OrUnknown(layer.ManagerName)})"));
		throw new InvalidOperationException(
			$"Package '{targetPackage}' already owns a schema named '{bundleIdentity.SchemaName}', but it is not "
			+ $"the one in this bundle: the bundle carries uId={OrUnknown(bundleIdentity.SchemaUId)} "
			+ $"(manager {OrUnknown(bundleIdentity.ManagerName)}) while '{targetPackage}' owns {owned}. "
			+ "Importing would not replace that layer — the platform preserves the bundle's uId, so it would add "
			+ "a second row with the same name in the same package, which the IU_Name_Manager_Package index "
			+ "rejects. Import into a package that does not own this name, or delete the conflicting schema "
			+ "first.");
	}

	private static bool UIdsDisagree(string bundleUId, string layerUId) {
		if (string.IsNullOrWhiteSpace(bundleUId) || string.IsNullOrWhiteSpace(layerUId)) {
			return false;
		}
		return Guid.TryParse(bundleUId, out Guid parsedBundleUId) && Guid.TryParse(layerUId, out Guid parsedLayerUId)
			? parsedBundleUId != parsedLayerUId
			: !string.Equals(bundleUId.Trim(), layerUId.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static bool ManagersDisagree(string bundleManagerName, string layerManagerName) =>
		!string.IsNullOrWhiteSpace(bundleManagerName) && !string.IsNullOrWhiteSpace(layerManagerName)
		&& !string.Equals(bundleManagerName.Trim(), layerManagerName.Trim(), StringComparison.OrdinalIgnoreCase);

	private static string OrUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "<unknown>" : value;

	private static string Describe(SchemaImportAction action, string schemaName, string targetPackage,
		IReadOnlyList<SchemaLayerDto> existingLayers) =>
		action switch {
			SchemaImportAction.Create =>
				$"Plan: CREATE schema '{schemaName}' in package '{targetPackage}' (it does not exist yet).",
			SchemaImportAction.Replace =>
				$"Plan: REPLACE schema '{schemaName}' in package '{targetPackage}'.",
			_ =>
				$"Plan: add a NEW LAYER of schema '{schemaName}' in package '{targetPackage}'; "
				+ $"it already exists in {DescribePackages(existingLayers)}."
		};

	private static string DescribePackages(IReadOnlyList<SchemaLayerDto> layers) =>
		string.Join(", ", layers
			.Select(layer => $"'{layer.PackageName}'")
			.Distinct(StringComparer.OrdinalIgnoreCase));
}
