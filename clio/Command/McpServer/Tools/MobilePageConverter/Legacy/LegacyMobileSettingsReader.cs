namespace Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json.Linq;

/// <summary>
/// Reads the EFFECTIVE classic Mobile-wizard settings of a legacy <c>Mobile&lt;Entity&gt;GridPageSettings&lt;Workplace&gt;</c>
/// schema from a Creatio environment (ENG-95730). The schema may live in several packages as replacing schemas;
/// every package layer contributes its own diff array, and only their ordered application (ROOT → HEAD, in
/// package-hierarchy order) is the effective settings. Raw bodies never leave the reader: the result carries the
/// merged <c>settings</c> node and per-layer facts (package, operation count) only.
/// </summary>
public interface ILegacyMobileSettingsReader {
	/// <summary>
	/// Reads and merges every package layer of the named legacy settings schema.
	/// </summary>
	/// <param name="schemaName">The legacy settings schema name.</param>
	/// <returns>The merged settings or a failure with an actionable error; never throws for a server-side problem.</returns>
	LegacyMobileSettingsReadResult Read(string schemaName);
}

/// <summary>What shape the stored body had.</summary>
public enum LegacyBodyShape {
	/// <summary>A JSON operation array (the classic wizard format).</summary>
	OperationArray,

	/// <summary>A JSON object — a Freedom UI mobile body stored under a legacy schema name (ENG-95733 case).</summary>
	FreedomUiJsonObject,

	/// <summary>No layer carried a body.</summary>
	Empty,

	/// <summary>A body that parsed as neither.</summary>
	Unparseable
}

/// <summary>One package layer of the legacy settings hierarchy. Carries no body.</summary>
public sealed record LegacyMobileSettingsLayer(
	string SchemaUId,
	string SchemaName,
	string PackageUId,
	string PackageName,
	int SchemaVersion,
	int OperationCount);

/// <summary>Result of <see cref="ILegacyMobileSettingsReader.Read"/>.</summary>
public sealed record LegacyMobileSettingsReadResult(
	bool Success,
	string Error,
	string SchemaName,
	string SchemaUId,
	int? HeadSchemaType,
	IReadOnlyList<LegacyMobileSettingsLayer> Layers,
	JObject EffectiveSettings,
	LegacyBodyShape BodyShape,
	IReadOnlyList<string> Notes) {

	/// <summary>Builds a failure result.</summary>
	public static LegacyMobileSettingsReadResult Fail(string schemaName, string error,
		LegacyBodyShape shape = LegacyBodyShape.Unparseable, IReadOnlyList<LegacyMobileSettingsLayer> layers = null) =>
		new(false, error, schemaName, null, null, layers ?? [], null, shape, []);
}

/// <summary>
/// Default <see cref="ILegacyMobileSettingsReader"/>: SysSchema row → designer hierarchy (re-queried from the ROOT
/// schema so every replacing layer appears) → per-layer unescape + parse → <see cref="IJsonDiffApplier.ApplyDiff"/>
/// ROOT → HEAD → the merged <c>settings</c> node. Cross-checks the hierarchy against every SysSchema row of that
/// name so a package the designer service did not return is reported instead of silently missing.
/// </summary>
internal sealed class LegacyMobileSettingsReader : ILegacyMobileSettingsReader {

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IPageDesignerHierarchyClient _hierarchyClient;
	private readonly Func<IJsonDiffApplier> _applierFactory;

	public LegacyMobileSettingsReader(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IPageDesignerHierarchyClient hierarchyClient,
		Func<IJsonDiffApplier> applierFactory) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_hierarchyClient = hierarchyClient;
		_applierFactory = applierFactory;
	}

	/// <inheritdoc />
	public LegacyMobileSettingsReadResult Read(string schemaName) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			return LegacyMobileSettingsReadResult.Fail(schemaName, "schemaName is required");
		}
		(JToken row, string error) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			_applicationClient, _serviceUrlBuilder, schemaName,
			("UId", "UId"), ("PackageUId", "SysPackage.UId"), ("PackageName", "SysPackage.Name"));
		if (row is null) {
			return LegacyMobileSettingsReadResult.Fail(schemaName, error ?? $"Schema '{schemaName}' not found");
		}
		string schemaUId = row["UId"]?.ToString();
		string packageUId = row["PackageUId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
			return LegacyMobileSettingsReadResult.Fail(schemaName,
				$"Schema '{schemaName}' metadata is missing package or schema identifiers");
		}

		string designPackageUId = ResolveDesignPackageUId(schemaUId, packageUId);
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy;
		try {
			hierarchy = _hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		} catch (Exception ex) {
			return LegacyMobileSettingsReadResult.Fail(schemaName,
				PageHierarchyRecoveryHint.Append($"Failed to load hierarchy for '{schemaName}': {ex.Message}"));
		}
		if (hierarchy.Count == 0) {
			return LegacyMobileSettingsReadResult.Fail(schemaName, $"Schema '{schemaName}' hierarchy is empty");
		}
		// Re-query from the ROOT layer: when the requested row resolves to a lower package, the upper replacing
		// layers only appear when the hierarchy is read from the root (same rule as PageGetCommand.ResolveHierarchy).
		string rootSchemaUId = FindRootSchemaUId(hierarchy, schemaName) ?? schemaUId;
		if (!string.Equals(rootSchemaUId, schemaUId, StringComparison.OrdinalIgnoreCase)) {
			try {
				IReadOnlyList<PageDesignerHierarchySchema> full = _hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
				if (full.Count > 0) {
					hierarchy = full;
				}
			} catch (Exception) {
				// Best-effort: keep the hierarchy already read; the package cross-check below reports any gap.
			}
		}

		// Only the layers that carry THIS schema name are settings layers (the designer service may also return
		// unrelated parents for a schema that extends another one).
		List<PageDesignerHierarchySchema> ordered = hierarchy
			.Where(s => string.Equals(s.Name, schemaName, StringComparison.OrdinalIgnoreCase))
			.Reverse()
			.ToList();
		if (ordered.Count == 0) {
			ordered = hierarchy.Reverse().ToList();
		}
		var notes = new List<string>();
		notes.AddRange(CrossCheckPackages(schemaName, ordered));

		var layerOps = new List<(JArray Operations, int SchemaVersion)>();
		var layers = new List<LegacyMobileSettingsLayer>();
		foreach (PageDesignerHierarchySchema layer in ordered) {
			JArray operations = new();
			if (!string.IsNullOrWhiteSpace(layer.Body)) {
				string text = Unescape(layer.Body);
				if (text.StartsWith('{')) {
					return LegacyMobileSettingsReadResult.Fail(schemaName,
						$"Schema '{schemaName}' (package '{layer.PackageName}') stores a Freedom UI JSON body under a legacy settings schema name; that is the {LegacyMobileSettingsClassifier.OverridesTicket} override case and is not converted here.",
						LegacyBodyShape.FreedomUiJsonObject, layers);
				}
				try {
					operations = JArray.Parse(text);
				} catch (Exception ex) {
					return LegacyMobileSettingsReadResult.Fail(schemaName,
						$"Schema '{schemaName}' (package '{layer.PackageName}') body is not a JSON operation array: {ex.Message}",
						LegacyBodyShape.Unparseable, layers);
				}
			}
			layerOps.Add((operations, layer.SchemaVersion));
			layers.Add(new LegacyMobileSettingsLayer(layer.UId, layer.Name, layer.PackageUId, layer.PackageName, layer.SchemaVersion, operations.Count));
		}
		if (layers.TrueForAll(l => l.OperationCount == 0)) {
			return LegacyMobileSettingsReadResult.Fail(schemaName,
				$"Schema '{schemaName}' carries no settings operations in any package layer.", LegacyBodyShape.Empty, layers);
		}

		JArray merged;
		try {
			merged = Merge(layerOps, _applierFactory);
		} catch (Exception ex) when (ex is JsonDiffApplierException or InvalidCastException or FormatException or ArgumentException) {
			// A malformed layer (a non-numeric index, an object where a name should be) surfaces from the applier as
			// a cast/format error, not only as JsonDiffApplierException; all of them are "this package's body is
			// broken", so keep the layer diagnostics instead of letting a bare exception escape.
			return LegacyMobileSettingsReadResult.Fail(schemaName,
				$"Applying the package layers of '{schemaName}' failed ({string.Join(" -> ", layers.Select(l => l.PackageName ?? l.SchemaUId))}): {ex.Message}",
				LegacyBodyShape.OperationArray, layers);
		}
		JObject settings = merged.OfType<JObject>().FirstOrDefault(item =>
			string.Equals(item.Value<string>("name"), LegacyGridPageSettingsParser.SettingsNodeName, StringComparison.Ordinal));
		if (settings is null) {
			return LegacyMobileSettingsReadResult.Fail(schemaName,
				$"Schema '{schemaName}' does not contain a '{LegacyGridPageSettingsParser.SettingsNodeName}' root node after merging its package layers.",
				LegacyBodyShape.OperationArray, layers);
		}
		return new LegacyMobileSettingsReadResult(
			true, null, schemaName, schemaUId, hierarchy[0].SchemaType, layers, settings,
			LegacyBodyShape.OperationArray, notes);
	}

	/// <summary>
	/// Applies every layer's operations in the given (ROOT → HEAD) order onto an empty tree with the page diff
	/// applier — the same applier and options page-bundle resolution uses. Pure; exposed for tests.
	/// </summary>
	internal static JArray Merge(IReadOnlyList<(JArray Operations, int SchemaVersion)> layers, Func<IJsonDiffApplier> applierFactory) {
		ArgumentNullException.ThrowIfNull(layers);
		ArgumentNullException.ThrowIfNull(applierFactory);
		JToken result = applierFactory().ApplyDiff(
			new JArray(),
			layers.Select(l => l.Operations).ToList(),
			layers.Select(l => new JsonApplierOperationsOptions { ApplyMoveIfIndirectParentMoved = l.SchemaVersion >= 1 }).ToList());
		return result as JArray ?? new JArray();
	}

	/// <summary>
	/// Normalizes a stored legacy body before JSON parsing: resolves escaped <c>\$</c> sequences (an invalid JSON
	/// escape the wizard may leave in bodies), trims, and drops a trailing statement terminator.
	/// </summary>
	internal static string Unescape(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return string.Empty;
		}
		string text = body.Replace("\\$", "$").Trim();
		if (text.EndsWith(';')) {
			text = text[..^1].TrimEnd();
		}
		return text;
	}

	private string ResolveDesignPackageUId(string schemaUId, string fallbackPackageUId) {
		string designPackageUId;
		try {
			designPackageUId = _hierarchyClient.GetDesignPackageUId(schemaUId);
		} catch (Exception) {
			designPackageUId = null;
		}
		return string.IsNullOrWhiteSpace(designPackageUId) ? fallbackPackageUId : designPackageUId;
	}

	// Mirrors PageGetCommand.FindRootSchemaUId: the LAST hierarchy entry carrying the schema name is the root layer.
	private static string FindRootSchemaUId(IReadOnlyList<PageDesignerHierarchySchema> hierarchy, string schemaName) {
		for (int i = hierarchy.Count - 1; i >= 0; i--) {
			if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
				return hierarchy[i].UId;
			}
		}
		return null;
	}

	/// <summary>
	/// Compares the packages that carry a SysSchema row of this name against the layers the hierarchy service
	/// returned; a package present in SysSchema but absent from the hierarchy is reported (never silent).
	/// </summary>
	private IEnumerable<string> CrossCheckPackages(string schemaName, IReadOnlyList<PageDesignerHierarchySchema> layers) {
		(JArray rows, string error) = PageSchemaMetadataHelper.QuerySysSchemaRowsByName(
			_applicationClient, _serviceUrlBuilder, schemaName, ("PackageName", "SysPackage.Name"), ("PackageUId", "SysPackage.UId"));
		if (rows is null) {
			yield return $"The package cross-check for '{schemaName}' could not run ({error ?? "query failed"}); the resolved hierarchy was not verified against SysSchema.";
			yield break;
		}
		var knownNames = new HashSet<string>(layers.Select(l => l.PackageName).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
		var knownUIds = new HashSet<string>(layers.Select(l => l.PackageUId).Where(u => !string.IsNullOrWhiteSpace(u)), StringComparer.OrdinalIgnoreCase);
		var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JToken row in rows) {
			string package = row["PackageName"]?.ToString();
			string packageUId = row["PackageUId"]?.ToString();
			bool known = (!string.IsNullOrWhiteSpace(package) && knownNames.Contains(package))
				|| (!string.IsNullOrWhiteSpace(packageUId) && knownUIds.Contains(packageUId));
			string label = string.IsNullOrWhiteSpace(package) ? packageUId : package;
			if (!known && !string.IsNullOrWhiteSpace(label) && reported.Add(label)) {
				yield return $"Package '{label}' carries a '{schemaName}' schema that was NOT part of the resolved hierarchy — the effective settings may be incomplete; verify the page in the classic Mobile application wizard.";
			}
		}
	}
}
