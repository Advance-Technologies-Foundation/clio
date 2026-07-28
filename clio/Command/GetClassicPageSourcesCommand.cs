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

/// <summary>Options for the <c>get-classic-page-sources</c> command.</summary>
// Legacy verb names (get-classic-migration-bundle / classic-migration-bundle) stay as aliases so guidance and
// scripts written against the old name keep resolving after the rename (ENG-94218).
[Verb("get-classic-page-sources",
	Aliases = ["classic-page-sources", "get-classic-migration-bundle", "classic-migration-bundle"],
	HelpText = "Collect the Classic page sources for folding (the full replacing-layer chain + parent-template " +
		"seed + resolution inputs) and write the manifest JSON to disk for the migration engine to fold")]
public class GetClassicPageSourcesOptions : EnvironmentOptions {

	/// <summary>Classic client-unit (page) schema name the page sources are collected for.</summary>
	[Option("schema-name", Required = true, HelpText = "Classic client-unit (page) schema name to collect the page sources for")]
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
/// Summary envelope returned by <c>get-classic-page-sources</c>. Carries the absolute manifest path and
/// per-block counts — never the schema bodies themselves (those live only in the manifest file).
/// </summary>
public sealed class GetClassicPageSourcesResponse {

	/// <summary>Whether the page sources were collected and written.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>The classic page schema the page sources were collected for.</summary>
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

	/// <summary>
	/// Non-fatal gaps the caller must weigh before acting on the manifest (for example: no section resolved, so the
	/// plan's List-page analysis will be empty). <c>null</c> when the collected sources are complete.
	/// </summary>
	[System.Text.Json.Serialization.JsonPropertyName("warnings")]
	public IReadOnlyList<string> Warnings { get; set; }

	/// <summary>Failure reason when <see cref="Success"/> is <c>false</c>; <c>null</c> otherwise.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Collects the Classic page sources server-side and writes it to disk in the shape the toolkit
/// Node engine (migrate.mjs) folds: the whole replacing-schema layer chain (base-&gt;top) plus the parent-template
/// seed, plus resolution inputs (entityColumns/columnTitles/resources). The layer bodies are written to the
/// manifest file, never returned in the response — the caller triggers the run and reads only the small summary.
/// </summary>
public class GetClassicPageSourcesCommand : Command<GetClassicPageSourcesOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.ClientUnit;
	private const string EmptyGuid = ClassicEntitySchemaQuery.EmptyGuid;
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

	// Test seam (instance-scoped, never mutated in production): the entity-inference regex InferEntity runs.
	// Defaults to the compiled EntityNameRegex; a command-level test can point it at a tiny-timeout pattern to
	// exercise the regex-timeout degradation through the real TryAssembleBundle path (not just SafeMatch alone).
	internal Regex EntityInferenceRegex { get; set; } = EntityNameRegex;

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
	private readonly IClassicSectionSchemaResolver _sectionResolver;
	private readonly IFileSystem _fileSystem;
	private readonly IoFileSystem _ioFileSystem;
	private readonly ILogger _logger;

	public GetClassicPageSourcesCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IRemoteEntitySchemaColumnManager columnManager,
		IPageDesignerHierarchyClient hierarchyClient,
		IClassicSectionSchemaResolver sectionResolver,
		IFileSystem fileSystem,
		IoFileSystem ioFileSystem,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_columnManager = columnManager;
		_hierarchyClient = hierarchyClient;
		_sectionResolver = sectionResolver;
		_fileSystem = fileSystem;
		_ioFileSystem = ioFileSystem;
		_logger = logger;
	}

	// Per-invocation caches: one GetSchema per (UId, hierarchy mode) and one layer enumeration per name for
	// the whole assembly, so seed walks, sections, and child pages never re-fetch what an earlier step already
	// loaded. Deliberately per-run (never on the command instance): the MCP path reuses resolved command
	// instances per environment, and an instance-level cache would serve stale schemas across calls.
	private sealed class PageSourcesRunContext {

		public Dictionary<string, (JObject Schema, string Error)> SchemaByCacheKey { get; } =
			new(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, IReadOnlyList<SchemaLayer>> LayersByName { get; } =
			new(StringComparer.OrdinalIgnoreCase);

		// Non-fatal gaps gathered anywhere in the assembly (including nested child manifests) and surfaced on
		// GetClassicPageSourcesResponse.Warnings. Lives here, not on the command, for the same reason as the
		// caches: the MCP path reuses command instances per environment.
		public List<string> Warnings { get; } = [];
	}

	/// <summary>
	/// Collects the Classic page sources for <paramref name="options"/> and writes the manifest to disk.
	/// Returns <c>true</c> and a summary response on success; <c>false</c> with <see cref="GetClassicPageSourcesResponse.Error"/>
	/// set when the schema cannot be resolved, a chain layer fails to load, or the manifest cannot be written.
	/// </summary>
	public virtual bool TryAssemblePageSources(GetClassicPageSourcesOptions options, out GetClassicPageSourcesResponse response) {
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
			var ctx = new PageSourcesRunContext();

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
				: InferEntity(ctx, schemas, seed);

			// 5. Merged localizable strings -> resources (best-effort; the merge folds localization, not the view).
			JObject resources = BuildResources(ctx, topLayerUId, options.SchemaName);

			// 6. Entity columns + titles from the merged entity schema (best-effort).
			(JObject entityColumns, JObject columnTitles) = BuildEntityColumns(ctx, entity);

			// 6b. Enrichers (best-effort, heuristic; omit unresolved, never fabricate). All enricher names are
			//     primed through ONE batched SelectQuery so the fan-out does not pay a round-trip per name.
			List<string> detailNames = CollectDetailNames(ctx, schemas, seed);
			IReadOnlyList<string> sectionCandidates = ResolveSectionCandidates(ctx, options.SchemaName, entity);
			var enricherNames = new List<string>(detailNames);
			enricherNames.AddRange(sectionCandidates);
			PrimeLayerBatch(ctx, enricherNames);
			JObject detailSchemas = BuildDetailSchemas(ctx, detailNames);
			JArray section = BuildSection(ctx, sectionCandidates);
			JObject childPageSchemas = BuildChildPageSchemas(ctx, detailSchemas);
			if (section.Count == 0) {
				// sectionLayerCount:0 alone cannot be told apart from "this entity has no Classic section", and an
				// omitted section silently empties the plan's List-page analysis (custom quick filters,
				// getSectionActions, hardcoded list columns). Say so in the response — a logger warning would not
				// reach an MCP caller, whose log buffer is cleared before the result is returned.
				ctx.Warnings.Add(
					"No Classic section resolved for " +
					(string.IsNullOrWhiteSpace(entity) ? $"page '{options.SchemaName}'" : $"entity '{entity}'") +
					$" (tried: {string.Join(", ", sectionCandidates)}). The manifest carries no section, so the " +
					"List-page side of the migration plan will be empty. Verify whether a section exists before " +
					"treating this as 'nothing to migrate'.");
			}

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
			(string manifestPath, string pathError) = ResolveOutputPath(options);
			if (pathError != null) {
				response = Fail(pathError);
				return false;
			}
			string manifestContent = manifest.ToString(Formatting.Indented);
			if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
				// Explicit, possibly agent-supplied output-file: atomic no-overwrite write (FileMode.CreateNew)
				// through the confinement guard so the Destructive=false refuse-overwrite contract holds against a
				// target planted after Resolve, and a symlink at the resolved path cannot redirect the write.
				try {
					OutputPathConfinement.WriteAtomic(_ioFileSystem, manifestPath, manifestContent);
				}
				catch (IOException ex) {
					response = Fail(ex.Message);
					return false;
				}
			} else {
				// Tool-owned default path: re-runnable, overwrites its own prior output.
				string directory = Path.GetDirectoryName(manifestPath);
				if (!string.IsNullOrWhiteSpace(directory)) {
					_fileSystem.CreateDirectoryIfNotExists(directory);
				}
				_fileSystem.WriteAllTextToFile(manifestPath, manifestContent);
			}

			response = new GetClassicPageSourcesResponse {
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
				ChildPageCount = childPageSchemas.Count,
				Warnings = ctx.Warnings.Count > 0 ? ctx.Warnings : null
			};
			return true;
		}
		catch (Exception ex) {
			response = Fail(ex.Message);
			return false;
		}
	}

	/// <inheritdoc />
	public override int Execute(GetClassicPageSourcesOptions options) {
		bool success = TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);
		_logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}

	private (JObject schema, string error) LoadSchemaCached(
		PageSourcesRunContext ctx, string schemaUId, string schemaName, bool useFullHierarchy = false) {
		string cacheKey = schemaUId + (useFullHierarchy ? "|merged" : "|own");
		if (ctx.SchemaByCacheKey.TryGetValue(cacheKey, out (JObject Schema, string Error) cached)) {
			return cached;
		}
		(JObject schema, string error) result = SchemaDesignerHelper.LoadSchema(
			_applicationClient, _serviceUrlBuilder, schemaUId, Kind, schemaName, useFullHierarchy);
		ctx.SchemaByCacheKey[cacheKey] = result;
		return result;
	}

	private (IReadOnlyList<SchemaLayer> layers, string error) EnumerateLayersCached(PageSourcesRunContext ctx, string schemaName) {
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
	private void PrimeLayerBatch(PageSourcesRunContext ctx, IReadOnlyCollection<string> schemaNames) {
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
	// so the collected sources are never worse than before.
	private (JArray schemas, JArray seed, string topLayerUId, string error) LoadChainAndSeed(
		PageSourcesRunContext ctx, string schemaName) {
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
		PageSourcesRunContext ctx, string schemaName) {
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
		PageSourcesRunContext ctx, string schemaName) {
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

	private JArray BuildSeed(PageSourcesRunContext ctx, JObject topSchema) {
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
				// Surface to ctx.Warnings too: a logger warning does not reach an MCP caller (log buffer cleared
				// before the result is returned), so a truncated seed would otherwise read as complete.
				string warning =
					$"Parent-template walk stopped at the depth cap ({MaxParentDepth}); the seed may be truncated " +
					$"(next unwalked parent '{parentUId}').";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
				break;
			}
			if (!visitedParentUId.Add(parentUId)) {
				// Cycle on the parent-link walk: stop and say so — silently truncating hides a corrupt chain.
				string warning = $"Parent-template walk stopped on a cycle at '{parentUId}'; the seed may be truncated.";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
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
		PageSourcesRunContext ctx,
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

	private string InferEntity(PageSourcesRunContext ctx, JArray schemas, JArray seed) {
		// Prefer the page's own layer chain (most specific), then the parent-template seed.
		foreach (JToken entry in schemas.Concat(seed)) {
			string body = entry["body"]?.ToString();
			if (string.IsNullOrEmpty(body)) {
				continue;
			}
			Match match = SafeMatch(ctx.Warnings, EntityInferenceRegex, body, "inferring the bound entity");
			if (match.Success) {
				return match.Groups[1].Value;
			}
		}
		return null;
	}

	private JObject BuildResources(PageSourcesRunContext ctx, string topLayerUId, string schemaName) {
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
			string warning = $"Could not gather merged localizable strings (resources): {ex.Message}";
			_logger.WriteWarning(warning);
			AddWarning(ctx, warning);
		}
		return resources;
	}

	private (JObject entityColumns, JObject columnTitles) BuildEntityColumns(PageSourcesRunContext ctx, string entity) {
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
			string warning = $"Could not gather entity columns for '{entity}': {ex.Message}";
			_logger.WriteWarning(warning);
			AddWarning(ctx, warning);
		}
		return (entityColumns, columnTitles);
	}

	// Collects distinct detail-schema names referenced across every layer body (page chain + parent seed).
	// The names come from server-supplied bodies, so collection is capped by ATTEMPTS — not by later successes —
	// to keep a malformed or hostile response from driving unbounded probing.
	private List<string> CollectDetailNames(PageSourcesRunContext ctx, JArray schemas, JArray seed) {
		var detailNames = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int collectionCap = MaxDetails * 2;
		foreach (JToken entry in schemas.Concat(seed)) {
			string body = entry["body"]?.ToString();
			if (string.IsNullOrEmpty(body)) {
				continue;
			}
			foreach (Match match in SafeMatches(ctx.Warnings, DetailSchemaNameRegex, body, "collecting detail-schema references")) {
				string detailName = match.Groups[1].Value;
				if (!seen.Add(detailName)) {
					continue;
				}
				if (detailNames.Count >= collectionCap) {
					string warning =
						$"More than {collectionCap} distinct detail-schema references found; the remainder is ignored.";
					_logger.WriteWarning(warning);
					AddWarning(ctx, warning);
					return detailNames;
				}
				detailNames.Add(detailName);
			}
		}
		return detailNames;
	}

	private JObject BuildDetailSchemas(PageSourcesRunContext ctx, IReadOnlyList<string> detailNames) {
		var detailSchemas = new JObject();
		foreach (string detailName in detailNames) {
			if (detailSchemas.Count >= MaxDetails) {
				string warning = $"Detail gathering stopped at {MaxDetails} resolved schemas; the remainder is omitted.";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
				break;
			}
			try {
				(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, detailName);
				if (enumError != null) {
					string warning = $"Could not gather detail schema '{detailName}': {enumError}";
					_logger.WriteWarning(warning);
					AddWarning(ctx, warning);
					continue;
				}
				if (layers.Count == 0) {
					continue; // omit: an unresolved detail is left for the engine to flag, never fabricated
				}
				string topUId = layers[layers.Count - 1].UId;
				(JObject detailSchema, string loadError) = LoadSchemaCached(ctx, topUId, detailName);
				if (loadError != null || detailSchema == null) {
					string warning = $"Could not gather detail schema '{detailName}': {loadError ?? "no schema returned"}";
					_logger.WriteWarning(warning);
					AddWarning(ctx, warning);
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
				string warning = $"Could not gather detail schema '{detailName}': {ex.Message}";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
			}
		}
		return detailSchemas;
	}

	// Section candidates in priority order: the schema names SysModule metadata binds to the entity first, then the
	// name-derived conventions as a fallback. Metadata leads because no name derivation can reach a section whose
	// schema name carries a UId/app infix (entity ASPContractData -> section ASPContractDatac145c7efSection) or that
	// was renamed; the conventions still cover stands where the metadata lookup is unavailable or the module row is
	// missing. A metadata failure degrades to the conventions and is surfaced as a response warning, never fatal —
	// the section is an enricher, not the sources' payload.
	private IReadOnlyList<string> ResolveSectionCandidates(PageSourcesRunContext ctx, string schemaName, string entity) {
		var candidates = new List<string>();
		if (!string.IsNullOrWhiteSpace(entity)) {
			ClassicSectionLookup lookup = _sectionResolver.ResolveSectionSchemaNames(entity);
			if (lookup.Error != null) {
				_logger.WriteWarning($"Could not resolve the section from SysModule metadata: {lookup.Error}");
				ctx.Warnings.Add(
					$"Section metadata lookup failed ({lookup.Error}); fell back to name conventions, which cannot " +
					"reach a renamed section or one whose schema name carries a UId infix.");
			}
			candidates.AddRange(lookup.SectionSchemaNames);
		}
		candidates.AddRange(BuildSectionCandidates(schemaName, entity));
		return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private JArray BuildSection(PageSourcesRunContext ctx, IReadOnlyList<string> candidates) {
		var section = new JArray();
		foreach (string candidate in candidates) {
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

	// Section candidates, most-specific first: the <PagePrefix>Section[V2] variants (derived by stripping the
	// trailing Page/PageV2 suffix off the page schema name) take precedence over the bare <Entity>Section[V2]
	// variants, so a section cloned/renamed off the page (e.g. Applicant1Page -> Applicant1Section) is preferred
	// over the base-entity section when both exist. Deduped so the common case (page prefix == entity) does not
	// enumerate the same name twice.
	private static IReadOnlyList<string> BuildSectionCandidates(string schemaName, string entity) {
		var candidates = new List<string>();
		AddSectionPair(candidates, StripPageSuffix(schemaName));
		AddSectionPair(candidates, entity);
		return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static void AddSectionPair(List<string> candidates, string prefix) {
		if (string.IsNullOrWhiteSpace(prefix)) {
			return;
		}
		candidates.Add(prefix + "SectionV2");
		candidates.Add(prefix + "Section");
	}

	// The page prefix is the page schema name without its trailing Page/PageV2 suffix (Applicant1Page ->
	// Applicant1). Returns null when the name carries no such suffix, so no spurious "<name>Section" candidate is
	// derived from a non-page schema name.
	private static string StripPageSuffix(string schemaName) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			return null;
		}
		foreach (string suffix in new[] { "PageV2", "Page" }) {
			if (schemaName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && schemaName.Length > suffix.Length) {
				return schemaName.Substring(0, schemaName.Length - suffix.Length);
			}
		}
		return null;
	}

	private JObject BuildChildPageSchemas(PageSourcesRunContext ctx, JObject detailSchemas) {
		var childPageSchemas = new JObject();
		// Collect the distinct edit-page names first so the whole set is resolved in one batched enumeration.
		var editPageNames = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JProperty detail in detailSchemas.Properties()) {
			string detailBody = detail.Value["body"]?.ToString();
			if (string.IsNullOrEmpty(detailBody)) {
				continue;
			}
			Match editPageMatch = SafeMatch(ctx.Warnings, EditPageRegex, detailBody, "resolving the detail's edit page");
			if (!editPageMatch.Success) {
				continue; // no edit page named on the detail -> nothing to nest; the engine flags it
			}
			string editPageName = editPageMatch.Groups[1].Value;
			if (!seen.Add(editPageName)) {
				continue;
			}
			if (editPageNames.Count >= MaxChildPages) {
				string warning = $"More than {MaxChildPages} distinct child edit pages referenced; the remainder is omitted.";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
				break;
			}
			editPageNames.Add(editPageName);
		}
		PrimeLayerBatch(ctx, editPageNames);
		foreach (string editPageName in editPageNames) {
			try {
				(JObject childManifest, string error) = AssembleChildManifest(ctx, editPageName);
				if (error != null) {
					string warning = $"Could not assemble child page '{editPageName}': {error}";
					_logger.WriteWarning(warning);
					AddWarning(ctx, warning);
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
	private (JObject manifest, string error) AssembleChildManifest(PageSourcesRunContext ctx, string editPageName) {
		// Existence gate on the (batch-primed, cached) enumeration so a heuristic edit-page miss stays a cheap
		// no-op instead of a designer round-trip. Only after it resolves do we pay the hierarchy resolution.
		(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, editPageName);
		if (enumError != null) {
			return (null, enumError);
		}
		if (layers.Count == 0) {
			return (null, null);
		}
		// Resolve the child page's chain AND parent-template seed the SAME single-round-trip way the top-level
		// page does — one GetParentSchemas designer call — instead of the per-layer LoadLayerChain +
		// per-template-level BuildSeed fan-out this ran per child page (the dominant round-trip cost when a page
		// carries many child edit pages, each itself deeply layered). LoadChainAndSeed degrades to that exact
		// legacy fan-out on any hierarchy failure, so a child manifest is never worse than before.
		(JArray schemas, JArray seed, _, string chainError) = LoadChainAndSeed(ctx, editPageName);
		if (chainError != null) {
			return (null, chainError);
		}
		string entity = InferEntity(ctx, schemas, seed);
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
	//
	// An explicit output-file is NOT written verbatim: this tool is MCP-callable and its output path can be
	// agent-supplied (not typed by a human at a shell), and the tool is non-destructive so the MCP host does
	// not prompt on the write. A bare GetFullPath would resolve `..\..\system\file` into an arbitrary
	// overwrite. It is confined to the workspace anchor OR the OS temp directory — the two locations the
	// migration skill legitimately targets (in-workspace, or an OS-temp scratch dir) — and anything escaping
	// both is rejected before any write, returning an error rather than a path.
	private (string path, string error) ResolveOutputPath(GetClassicPageSourcesOptions options) {
		if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
			// Route the explicit output-file through the shared confinement guard (which takes the CwdLock
			// itself): it resolves symlinks, drops an untrusted anchor (filesystem root / ancestor of $HOME),
			// confines to the workspace or OS temp dir, and — rejectExistingTarget:true — refuses to overwrite an
			// existing file so the Destructive=false classification stays honest.
			return OutputPathConfinement.Resolve(_ioFileSystem, options.OutputFile);
		}
		// H1: reading the process-global cwd must serialize against the MCP workspace tools that PIN cwd.
		// In the MCP path this runs under the shared tool lock; in the single-threaded CLI path it is uncontended.
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			string anchor = PageOutputDirectoryResolver.ResolveAnchor(
				_ioFileSystem,
				_ioFileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				null);
			// The default manifest path is tool-owned and re-runnable — it overwrites its own prior output.
			return (Path.Combine(anchor, ClioMigrationDirectoryName, options.SchemaName, ManifestFileName), null);
		}
	}

	private static GetClassicPageSourcesResponse Fail(string error) =>
		new() { Success = false, Error = error };

	// Best-effort regex evaluation over server-supplied bodies. The compiled patterns carry a 1s match timeout;
	// a timeout on one pathological body must DEGRADE (skip that body, keep the rest of the collected sources) exactly like
	// every other enricher, never abort the whole assembly. Every Match/Matches call funnels through these two
	// guards so no regex pass can turn a would-be-successful collection into a hard failure.
	// internal, not private: the timeout branch is only reachable with an injected pattern/timeout, so the guards
	// take the warnings sink directly (all they need) and clio.tests exercises them head-on.
	internal Match SafeMatch(List<string> warnings, Regex regex, string body, string what) {
		try {
			return regex.Match(body);
		}
		catch (RegexMatchTimeoutException) {
			ReportRegexTimeout(warnings, what);
			return Match.Empty;
		}
	}

	internal IReadOnlyList<Match> SafeMatches(List<string> warnings, Regex regex, string body, string what) {
		try {
			// Materialize inside the try: Regex.Matches is lazily evaluated, so a timeout would otherwise surface
			// at the caller's enumeration site — outside this guard — rather than being caught here.
			return regex.Matches(body).ToList();
		}
		catch (RegexMatchTimeoutException) {
			ReportRegexTimeout(warnings, what);
			return [];
		}
	}

	// A skipped body lowers detailCount/sectionLayerCount/entity resolution with nothing else to show for it, so
	// the caller must be told: an MCP caller never sees the logger, and "extraction degraded" must not read as
	// "the page has nothing there". Deduped — one pathological page can trip the same guard on many bodies.
	private void ReportRegexTimeout(List<string> warnings, string what) {
		string warning =
			$"Pattern matching timed out while {what}; that schema body was skipped, so the collected sources may be " +
			"incomplete. A lower count here does NOT mean the page has nothing to migrate.";
		_logger.WriteWarning(warning);
		if (!warnings.Contains(warning, StringComparer.Ordinal)) {
			warnings.Add(warning);
		}
	}

	// Surface a completeness gap to the MCP caller. A logger warning alone does not reach an MCP caller (the log
	// buffer is cleared before the result is returned), so any branch that OMITS or TRUNCATES manifest content
	// while still returning success must also record it here — otherwise success:true / warnings:null reads as a
	// complete bundle. Deduped so a repeated gap does not flood the channel.
	private static void AddWarning(PageSourcesRunContext ctx, string warning) {
		if (!ctx.Warnings.Contains(warning, StringComparer.Ordinal)) {
			ctx.Warnings.Add(warning);
		}
	}
}
