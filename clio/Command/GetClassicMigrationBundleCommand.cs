namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

/// <summary>Options for the <c>get-classic-migration-bundle</c> command.</summary>
[Verb("get-classic-migration-bundle", Aliases = ["classic-migration-bundle"],
	HelpText = "Assemble a Classic->Freedom migration bundle (merged layer chain + parent-template seed + " +
		"resolution inputs) and write the manifest JSON to disk for the migration engine to fold")]
public class GetClassicMigrationBundleOptions : EnvironmentOptions {

	/// <summary>Classic client-unit (page) schema name the bundle is assembled for.</summary>
	[Option("schema-name", Required = true, HelpText = "Classic client-unit (page) schema name to assemble the bundle for")]
	public string SchemaName { get; set; }

	/// <summary>Optional entity schema name; inferred from the page bodies when omitted.</summary>
	[Option("entity", Required = false,
		HelpText = "Entity schema name (optional; inferred from the page body when omitted). Drives entityColumns/columnTitles.")]
	public string Entity { get; set; }

	/// <summary>Optional manifest output path; when omitted the manifest is anchored under the workspace root.</summary>
	[Option("output-file", Required = false,
		HelpText = "Manifest output path. Default: <workspace-root>/.clio-migration/<schema>/manifest.json " +
			"(falls back to the current directory, never the bare home directory)")]
	public string OutputFile { get; set; }
}

/// <summary>
/// Summary envelope returned by <c>get-classic-migration-bundle</c>. Carries the absolute manifest path and
/// per-block counts — never the schema bodies themselves (those live only in the manifest file).
/// </summary>
public sealed class GetClassicMigrationBundleResponse {

	/// <summary>Whether the bundle was assembled and written.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>The classic page schema the bundle was assembled for.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	/// <summary>The resolved entity schema name (explicit option or inferred), when known.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("entity")]
	public string Entity { get; set; }

	/// <summary>Absolute path of the manifest file written to disk.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("manifestPath")]
	public string ManifestPath { get; set; }

	/// <summary>Number of replacing-schema layers in the manifest's <c>schemas</c> chain.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("layerCount")]
	public int LayerCount { get; set; }

	/// <summary>Number of parent-template layer bodies in the manifest's <c>seed</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("seedCount")]
	public int SeedCount { get; set; }

	/// <summary>Number of merged localizable strings gathered into <c>resources</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("resourceCount")]
	public int ResourceCount { get; set; }

	/// <summary>Number of entity columns that contributed a localized title to <c>columnTitles</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("columnCount")]
	public int ColumnCount { get; set; }

	/// <summary>Number of referenced detail schemas resolved into <c>detailSchemas</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("detailCount")]
	public int DetailCount { get; set; }

	/// <summary>Number of section layer bodies gathered into <c>section</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("sectionLayerCount")]
	public int SectionLayerCount { get; set; }

	/// <summary>Number of child edit pages nested into <c>childPageSchemas</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("childPageCount")]
	public int ChildPageCount { get; set; }

	/// <summary>Failure reason when <see cref="Success"/> is <c>false</c>; <c>null</c> otherwise.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Assembles a Classic-&gt;Freedom migration bundle server-side and writes it to disk in the shape the toolkit
/// Node engine (migrate.mjs) folds: the whole replacing-schema layer chain (base-&gt;top) plus the parent-template
/// seed, plus resolution inputs (entityColumns/columnTitles/resources). The layer bodies are written to the
/// manifest file, never returned in the response — the caller triggers the run and reads only the small summary.
/// </summary>
public class GetClassicMigrationBundleCommand : Command<GetClassicMigrationBundleOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.ClientUnit;
	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";
	private const string ClioMigrationDirectoryName = ".clio-migration";
	private const string ManifestFileName = "manifest.json";
	private const int MaxParentDepth = 20;
	private const int MaxDetails = 50;
	private const int MaxChildPages = 50;
	private const string DefaultCulture = "en-US";

	// Classic client-unit page bodies declare their bound object as `entitySchemaName: "Contact"`. The leading
	// non-word lookbehind stops the match from firing inside a longer identifier (e.g. `masterEntitySchemaName`),
	// so inference binds the page's own entity rather than a related one.
	private static readonly Regex EntityNameRegex = new(
		"(?<![A-Za-z_])entitySchemaName[\"']?\\s*:\\s*[\"']([A-Za-z_][\\w]*)[\"']",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	// Detail schema references in a classic page body: `schemaName: "SomeDetailV2"` (detail-named schemas only).
	// The lookbehind keeps longer identifiers (e.g. `entitySchemaName: "XDetail"`) from matching as details.
	private static readonly Regex DetailSchemaNameRegex = new(
		"(?<![A-Za-z_])schemaName[\"']?\\s*:\\s*[\"']([A-Za-z][\\w]*Detail[\\w]*)[\"']",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	// A detail's edit page: getEditPageName / editPageName / EditPageSchemaName -> "SomePage".
	private static readonly Regex EditPageRegex = new(
		"(?:getEditPageName|editPageName|EditPageSchemaName)[\\s\\S]{0,80}?[\"']([A-Za-z][\\w]+)[\"']",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly IPageDesignerHierarchyClient _hierarchyClient;
	private readonly IFileSystem _fileSystem;
	private readonly IoFileSystem _ioFileSystem;
	private readonly ILogger _logger;

	public GetClassicMigrationBundleCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IRemoteEntitySchemaColumnManager columnManager,
		IPageDesignerHierarchyClient hierarchyClient,
		IFileSystem fileSystem,
		IoFileSystem ioFileSystem,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_columnManager = columnManager;
		_hierarchyClient = hierarchyClient;
		_fileSystem = fileSystem;
		_ioFileSystem = ioFileSystem;
		_logger = logger;
	}

	// Per-invocation caches: one GetSchema per (UId, hierarchy mode) and one layer enumeration per name for
	// the whole assembly, so seed walks, sections, and child pages never re-fetch what an earlier step already
	// loaded. Deliberately per-run (never on the command instance): the MCP path reuses resolved command
	// instances per environment, and an instance-level cache would serve stale schemas across calls.
	private sealed class BundleRunContext {

		public Dictionary<string, (JObject Schema, string Error)> SchemaByCacheKey { get; } =
			new(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, IReadOnlyList<SchemaLayer>> LayersByName { get; } =
			new(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Assembles the migration bundle for <paramref name="options"/> and writes the manifest to disk.
	/// Returns <c>true</c> and a summary response on success; <c>false</c> with <see cref="GetClassicMigrationBundleResponse.Error"/>
	/// set when the schema cannot be resolved, a chain layer fails to load, or the manifest cannot be written.
	/// </summary>
	public virtual bool TryAssembleBundle(GetClassicMigrationBundleOptions options, out GetClassicMigrationBundleResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = Fail("schema-name is required");
				return false;
			}
			// Validate before any network call or path composition: the name is also a path segment of the
			// default manifest location, so this both fails fast and keeps the write confined to the anchor.
			if (!PageSchemaMetadataHelper.IsValidSchemaName(options.SchemaName)) {
				response = Fail(PageSchemaMetadataHelper.SchemaNameFormatError);
				return false;
			}
			var ctx = new BundleRunContext();

			// 1-3. Resolve the page's full replacing-layer chain (schemas[]) AND the parent-template seed[] in ONE
			//      GetParentSchemas designer round-trip (useFullHierarchy=true returns the whole effective chain),
			//      instead of the per-layer LoadLayerChain + per-template-level BuildSeed fan-out (~30+ round-trips
			//      on a heavily-layered page). Falls back to that proven fan-out if the hierarchy call is
			//      unavailable/empty. Live-verified parity: the engine's merged page + Freedom payload are identical
			//      to the fan-out across Contact/Account/Activity/Order pages (see the change summary).
			(JArray schemas, JArray seed, string topLayerUId, string chainError) =
				LoadChainAndSeed(ctx, options.SchemaName);
			if (chainError != null) {
				response = Fail(chainError);
				return false;
			}

			// 4. Resolve the entity (explicit option, else inferred from the bodies).
			string entity = !string.IsNullOrWhiteSpace(options.Entity)
				? options.Entity
				: InferEntity(schemas, seed);

			// 5. Merged localizable strings -> resources (best-effort; the merge folds localization, not the view).
			JObject resources = BuildResources(ctx, topLayerUId, options.SchemaName);

			// 6. Entity columns + titles from the merged entity schema (best-effort).
			(JObject entityColumns, JObject columnTitles) = BuildEntityColumns(entity);

			// 6b. Enrichers (best-effort, heuristic; omit unresolved, never fabricate). All enricher names are
			//     primed through ONE batched SelectQuery so the fan-out does not pay a round-trip per name.
			List<string> detailNames = CollectDetailNames(schemas, seed);
			var enricherNames = new List<string>(detailNames);
			if (!string.IsNullOrWhiteSpace(entity)) {
				enricherNames.Add(entity + "SectionV2");
				enricherNames.Add(entity + "Section");
			}
			PrimeLayerBatch(ctx, enricherNames);
			JObject detailSchemas = BuildDetailSchemas(ctx, detailNames);
			JArray section = BuildSection(ctx, entity);
			JObject childPageSchemas = BuildChildPageSchemas(ctx, detailSchemas);

			// 7. Assemble the manifest in the engine's contract shape (omit empty fields, never null-fill).
			var manifest = new JObject { ["schemas"] = schemas };
			if (!string.IsNullOrWhiteSpace(entity)) {
				manifest["entity"] = entity;
			}
			if (seed.Count > 0) {
				manifest["seed"] = seed;
			}
			if (entityColumns.HasValues) {
				manifest["entityColumns"] = entityColumns;
			}
			if (columnTitles.HasValues) {
				manifest["columnTitles"] = columnTitles;
			}
			if (resources.HasValues) {
				manifest["resources"] = resources;
			}
			if (detailSchemas.HasValues) {
				manifest["detailSchemas"] = detailSchemas;
			}
			if (section.Count > 0) {
				manifest["section"] = section;
			}
			if (childPageSchemas.HasValues) {
				manifest["childPageSchemas"] = childPageSchemas;
			}

			// 8. Write the manifest to disk. The bodies live here, not in the response.
			string manifestPath = ResolveOutputPath(options);
			string directory = Path.GetDirectoryName(manifestPath);
			if (!string.IsNullOrWhiteSpace(directory)) {
				_fileSystem.CreateDirectoryIfNotExists(directory);
			}
			_fileSystem.WriteAllTextToFile(manifestPath, manifest.ToString(Formatting.Indented));

			response = new GetClassicMigrationBundleResponse {
				Success = true,
				SchemaName = options.SchemaName,
				Entity = entity,
				ManifestPath = manifestPath,
				LayerCount = schemas.Count,
				SeedCount = seed.Count,
				ResourceCount = resources.Count,
				ColumnCount = columnTitles.Count,
				DetailCount = detailSchemas.Count,
				SectionLayerCount = section.Count,
				ChildPageCount = childPageSchemas.Count
			};
			return true;
		}
		catch (Exception ex) {
			response = Fail(ex.Message);
			return false;
		}
	}

	/// <inheritdoc />
	public override int Execute(GetClassicMigrationBundleOptions options) {
		bool success = TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);
		_logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}

	private (JObject schema, string error) LoadSchemaCached(
		BundleRunContext ctx, string schemaUId, string schemaName, bool useFullHierarchy = false) {
		string cacheKey = schemaUId + (useFullHierarchy ? "|merged" : "|own");
		if (ctx.SchemaByCacheKey.TryGetValue(cacheKey, out (JObject Schema, string Error) cached)) {
			return cached;
		}
		(JObject schema, string error) result = SchemaDesignerHelper.LoadSchema(
			_applicationClient, _serviceUrlBuilder, schemaUId, Kind, schemaName, useFullHierarchy);
		ctx.SchemaByCacheKey[cacheKey] = result;
		return result;
	}

	private (IReadOnlyList<SchemaLayer> layers, string error) EnumerateLayersCached(BundleRunContext ctx, string schemaName) {
		if (ctx.LayersByName.TryGetValue(schemaName, out IReadOnlyList<SchemaLayer> cached)) {
			return (cached, null);
		}
		(IReadOnlyList<SchemaLayer> layers, string error) = SchemaDesignerHelper.EnumerateSchemaLayers(
			_applicationClient, _serviceUrlBuilder, schemaName, Kind);
		if (error == null) {
			// "Not found" (empty list) is memoized; transport/permission errors are not, so a retry elsewhere
			// in the run still gets its chance.
			ctx.LayersByName[schemaName] = layers;
		}
		return (layers, error);
	}

	// Resolves many names in ONE SelectQuery and seeds the enumeration cache — including empty entries for
	// names that do not exist, so later per-name lookups don't re-query them. A batch failure only logs:
	// every consumer falls back to the memoized per-name path.
	private void PrimeLayerBatch(BundleRunContext ctx, IReadOnlyCollection<string> schemaNames) {
		List<string> missing = schemaNames
			.Where(name => !string.IsNullOrWhiteSpace(name) && !ctx.LayersByName.ContainsKey(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (missing.Count == 0) {
			return;
		}
		try {
			(IReadOnlyDictionary<string, IReadOnlyList<SchemaLayer>> layersByName, string error) =
				SchemaDesignerHelper.EnumerateSchemaLayersBatch(_applicationClient, _serviceUrlBuilder, missing, Kind);
			if (error != null) {
				_logger.WriteWarning($"Batched layer enumeration failed; falling back to per-name lookups: {error}");
				return;
			}
			foreach (KeyValuePair<string, IReadOnlyList<SchemaLayer>> entry in layersByName) {
				ctx.LayersByName[entry.Key] = entry.Value;
			}
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Batched layer enumeration failed; falling back to per-name lookups: {ex.Message}");
		}
	}

	// Resolves BOTH the page's replacing-layer chain (schemas[]) and its parent-template seed[] from a SINGLE
	// GetParentSchemas designer call (useFullHierarchy=true returns the whole effective chain), instead of the
	// per-layer LoadLayerChain + per-template-level BuildSeed fan-out (~30+ round-trips on a heavily-layered
	// page). The flat hierarchy is ordered base->top; layers named schemaName become schemas[], the rest seed[].
	// On any designer/transport failure or an unexpectedly empty result it degrades to the proven legacy fan-out,
	// so the bundle is never worse than before.
	private (JArray schemas, JArray seed, string topLayerUId, string error) LoadChainAndSeed(
		BundleRunContext ctx, string schemaName) {
		try {
			IReadOnlyList<PageDesignerHierarchySchema> hierarchy = ResolveHierarchyBaseToTop(schemaName);
			if (hierarchy is { Count: > 0 }) {
				var schemas = new JArray();
				var seed = new JArray();
				string topLayerUId = null;
				foreach (PageDesignerHierarchySchema layer in hierarchy) {
					string body = layer.Body ?? string.Empty;
					if (string.Equals(layer.Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
						// pkg is provenance the engine matches against clientEditableSchemas — mirror LoadLayerChain.
						schemas.Add(new JObject { ["pkg"] = layer.PackageName, ["body"] = body });
						topLayerUId = layer.UId; // base->top: the last page layer is the most-derived (top) layer.
					}
					else {
						seed.Add(CreateSeedEntry(layer.PackageName, body));
					}
				}
				if (schemas.Count > 0) {
					return (schemas, seed, topLayerUId, null);
				}
				// The hierarchy carried no layer named schemaName (unexpected) — fall back rather than emit an
				// empty schemas[] the engine would reject.
				_logger.WriteWarning(
					$"GetParentSchemas returned no '{schemaName}' layer; falling back to per-layer enumeration.");
			}
		}
		catch (Exception ex) {
			_logger.WriteWarning(
				$"GetParentSchemas hierarchy resolution failed ({ex.Message}); falling back to per-layer enumeration.");
		}
		return LoadChainAndSeedLegacy(ctx, schemaName);
	}

	// The proven per-layer fan-out, kept as the fallback for LoadChainAndSeed (and still used directly by the
	// section/child-page enrichers): the same-named layer chain -> schemas[], then the parent-template walk -> seed[].
	private (JArray schemas, JArray seed, string topLayerUId, string error) LoadChainAndSeedLegacy(
		BundleRunContext ctx, string schemaName) {
		(JArray schemas, JObject topSchema, string topLayerUId, string chainError) = LoadLayerChain(ctx, schemaName);
		if (chainError != null) {
			return (null, null, null, chainError);
		}
		JArray seed = BuildSeed(ctx, topSchema);
		return (schemas, seed, topLayerUId, null);
	}

	// Mirrors get-page / get-page-hierarchy chain resolution (unifying the copies is tracked as ENG-93249):
	// resolve name -> UId + package, ask the designer for the design package (fallback to the schema's package),
	// fetch the full hierarchy, then re-anchor on the ROOT variant of the name (a name->UId lookup can resolve to
	// an arbitrary replacing layer) and re-fetch. Returned base->top: the service yields leaf-first, reversed here
	// to match the engine's merge order. Returns null when the schema cannot be resolved (caller falls back).
	private IReadOnlyList<PageDesignerHierarchySchema> ResolveHierarchyBaseToTop(string schemaName) {
		(JToken metadata, _) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			_applicationClient, _serviceUrlBuilder, schemaName,
			("UId", "UId"), ("PackageUId", "SysPackage.UId"));
		string schemaUId = metadata?["UId"]?.ToString();
		string packageUId = metadata?["PackageUId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
			return null;
		}
		string designPackageUId;
		try {
			designPackageUId = _hierarchyClient.GetDesignPackageUId(schemaUId);
		}
		catch (Exception ex) {
			// best-effort: the design package resolves to the schema's own package below. Logged at debug so the
			// degradation is diagnosable without adding noise to the common case (the fallback yields a correct anchor).
			_logger.WriteDebug(
				$"GetDesignPackageUId failed for '{schemaName}' ({ex.Message}); anchoring on the schema's own package.");
			designPackageUId = null;
		}
		if (string.IsNullOrWhiteSpace(designPackageUId)) {
			designPackageUId = packageUId;
		}
		IReadOnlyList<PageDesignerHierarchySchema> initial =
			_hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		if (initial.Count == 0) {
			return null;
		}
		string rootSchemaUId = FindRootSchemaUId(initial, schemaName) ?? schemaUId;
		IReadOnlyList<PageDesignerHierarchySchema> leafFirst;
		if (string.Equals(rootSchemaUId, schemaUId, StringComparison.OrdinalIgnoreCase)) {
			leafFirst = initial;
		}
		else {
			IReadOnlyList<PageDesignerHierarchySchema> full = _hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
			leafFirst = full.Count > 0 ? full : initial;
		}
		return leafFirst.Reverse().ToList(); // leaf-first -> base->top
	}

	// The root variant is the LAST occurrence of the requested name in the leaf-first hierarchy (the most-base
	// replacing layer of the page itself), mirroring get-page's normalization.
	private static string FindRootSchemaUId(IReadOnlyList<PageDesignerHierarchySchema> hierarchy, string schemaName) {
		for (int i = hierarchy.Count - 1; i >= 0; i--) {
			if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
				return hierarchy[i].UId;
			}
		}
		return null;
	}

	// Enumerates a schema's replacing-layer chain and loads every layer body, producing the engine-facing
	// [{pkg, body}] array base->top plus the most-derived layer (for parent walks). Shared by the main chain,
	// the section gatherer, and child-page manifests.
	private (JArray schemas, JObject topSchema, string topUId, string error) LoadLayerChain(
		BundleRunContext ctx, string schemaName) {
		(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, schemaName);
		if (enumError != null) {
			return (null, null, null, enumError);
		}
		if (layers.Count == 0) {
			return (null, null, null, $"Schema '{schemaName}' not found (ManagerName='{Kind.ManagerName}')");
		}
		var schemas = new JArray();
		JObject topSchema = null;
		string topUId = null;
		foreach (SchemaLayer layer in layers) {
			(JObject layerSchema, string loadError) = LoadSchemaCached(ctx, layer.UId, schemaName);
			if (loadError != null) {
				return (null, null, null, $"Failed to load layer '{layer.PackageName}' ({layer.UId}): {loadError}");
			}
			schemas.Add(new JObject {
				["pkg"] = layer.PackageName,
				["body"] = layerSchema["body"]?.ToString() ?? string.Empty
			});
			topSchema = layerSchema;
			topUId = layer.UId;
		}
		return (schemas, topSchema, topUId, null);
	}

	private JArray BuildSeed(BundleRunContext ctx, JObject topSchema) {
		// Walk `parent` from the top layer up to the base template. At EACH template level, enumerate every
		// same-named layer (a parent template can itself be replaced across packages) so the seed carries the
		// whole layer set per level — not just the single parent.uId layer. Seeding only the linked layer drops
		// base containers defined in a sibling layer, which the engine then reports as unresolvedParents.
		// The visited sets are per-seed (NOT per-run): a nested child manifest folds independently and must
		// carry its own full seed even when it shares templates with the main page.
		var levels = new List<List<JObject>>(); // top-first; reversed to base->top at the end
		var visitedParentUId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var seededTemplateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var seededLayerUIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		JObject current = topSchema;
		int depth = 0;
		while (true) {
			string parentUId = (current?["parent"] as JObject)?["uId"]?.ToString();
			if (string.IsNullOrWhiteSpace(parentUId) || string.Equals(parentUId, EmptyGuid, StringComparison.OrdinalIgnoreCase)) {
				break; // reached the base template — a clean, complete walk
			}
			if (depth >= MaxParentDepth) {
				// Depth cap hit with a parent still to follow: the seed is truncated. Say so, or a truncated
				// seed looks identical to a page that simply has no more parents (parity with the other exits).
				_logger.WriteWarning(
					$"Parent-template walk stopped at the depth cap ({MaxParentDepth}); the seed may be truncated " +
					$"(next unwalked parent '{parentUId}').");
				break;
			}
			if (!visitedParentUId.Add(parentUId)) {
				// Cycle on the parent-link walk: stop and say so — silently truncating hides a corrupt chain.
				_logger.WriteWarning(
					$"Parent-template walk stopped on a cycle at '{parentUId}'; the seed may be truncated.");
				break;
			}
			(JObject parentLayer, string error) = LoadSchemaCached(ctx, parentUId, null);
			if (error != null || parentLayer == null) {
				// Best-effort: stop the walk and keep what we have — but say so, or a truncated seed looks
				// identical to a page that simply has no parents.
				_logger.WriteWarning($"Parent-template walk stopped at '{parentUId}': {error ?? "no schema returned"}");
				break;
			}
			levels.Add(LoadParentLevelLayers(ctx, parentUId, parentLayer, seededTemplateNames, seededLayerUIds));
			current = parentLayer; // continue up from the linked layer's own parent
			depth++;
		}
		levels.Reverse(); // base template first, most-derived template last
		var seed = new JArray();
		foreach (List<JObject> levelEntries in levels) {
			foreach (JObject entry in levelEntries) {
				seed.Add(entry);
			}
		}
		return seed;
	}

	// Returns the seed entries of one parent-template level base->top: every same-named layer when the template
	// can be enumerated, else just the linked layer. Layer UIds are tracked across the walk so a chain that
	// revisits the same template (e.g. a parent link into a replaced sibling) never seeds a duplicate body.
	private List<JObject> LoadParentLevelLayers(
		BundleRunContext ctx,
		string parentUId,
		JObject parentLayer,
		HashSet<string> seededTemplateNames,
		HashSet<string> seededLayerUIds) {
		string parentName = parentLayer["name"]?.ToString();
		if (string.IsNullOrWhiteSpace(parentName) || !seededTemplateNames.Add(parentName)) {
			return seededLayerUIds.Add(parentUId)
				? [CreateSeedEntry(parentLayer, parentLayer["package"]?["name"]?.ToString())]
				: [];
		}
		(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, parentName);
		if (enumError != null || layers.Count == 0) {
			if (enumError != null) {
				_logger.WriteWarning($"Could not enumerate parent template '{parentName}' layers: {enumError}");
			}
			return seededLayerUIds.Add(parentUId)
				? [CreateSeedEntry(parentLayer, parentLayer["package"]?["name"]?.ToString())]
				: [];
		}
		var levelEntries = new List<JObject>();
		foreach (SchemaLayer layer in layers) {
			if (!seededLayerUIds.Add(layer.UId)) {
				continue;
			}
			if (string.Equals(layer.UId, parentUId, StringComparison.OrdinalIgnoreCase)) {
				levelEntries.Add(CreateSeedEntry(parentLayer, layer.PackageName)); // reuse the loaded linked layer
				continue;
			}
			(JObject layerSchema, string loadError) = LoadSchemaCached(ctx, layer.UId, parentName);
			if (loadError != null || layerSchema == null) {
				_logger.WriteWarning($"Could not load parent-template layer '{parentName}' ({layer.UId}): {loadError ?? "no schema returned"}");
				continue;
			}
			levelEntries.Add(CreateSeedEntry(layerSchema, layer.PackageName));
		}
		if (levelEntries.Count == 0 && seededLayerUIds.Add(parentUId)) {
			// Every enumerated sibling failed to load (the linked layer itself was not among the rows):
			// never drop the level entirely — seed at least the layer the walk already holds.
			levelEntries.Add(CreateSeedEntry(parentLayer, parentLayer["package"]?["name"]?.ToString()));
		}
		return levelEntries;
	}

	// pkg is provenance the engine matches against clientEditableSchemas — when the owning package is unknown
	// the property is omitted (an honest gap), never substituted with a value of the wrong kind.
	private static JObject CreateSeedEntry(JObject layerSchema, string packageName) =>
		CreateSeedEntry(packageName, layerSchema["body"]?.ToString());

	// Overload for the GetParentSchemas path, whose layer body is already a plain string (not a schema JObject).
	private static JObject CreateSeedEntry(string packageName, string body) {
		var entry = new JObject();
		if (!string.IsNullOrWhiteSpace(packageName)) {
			entry["pkg"] = packageName;
		}
		entry["body"] = body ?? string.Empty;
		return entry;
	}

	private string InferEntity(JArray schemas, JArray seed) {
		// Prefer the page's own layer chain (most specific), then the parent-template seed.
		foreach (JToken entry in schemas.Concat(seed)) {
			string body = entry["body"]?.ToString();
			if (string.IsNullOrEmpty(body)) {
				continue;
			}
			Match match = SafeMatch(EntityNameRegex, body, "inferring the bound entity");
			if (match.Success) {
				return match.Groups[1].Value;
			}
		}
		return null;
	}

	private JObject BuildResources(BundleRunContext ctx, string topLayerUId, string schemaName) {
		var resources = new JObject();
		try {
			(JObject schema, string error) = LoadSchemaCached(ctx, topLayerUId, schemaName, useFullHierarchy: true);
			if (error != null || schema == null) {
				_logger.WriteWarning($"Could not gather merged localizable strings (resources): {error ?? "no schema returned"}");
				return resources;
			}
			foreach (MergedLocalizableString localizableString in SchemaDesignerHelper.ExtractMergedLocalizableStrings(schema)) {
				if (string.IsNullOrWhiteSpace(localizableString.Name) || localizableString.Values.Count == 0) {
					continue;
				}
				string value = localizableString.Values
						.FirstOrDefault(v => string.Equals(v.CultureName, DefaultCulture, StringComparison.OrdinalIgnoreCase))?.Value
					?? localizableString.Values[0].Value;
				if (!string.IsNullOrEmpty(value) && resources[localizableString.Name] == null) {
					resources[localizableString.Name] = value;
				}
			}
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Could not gather merged localizable strings (resources): {ex.Message}");
		}
		return resources;
	}

	private (JObject entityColumns, JObject columnTitles) BuildEntityColumns(string entity) {
		var entityColumns = new JObject();
		var columnTitles = new JObject();
		if (string.IsNullOrWhiteSpace(entity)) {
			return (entityColumns, columnTitles);
		}
		try {
			// Package omitted => the merged/effective schema (own + inherited columns from every package layer).
			// Only the schema name travels in the options: the injected column manager is already bound to this
			// command's environment (both the CLI dispatch and the MCP ResolveCommand path build the command
			// from an environment-scoped container).
			var propertyOptions = new GetEntitySchemaPropertiesOptions { SchemaName = entity };
			EntitySchemaPropertiesInfo properties = _columnManager.GetSchemaProperties(propertyOptions);
			foreach (EntitySchemaPropertyColumnInfo column in properties.Columns ?? []) {
				if (string.IsNullOrWhiteSpace(column.Name)) {
					continue;
				}
				var columnMeta = new JObject();
				if (!string.IsNullOrWhiteSpace(column.Type)) {
					columnMeta["type"] = column.Type;
				}
				if (!string.IsNullOrWhiteSpace(column.ReferenceSchemaName)) {
					columnMeta["ref"] = column.ReferenceSchemaName;
				}
				if (columnMeta.HasValues) {
					entityColumns[column.Name] = columnMeta;
				}
				if (!string.IsNullOrWhiteSpace(column.Title)) {
					columnTitles[column.Name] = column.Title;
				}
			}
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Could not gather entity columns for '{entity}': {ex.Message}");
		}
		return (entityColumns, columnTitles);
	}

	// Collects distinct detail-schema names referenced across every layer body (page chain + parent seed).
	// The names come from server-supplied bodies, so collection is capped by ATTEMPTS — not by later successes —
	// to keep a malformed or hostile response from driving unbounded probing.
	private List<string> CollectDetailNames(JArray schemas, JArray seed) {
		var detailNames = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int collectionCap = MaxDetails * 2;
		foreach (JToken entry in schemas.Concat(seed)) {
			string body = entry["body"]?.ToString();
			if (string.IsNullOrEmpty(body)) {
				continue;
			}
			foreach (Match match in SafeMatches(DetailSchemaNameRegex, body, "collecting detail-schema references")) {
				string detailName = match.Groups[1].Value;
				if (!seen.Add(detailName)) {
					continue;
				}
				if (detailNames.Count >= collectionCap) {
					_logger.WriteWarning(
						$"More than {collectionCap} distinct detail-schema references found; the remainder is ignored.");
					return detailNames;
				}
				detailNames.Add(detailName);
			}
		}
		return detailNames;
	}

	private JObject BuildDetailSchemas(BundleRunContext ctx, IReadOnlyList<string> detailNames) {
		var detailSchemas = new JObject();
		foreach (string detailName in detailNames) {
			if (detailSchemas.Count >= MaxDetails) {
				_logger.WriteWarning($"Detail gathering stopped at {MaxDetails} resolved schemas; the remainder is omitted.");
				break;
			}
			try {
				(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, detailName);
				if (enumError != null) {
					_logger.WriteWarning($"Could not gather detail schema '{detailName}': {enumError}");
					continue;
				}
				if (layers.Count == 0) {
					continue; // omit: an unresolved detail is left for the engine to flag, never fabricated
				}
				string topUId = layers[layers.Count - 1].UId;
				(JObject detailSchema, string loadError) = LoadSchemaCached(ctx, topUId, detailName);
				if (loadError != null || detailSchema == null) {
					_logger.WriteWarning($"Could not gather detail schema '{detailName}': {loadError ?? "no schema returned"}");
					continue;
				}
				var detailEntry = new JObject { ["body"] = detailSchema["body"]?.ToString() ?? string.Empty };
				string title = SchemaDesignerHelper.ExtractCaption(detailSchema);
				if (!string.IsNullOrWhiteSpace(title)) {
					detailEntry["title"] = title;
				}
				detailSchemas[detailName] = detailEntry;
			}
			catch (Exception ex) {
				_logger.WriteWarning($"Could not gather detail schema '{detailName}': {ex.Message}");
			}
		}
		return detailSchemas;
	}

	private JArray BuildSection(BundleRunContext ctx, string entity) {
		var section = new JArray();
		if (string.IsNullOrWhiteSpace(entity)) {
			return section;
		}
		// The classic list-page section follows the <Entity>Section[V2] naming convention.
		foreach (string candidate in new[] { entity + "SectionV2", entity + "Section" }) {
			try {
				(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, candidate);
				if (enumError != null) {
					_logger.WriteWarning($"Could not gather section '{candidate}': {enumError}");
					continue;
				}
				if (layers.Count == 0) {
					continue;
				}
				(JArray sectionSchemas, _, _, string chainError) = LoadLayerChain(ctx, candidate);
				if (chainError != null) {
					// Omit the whole candidate rather than emit a partial chain the engine would misfold.
					_logger.WriteWarning($"Could not gather section '{candidate}': {chainError}");
					continue;
				}
				if (sectionSchemas.Count > 0) {
					return sectionSchemas; // the first naming convention that resolves wins
				}
			}
			catch (Exception ex) {
				_logger.WriteWarning($"Could not gather section '{candidate}': {ex.Message}");
			}
		}
		return section;
	}

	private JObject BuildChildPageSchemas(BundleRunContext ctx, JObject detailSchemas) {
		var childPageSchemas = new JObject();
		// Collect the distinct edit-page names first so the whole set is resolved in one batched enumeration.
		var editPageNames = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JProperty detail in detailSchemas.Properties()) {
			string detailBody = detail.Value["body"]?.ToString();
			if (string.IsNullOrEmpty(detailBody)) {
				continue;
			}
			Match editPageMatch = SafeMatch(EditPageRegex, detailBody, "resolving the detail's edit page");
			if (!editPageMatch.Success) {
				continue; // no edit page named on the detail -> nothing to nest; the engine flags it
			}
			string editPageName = editPageMatch.Groups[1].Value;
			if (!seen.Add(editPageName)) {
				continue;
			}
			if (editPageNames.Count >= MaxChildPages) {
				_logger.WriteWarning($"More than {MaxChildPages} distinct child edit pages referenced; the remainder is omitted.");
				break;
			}
			editPageNames.Add(editPageName);
		}
		PrimeLayerBatch(ctx, editPageNames);
		foreach (string editPageName in editPageNames) {
			try {
				(JObject childManifest, string error) = AssembleChildManifest(ctx, editPageName);
				if (error != null) {
					_logger.WriteWarning($"Could not assemble child page '{editPageName}': {error}");
					continue;
				}
				if (childManifest != null) {
					childPageSchemas[editPageName] = childManifest;
				}
			}
			catch (Exception ex) {
				_logger.WriteWarning($"Could not assemble child page '{editPageName}': {ex.Message}");
			}
		}
		return childPageSchemas;
	}

	// Assembles the CORE nested manifest (schemas + seed + entity) for a child edit page. Bounded to one
	// level of children — the engine recursively maps the nested manifest and depth-caps its own display.
	// An edit-page name that resolves to no schema is a heuristic miss and is omitted silently (null, null).
	private (JObject manifest, string error) AssembleChildManifest(BundleRunContext ctx, string editPageName) {
		(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, editPageName);
		if (enumError != null) {
			return (null, enumError);
		}
		if (layers.Count == 0) {
			return (null, null);
		}
		(JArray schemas, JObject topSchema, _, string chainError) = LoadLayerChain(ctx, editPageName);
		if (chainError != null) {
			return (null, chainError);
		}
		JArray seed = BuildSeed(ctx, topSchema);
		string entity = InferEntity(schemas, seed);
		var manifest = new JObject { ["schemas"] = schemas };
		if (!string.IsNullOrWhiteSpace(entity)) {
			manifest["entity"] = entity;
		}
		if (seed.Count > 0) {
			manifest["seed"] = seed;
		}
		return (manifest, null);
	}

	// The returned path is ALWAYS absolute: the MCP server's working directory is unknown to the caller
	// (frequently $HOME or the install dir), so a relative path in the response would be unresolvable.
	// The default is anchored the way get-page anchors .clio-pages: workspace root when one encloses the
	// current directory, the current directory otherwise, and the managed clio home instead of the bare
	// home directory (PRD OQ-04 / PageOutputDirectoryResolver).
	private string ResolveOutputPath(GetClassicMigrationBundleOptions options) {
		// H1: reading the process-global cwd must serialize against the MCP workspace tools that PIN cwd.
		// In the MCP path this runs under the shared tool lock; in the single-threaded CLI path the lock
		// is uncontended.
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
				return _ioFileSystem.Path.GetFullPath(options.OutputFile);
			}
			string anchor = PageOutputDirectoryResolver.ResolveAnchor(
				_ioFileSystem,
				_ioFileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				null);
			return Path.Combine(anchor, ClioMigrationDirectoryName, options.SchemaName, ManifestFileName);
		}
	}

	private static GetClassicMigrationBundleResponse Fail(string error) =>
		new() { Success = false, Error = error };

	// Best-effort regex evaluation over server-supplied bodies. The compiled patterns carry a 1s match timeout;
	// a timeout on one pathological body must DEGRADE (skip that body, keep the rest of the bundle) exactly like
	// every other enricher, never abort the whole assembly. Every Match/Matches call funnels through these two
	// guards so no regex pass can turn a would-be-successful bundle into a hard failure.
	private Match SafeMatch(Regex regex, string body, string what) {
		try {
			return regex.Match(body);
		}
		catch (RegexMatchTimeoutException ex) {
			_logger.WriteWarning($"Regex match timed out while {what}; skipping this body: {ex.Message}");
			return Match.Empty;
		}
	}

	private IReadOnlyList<Match> SafeMatches(Regex regex, string body, string what) {
		try {
			// Materialize inside the try: Regex.Matches is lazily evaluated, so a timeout would otherwise surface
			// at the caller's enumeration site — outside this guard — rather than being caught here.
			return regex.Matches(body).ToList();
		}
		catch (RegexMatchTimeoutException ex) {
			_logger.WriteWarning($"Regex match timed out while {what}; skipping this body: {ex.Message}");
			return [];
		}
	}
}
