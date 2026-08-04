namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
	Justification = "Command composes its required collaborators (application client, URL builder, column manager, hierarchy client, section resolver, detail edit-page resolver, file system, logger) via constructor injection; grouping them into a parameter object would hide which Classic-migration lookup each collects and gain nothing behaviorally.")]
public class GetClassicPageSourcesCommand : Command<GetClassicPageSourcesOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.ClientUnit;
	private const string EmptyGuid = ClassicEntitySchemaQuery.EmptyGuid;
	private const string ClioMigrationDirectoryName = ".clio-migration";
	private const string ManifestFileName = "manifest.json";
	private const string DefaultCulture = "en-US";
	// The manifest/detail-entry field naming the bound object. One name for the three places that write it (page
	// manifest, child-page manifest, annotated detail entry) so the engine's contract key is stated once.
	private const string EntityKey = "entity";
	// The detail-entry field naming the resolved child page (or `false` for a verified none). Single-sourced for the
	// same reason as EntityKey: the engine reads this exact key, so it is stated once rather than spelled at each write.
	private const string EditPageKey = "editPage";
	// No numeric fan-out caps (ENG-94402): a migration unit is collected WHOLE. A page with 250 details migrates
	// all 250, and a parent chain deeper than any hand-picked number is walked to its base template. Termination
	// does not rest on a cap — every unbounded walk is bounded by a visited-set over a finite input: the parent walk
	// by `visitedParentUId` (each UId is followed once), detail/child-page collection by the `seen` name set over
	// finitely many bodies. Numeric caps only ever truncated real units (ContactPageV2 sat at 48 of the old 50),
	// which is precisely the silent-incompleteness this command must not produce.
	// Stand-in reason when the designer answered without an error AND without a schema — an empty success the
	// walk/enricher paths must report as a gap rather than as a resolved-but-empty layer.
	private const string NoSchemaReturned = "no schema returned";

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
	//
	// SECONDARY route only (ENG-94401). This token belongs to the pre-V2 `*Detail` generation and its measured yield
	// on the product is ZERO: 0 of 845 page-detail pairs, 0 of 505 base pages, and — re-measured live on this stand —
	// 0 of the 24 details AccountPageV2 gathers. Child pages are resolved from `SysModuleEdit` metadata
	// (IClassicDetailEditPageResolver) instead; this pattern runs only for a detail the metadata answered nothing for,
	// so a hand-written custom detail that does carry the token still resolves. Do NOT restore it as the primary route.
	private static readonly Regex EditPageRegex = new(
		"(?:getEditPageName|editPageName|EditPageSchemaName)[\\s\\S]{0,80}?[\"']([A-Za-z][\\w]+)[\"']",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	// A detail reference in a page's `details` config that OVERRIDES the entity the detail binds by default, e.g.
	// `Files: { schemaName: "FileDetailV2", entitySchemaName: "AccountFile", ... }` — the page binds FileDetailV2 to
	// AccountFile, not to the entity the detail body declares. The override is what decides which entity's
	// SysModuleEdit rows apply, so it must be read from the PAGE body; the detail body alone cannot see it. Both key
	// orders are matched because the config is hand-written. The two keys must be ADJACENT: that is what keeps the
	// match inside one detail entry (an intervening `}` or `filter: {...}` breaks it), and a non-adjacent override is
	// simply not seen — the detail then falls back to its own declared entity, which degrades rather than mis-resolves.
	private static readonly Regex DetailEntityOverrideRegex = new(
		"(?<![A-Za-z_])schemaName[\"']?\\s*:\\s*[\"'](?<detail>[A-Za-z][\\w]*Detail[\\w]*)[\"']\\s*,\\s*" +
			"entitySchemaName[\"']?\\s*:\\s*[\"'](?<entity>[A-Za-z_][\\w]*)[\"']" +
		"|(?<![A-Za-z_])entitySchemaName[\"']?\\s*:\\s*[\"'](?<entity>[A-Za-z_][\\w]*)[\"']\\s*,\\s*" +
			"schemaName[\"']?\\s*:\\s*[\"'](?<detail>[A-Za-z][\\w]*Detail[\\w]*)[\"']",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly IPageDesignerHierarchyClient _hierarchyClient;
	private readonly IClassicSectionSchemaResolver _sectionResolver;
	private readonly IClassicDetailEditPageResolver _childPageResolver;
	private readonly IoFileSystem _ioFileSystem;
	private readonly ILogger _logger;

	/// <summary>
	/// Creates the command with the collaborators it needs to resolve a classic page hierarchy and write its sources.
	/// </summary>
	/// <param name="applicationClient">Client used to call the designer and OData services on the target environment.</param>
	/// <param name="serviceUrlBuilder">Builds the absolute service URLs for the target environment.</param>
	/// <param name="columnManager">Reads remote entity-schema columns for the resolved section entity.</param>
	/// <param name="hierarchyClient">Fetches the page-designer inheritance hierarchy for a schema.</param>
	/// <param name="sectionResolver">Resolves a section name to its classic page schema.</param>
	/// <param name="childPageResolver">
	/// Resolves the child pages a detail's entity registers in <c>SysModuleEdit</c>. Injected rather than built inline
	/// from the client this command already holds, so the metadata route is substitutable in tests and so the ESQ
	/// column set stays next to the other Classic-migration lookups (ENG-94401).
	/// </param>
	/// <param name="ioFileSystem">File-system abstraction used for every write this command performs.</param>
	/// <param name="logger">Logger for progress and error output.</param>
	/// <remarks>
	/// One file-system abstraction, not two: the confinement guard (<c>OutputPathConfinement</c>) and the cwd anchor
	/// resolution both take <see cref="IoFileSystem"/>, so routing the default-path write through it as well keeps
	/// every write this command performs on a single abstraction.
	/// </remarks>
	public GetClassicPageSourcesCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IRemoteEntitySchemaColumnManager columnManager,
		IPageDesignerHierarchyClient hierarchyClient,
		IClassicSectionSchemaResolver sectionResolver,
		IClassicDetailEditPageResolver childPageResolver,
		IoFileSystem ioFileSystem,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_columnManager = columnManager;
		_hierarchyClient = hierarchyClient;
		_sectionResolver = sectionResolver;
		_childPageResolver = childPageResolver;
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
			IReadOnlyDictionary<string, string> detailEntityOverrides = CollectDetailEntityOverrides(ctx, schemas, seed);
			IReadOnlyList<string> sectionCandidates = ResolveSectionCandidates(ctx, options.SchemaName, entity);
			var enricherNames = new List<string>(detailNames);
			enricherNames.AddRange(sectionCandidates);
			PrimeLayerBatch(ctx, enricherNames);
			JObject detailSchemas = BuildDetailSchemas(ctx, detailNames);
			JArray section = BuildSection(ctx, sectionCandidates);
			JObject childPageSchemas = BuildChildPageSchemas(
				ctx, detailSchemas, detailEntityOverrides, options.SchemaName,
				out IReadOnlyDictionary<string, DetailChildPageInfo> detailChildPageInfo);
			// The engine keys childPageSchemas by the detail's `editPage` FIRST, and an explicit editPage/entity on the
			// detail entry WINS over its own body scan — so without these annotations the nested manifests we just
			// resolved would be keyed by a page name the engine never looks up, and every detail would still read as
			// "child page NOT verified" (ENG-94401).
			AnnotateDetailSchemas(detailSchemas, detailChildPageInfo);
			if (section.Count == 0) {
				// sectionLayerCount:0 alone cannot be told apart from "this entity has no Classic section", and an
				// omitted section silently empties the plan's List-page analysis (custom quick filters,
				// getSectionActions, hardcoded list columns). Say so in the response — a logger warning would not
				// reach an MCP caller, whose log buffer is cleared before the result is returned.
				AddWarning(ctx,
					"No Classic section resolved for " +
					(string.IsNullOrWhiteSpace(entity) ? $"page '{options.SchemaName}'" : $"entity '{entity}'") +
					$" (tried: {string.Join(", ", sectionCandidates)}). The manifest carries no section, so the " +
					"List-page side of the migration plan will be empty. Verify whether a section exists before " +
					"treating this as 'nothing to migrate'.");
			}

			// 7. Assemble the manifest in the engine's contract shape (omit empty fields, never null-fill).
			var manifest = new JObject { ["schemas"] = schemas };
			if (!string.IsNullOrWhiteSpace(entity)) {
				manifest[EntityKey] = entity;
			}
			AddBlock(manifest, "seed", seed);
			AddBlock(manifest, "entityColumns", entityColumns);
			AddBlock(manifest, "columnTitles", columnTitles);
			AddBlock(manifest, "resources", resources);
			AddBlock(manifest, "detailSchemas", detailSchemas);
			AddBlock(manifest, "section", section);
			AddBlock(manifest, "childPageSchemas", childPageSchemas);

			// 8. Write the manifest to disk. The bodies live here, not in the response.
			(string manifestPath, string writeError) = WriteManifest(options, manifest);
			if (writeError != null) {
				response = Fail(writeError);
				return false;
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

	// Adds a manifest block only when it carries content: the engine's contract reads an absent field as "nothing
	// collected", while an empty object/array would read as a resolved-but-empty one.
	private static void AddBlock(JObject manifest, string name, JObject block) {
		if (block.HasValues) {
			manifest[name] = block;
		}
	}

	private static void AddBlock(JObject manifest, string name, JArray block) {
		if (block.Count > 0) {
			manifest[name] = block;
		}
	}

	// Resolves the output path and writes the manifest there, returning the absolute path written or the reason the
	// write was refused (a rejected path, or an explicit output-file that already exists).
	private (string path, string error) WriteManifest(GetClassicPageSourcesOptions options, JObject manifest) {
		(string manifestPath, string pathError) = ResolveOutputPath(options);
		if (pathError != null) {
			return (null, pathError);
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
				return (null, ex.Message);
			}
			return (manifestPath, null);
		}
		// Tool-owned default path: re-runnable, overwrites its own prior output.
		string directory = _ioFileSystem.Path.GetDirectoryName(manifestPath);
		if (!string.IsNullOrWhiteSpace(directory)) {
			_ioFileSystem.Directory.CreateDirectory(directory); // no-op when it already exists
		}
		_ioFileSystem.File.WriteAllText(manifestPath, manifestContent);
		return (manifestPath, null);
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

	// Names per batched SelectQuery. EnumerateSchemaLayersBatch puts every name into ONE `In` filter, so the
	// name count becomes the query's parameter count — and with the fan-out caps gone (ENG-94402) that count is
	// driven by the page, not by a constant. Chunking keeps each query far below the DBMS parameter ceiling
	// (MSSql refuses a parameterized statement past 2100) and below request-size limits, so a very wide page
	// still resolves in a few batched queries instead of failing the batch and degrading to N per-name lookups.
	// The bound itself is the shared `In`-list ceiling: every batched value set in this flow — here and in the
	// child-page resolver — is the same kind of parameter list against the same DBMS, so one constant states it.
	private const int LayerBatchChunkSize = ClassicEntitySchemaQuery.InFilterChunkSize;

	// Resolves many names in batched SelectQueries and seeds the enumeration cache — including empty entries for
	// names that do not exist, so later per-name lookups don't re-query them. A batch failure only logs (no
	// ctx.Warnings): every consumer falls back to the memoized per-name path, which loses no manifest content,
	// so this is a slow path rather than an incompleteness gap.
	private void PrimeLayerBatch(PageSourcesRunContext ctx, IReadOnlyCollection<string> schemaNames) {
		List<string> missing = schemaNames
			.Where(name => !string.IsNullOrWhiteSpace(name) && !ctx.LayersByName.ContainsKey(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (missing.Count == 0) {
			return;
		}
		for (int offset = 0; offset < missing.Count; offset += LayerBatchChunkSize) {
			List<string> chunk = missing.GetRange(offset, Math.Min(LayerBatchChunkSize, missing.Count - offset));
			// One failing chunk must not abandon the rest: each chunk the batch resolves still spares its names a
			// per-name round-trip, and the names of a failed chunk simply stay unmemoized for the fallback path.
			PrimeLayerChunk(ctx, chunk);
		}
	}

	private void PrimeLayerChunk(PageSourcesRunContext ctx, IReadOnlyCollection<string> chunk) {
		try {
			(IReadOnlyDictionary<string, IReadOnlyList<SchemaLayer>> layersByName, string error) =
				SchemaDesignerHelper.EnumerateSchemaLayersBatch(_applicationClient, _serviceUrlBuilder, chunk, Kind);
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
	// to match the engine's merge order. Returns an EMPTY list when the schema cannot be resolved (caller falls back).
	private IReadOnlyList<PageDesignerHierarchySchema> ResolveHierarchyBaseToTop(string schemaName) {
		(JToken metadata, _) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			_applicationClient, _serviceUrlBuilder, schemaName,
			("UId", "UId"), ("PackageUId", "SysPackage.UId"));
		string schemaUId = metadata?["UId"]?.ToString();
		string packageUId = metadata?["PackageUId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
			return [];
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
			return [];
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
		while (true) {
			string parentUId = (current?["parent"] as JObject)?["uId"]?.ToString();
			if (string.IsNullOrWhiteSpace(parentUId) || string.Equals(parentUId, EmptyGuid, StringComparison.OrdinalIgnoreCase)) {
				break; // reached the base template — a clean, complete walk
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
				// Best-effort: stop the walk and keep what we have — but say so through BOTH channels. The logger
				// alone does not reach an MCP caller (its buffer is cleared before the result is returned), so a
				// seed truncated here would read as a page that simply has no more parents (parity with the
				// cycle exit above).
				string warning =
					$"Parent-template walk stopped at '{parentUId}' ({error ?? NoSchemaReturned}); the seed is " +
					"truncated and the base containers defined above this point are missing from the manifest.";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
				break;
			}
			levels.Add(LoadParentLevelLayers(ctx, parentUId, parentLayer, seededTemplateNames, seededLayerUIds));
			current = parentLayer; // continue up from the linked layer's own parent
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
			return LinkedLayerOnly(parentUId, parentLayer, seededLayerUIds);
		}
		(IReadOnlyList<SchemaLayer> layers, string enumError) = EnumerateLayersCached(ctx, parentName);
		if (enumError != null) {
			// The level degrades to the linked layer alone, so any sibling layer of this template is dropped from
			// the seed — the engine then reports the containers they define as unresolvedParents. Caller-visible,
			// not logger-only: an omitted seed layer is exactly as invisible as a truncated one.
			string warning =
				$"Could not enumerate parent template '{parentName}' layers ({enumError}); only the linked layer " +
				"is seeded, so containers defined in a sibling layer of that template are missing from the manifest.";
			_logger.WriteWarning(warning);
			AddWarning(ctx, warning);
		}
		if (enumError != null || layers.Count == 0) {
			return LinkedLayerOnly(parentUId, parentLayer, seededLayerUIds);
		}
		List<JObject> levelEntries =
			LoadEnumeratedLevelLayers(ctx, layers, parentName, parentUId, parentLayer, seededLayerUIds);
		if (levelEntries.Count == 0) {
			// Every enumerated sibling failed to load (the linked layer itself was not among the rows):
			// never drop the level entirely — seed at least the layer the walk already holds.
			return LinkedLayerOnly(parentUId, parentLayer, seededLayerUIds);
		}
		return levelEntries;
	}

	// The fallback for a level whose siblings cannot be enumerated or loaded: seed just the layer the walk already
	// holds — unless that layer was seeded at an earlier level, in which case the level contributes nothing.
	private static List<JObject> LinkedLayerOnly(
		string parentUId, JObject parentLayer, HashSet<string> seededLayerUIds) =>
		seededLayerUIds.Add(parentUId)
			? [CreateSeedEntry(parentLayer, parentLayer["package"]?["name"]?.ToString())]
			: [];

	// Loads one parent-template level's enumerated layers into seed entries, base->top. A layer already seeded
	// earlier in the walk is skipped (no duplicate body), the linked layer reuses the schema the walk already
	// loaded, and a layer whose body fails to load is skipped with a warning rather than failing the level.
	private List<JObject> LoadEnumeratedLevelLayers(
		PageSourcesRunContext ctx,
		IReadOnlyList<SchemaLayer> layers,
		string parentName,
		string parentUId,
		JObject parentLayer,
		HashSet<string> seededLayerUIds) {
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
				// One enumerated sibling body is dropped from the seed while the level still succeeds — a partial
				// level that the response would otherwise report as a complete one.
				string warning =
					$"Could not load parent-template layer '{parentName}' ({layer.UId}): {loadError ?? NoSchemaReturned}. " +
					"That layer's body is missing from the seed.";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
				continue;
			}
			levelEntries.Add(CreateSeedEntry(layerSchema, layer.PackageName));
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
			string entity = InferEntityFromBody(ctx, entry["body"]?.ToString());
			if (entity != null) {
				return entity;
			}
		}
		return null;
	}

	// The entity a single schema body declares as its own bound object, or null when it declares none. Shared by the
	// page-chain inference above and the per-detail entity resolution the child-page lookup needs.
	private string InferEntityFromBody(PageSourcesRunContext ctx, string body) {
		if (string.IsNullOrEmpty(body)) {
			return null;
		}
		Match match = SafeMatch(ctx.Warnings, EntityInferenceRegex, body, "inferring the bound entity");
		return match.Success ? match.Groups[1].Value : null;
	}

	// The per-detail entity OVERRIDES the page's `details` config declares (detail schema name -> entity schema name).
	// Read from the page bodies, not the detail bodies: a page can bind a shared detail to a different entity than the
	// detail's own default (`Files: { schemaName: "FileDetailV2", entitySchemaName: "AccountFile" }`), and that
	// override — not the detail's default — decides whose SysModuleEdit rows apply.
	//
	// Deliberately a second pass rather than an extra output of CollectDetailNames: that collector is attempt-capped
	// against hostile bodies and its cap semantics are asserted by tests, so overrides are gathered beside it instead
	// of reshaping it. Precedence within the pass: the page's own layer chain wins over the parent-template seed
	// (most specific first), and within either, a later (more derived) layer overrides an earlier one.
	private IReadOnlyDictionary<string, string> CollectDetailEntityOverrides(
		PageSourcesRunContext ctx, JArray schemas, JArray seed) {
		var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		CollectDetailEntityOverrides(ctx, seed, overrides);    // least specific first...
		CollectDetailEntityOverrides(ctx, schemas, overrides); // ...so the page's own layers overwrite the seed
		return overrides;
	}

	private void CollectDetailEntityOverrides(
		PageSourcesRunContext ctx, JArray layers, Dictionary<string, string> overrides) {
		foreach (JToken entry in layers) {
			string body = entry["body"]?.ToString();
			if (string.IsNullOrEmpty(body)) {
				continue;
			}
			foreach (GroupCollection groups in SafeMatches(
					ctx.Warnings, DetailEntityOverrideRegex, body, "reading the details' entity overrides")
				.Select(match => match.Groups)) {
				string detailName = groups["detail"].Value;
				string entityName = groups["entity"].Value;
				if (!string.IsNullOrWhiteSpace(detailName) && !string.IsNullOrWhiteSpace(entityName)) {
					overrides[detailName] = entityName;
				}
			}
		}
	}

	private JObject BuildResources(PageSourcesRunContext ctx, string topLayerUId, string schemaName) {
		var resources = new JObject();
		try {
			(JObject schema, string error) = LoadSchemaCached(ctx, topLayerUId, schemaName, useFullHierarchy: true);
			if (error != null || schema == null) {
				// resourceCount:0 cannot be told apart from a page that declares no localizable strings, and the
				// engine then folds captions it has no translation for. Same channel as the catch below.
				string warning = $"Could not gather merged localizable strings (resources): {error ?? NoSchemaReturned}. " +
					"The manifest carries no resources, so localized captions will be missing from the folded page.";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
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

	// Collects EVERY distinct detail-schema name referenced across every layer body (page chain + parent seed).
	// Uncapped by design (ENG-94402): a page that references 250 details must migrate all 250. Collection is
	// bounded by the input rather than by a number — the `seen` set admits each name once, over finitely many
	// bodies, so a malformed or repetitive response cannot drive an unbounded walk.
	private List<string> CollectDetailNames(PageSourcesRunContext ctx, JArray schemas, JArray seed) {
		var detailNames = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JToken entry in schemas.Concat(seed)) {
			string body = entry["body"]?.ToString();
			if (string.IsNullOrEmpty(body)) {
				continue;
			}
			foreach (Match match in SafeMatches(ctx.Warnings, DetailSchemaNameRegex, body, "collecting detail-schema references")) {
				string detailName = match.Groups[1].Value;
				if (seen.Add(detailName)) {
					detailNames.Add(detailName);
				}
			}
		}
		return detailNames;
	}

	private JObject BuildDetailSchemas(PageSourcesRunContext ctx, IReadOnlyList<string> detailNames) {
		var detailSchemas = new JObject();
		// Every collected detail is resolved — no cap. Layer enumeration is batch-primed, but the body load is one
		// designer round-trip per detail, so a very wide page is proportionally slower. That is the accepted trade:
		// a slow complete unit beats a fast one that silently omits part of the page.
		foreach (string detailName in detailNames) {
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
					string warning = $"Could not gather detail schema '{detailName}': {loadError ?? NoSchemaReturned}";
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
				AddWarning(ctx,
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

	// What the child-page lookup learned about one detail, in the shape the engine's detailSchemas entry reads.
	// EditPage: the primary edit card's schema name; null when nothing was resolved. VerifiedNoEditPage: the metadata
	// answered DEFINITIVELY that the detail's entity registers no edit card — the difference between "we checked and
	// there is none" (which lets the engine's plan proceed) and "we never checked" (which must not read as none).
	private sealed record DetailChildPageInfo(string Entity, string EditPage, bool VerifiedNoEditPage);

	private JObject BuildChildPageSchemas(
		PageSourcesRunContext ctx,
		JObject detailSchemas,
		IReadOnlyDictionary<string, string> detailEntityOverrides,
		string pageSchemaName,
		out IReadOnlyDictionary<string, DetailChildPageInfo> detailChildPageInfo) {
		var childPageSchemas = new JObject();
		// Collect the distinct edit-page names first so the whole set is resolved in one batched enumeration.
		IReadOnlyList<string> editPageNames = CollectChildPageNames(
			ctx, detailSchemas, detailEntityOverrides, pageSchemaName, out detailChildPageInfo);
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
				// Same channel as the error branch above: a dropped child page leaves childPageCount lower than
				// the page's real fan-out with nothing in the response to say why.
				string warning = $"Could not assemble child page '{editPageName}': {ex.Message}";
				_logger.WriteWarning(warning);
				AddWarning(ctx, warning);
			}
		}
		return childPageSchemas;
	}

	// Every distinct child-page name for the gathered details. Uncapped (ENG-94402): the migration unit is collected
	// whole, and the `seen` set admits each name once so a repeated reference is never resolved twice.
	//
	// PRIMARY route: each detail's entity -> its SysModuleEdit registrations (edit card + add mini page), resolved for
	// every detail entity in ONE batched metadata lookup. This replaces scanning the detail body for a
	// getEditPageName/editPageName/EditPageSchemaName token, whose measured yield on the product is zero — see the
	// EditPageRegex comment and ENG-94401. The body scan stays as the SECONDARY route for a detail the metadata
	// answered nothing for, so a custom detail that does carry the token still resolves.
	//
	// One entity can register several pages (one row per TypeColumnValue, plus a mini page), so the child-page count
	// is not bounded by the detail count either — which is exactly why removing the numeric caps matters here.
	private List<string> CollectChildPageNames(
		PageSourcesRunContext ctx,
		JObject detailSchemas,
		IReadOnlyDictionary<string, string> detailEntityOverrides,
		string pageSchemaName,
		out IReadOnlyDictionary<string, DetailChildPageInfo> detailChildPageInfo) {
		IReadOnlyDictionary<string, string> entityByDetail =
			ResolveDetailEntities(ctx, detailSchemas, detailEntityOverrides);
		(IReadOnlyDictionary<string, IReadOnlyList<ClassicChildPage>> pagesByEntity, ISet<string> resolvedEntities) =
			ResolveChildPagesByEntity(ctx, entityByDetail);
		var info = new Dictionary<string, DetailChildPageInfo>(StringComparer.OrdinalIgnoreCase);
		detailChildPageInfo = info;
		var editPageNames = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// Local so both routes share the dedup and the self-reference guard.
		void Add(string editPageName) {
			if (string.IsNullOrWhiteSpace(editPageName)) {
				return;
			}
			// A detail bound to the page's OWN entity resolves back to the page itself; nesting a page inside its own
			// manifest is never what the engine folds.
			if (string.Equals(editPageName, pageSchemaName, StringComparison.OrdinalIgnoreCase)) {
				return;
			}
			if (seen.Add(editPageName)) {
				editPageNames.Add(editPageName);
			}
		}
		foreach (JProperty detail in detailSchemas.Properties()) {
			entityByDetail.TryGetValue(detail.Name, out string entity);
			// Metadata first; the body scan runs only for a detail the metadata answered nothing for.
			if (!TryCollectFromMetadata(detail.Name, entity, pageSchemaName, pagesByEntity, info, Add)) {
				CollectFromDetailBody(ctx, detail, entity, resolvedEntities, info, Add);
			}
		}
		return editPageNames;
	}

	// PRIMARY route for one detail: the pages its entity registers in SysModuleEdit. Returns false when metadata has
	// no answer for this detail (unresolved entity, or no registrations), which is the caller's cue to fall back to the
	// body scan; a true return means metadata was definite and the body scan would add nothing.
	private static bool TryCollectFromMetadata(
		string detailName,
		string entity,
		string pageSchemaName,
		IReadOnlyDictionary<string, IReadOnlyList<ClassicChildPage>> pagesByEntity,
		IDictionary<string, DetailChildPageInfo> info,
		Action<string> add) {
		if (entity == null
			|| !pagesByEntity.TryGetValue(entity, out IReadOnlyList<ClassicChildPage> metadataPages)
			|| metadataPages.Count == 0) {
			return false;
		}
		foreach (ClassicChildPage metadataPage in metadataPages) {
			add(metadataPage.SchemaName);
		}
		// The engine reads ONE editPage per detail: the edit card, not the add mini page (which it keys
		// separately). The mini pages stay in childPageSchemas so the plan still carries their sources.
		string card = PickPrimaryCard(metadataPages, entity, pageSchemaName);
		info[detailName] = new DetailChildPageInfo(entity, card, VerifiedNoEditPage: card == null);
		return true;
	}

	// SECONDARY route for one detail: the getEditPageName/editPageName token in its own body. `verified none` is
	// claimable ONLY when the metadata answered for THIS detail's entity — i.e. the entity is in the resolver's
	// resolved set. A batch-wide "the lookup ran" is not enough: the resolver warns per entity and leaves the ones it
	// could not resolve out, and for those we never looked, so saying "none" would license the engine to plan around a
	// child page that does exist.
	private void CollectFromDetailBody(
		PageSourcesRunContext ctx,
		JProperty detail,
		string entity,
		ISet<string> resolvedEntities,
		IDictionary<string, DetailChildPageInfo> info,
		Action<string> add) {
		bool verifiedNone = entity != null && resolvedEntities.Contains(entity);
		string detailBody = detail.Value["body"]?.ToString();
		Match editPageMatch = string.IsNullOrEmpty(detailBody)
			? Match.Empty
			: SafeMatch(ctx.Warnings, EditPageRegex, detailBody, "resolving the detail's edit page");
		if (!editPageMatch.Success) {
			// Neither metadata nor body names a page. Record what we know so the engine can tell a verified
			// "no edit page exists" apart from an unchecked detail.
			info[detail.Name] = new DetailChildPageInfo(entity, null, verifiedNone);
			return;
		}
		string bodyPage = editPageMatch.Groups[1].Value;
		info[detail.Name] = new DetailChildPageInfo(entity, bodyPage, VerifiedNoEditPage: false);
		add(bodyPage);
	}

	// Which of an entity's registered cards to name as THE detail's editPage. Every candidate here came from metadata,
	// and every candidate this method may return is also nested in childPageSchemas — so it ranks for the engine's
	// single per-detail slot without ever inventing a page or naming one the manifest does not carry.
	//
	// The self-referential card is excluded for exactly that reason: a detail bound to the page's OWN entity resolves
	// back to the page being assembled, which the nesting guard drops, so naming it as editPage would point the
	// engine's [editPage, entity, entity + "Page"] lookup at a page that is not in the manifest — the detail would then
	// read as "child page NOT verified", the very gate failure this annotation exists to clear. With no card left, the
	// caller records a verified "no edit page" instead (the metadata did answer for this entity).
	//
	// TypeColumnValue cannot rank the rest: an entity's cards are either all typed (Activity registers
	// EmailPageV2 + ActivityPageV2 + ActivityPageV2 under three different type values, with no default row) or all
	// untyped (Order registers PortalOrderPage and OrderPageV2, both with an empty one). So prefer the card whose name
	// is the entity's own conventional page name — `<entity>PageV2`, then `<entity>Page` — which picks ActivityPageV2
	// over EmailPageV2, OrderPageV2 over PortalOrderPage, and CasePage over PortalCasePage. An exact match outranks a
	// mere prefix match so a second entity-prefixed card (e.g. ContactPageV2 vs ContactPageV2Detail) cannot win by
	// registration order alone. Fall back to a prefix match, then to registration order, when the entity registers no
	// conventionally-named card (e.g. VwAccountRelationship -> AccountRelationshipDetailPageV2).
	private static string PickPrimaryCard(
		IReadOnlyList<ClassicChildPage> registered, string entity, string pageSchemaName) {
		List<ClassicChildPage> cards = registered
			.Where(page => !page.IsMiniPage
				&& !string.Equals(page.SchemaName, pageSchemaName, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (cards.Count == 0) {
			return null;
		}
		if (string.IsNullOrWhiteSpace(entity)) {
			return cards[0].SchemaName;
		}
		ClassicChildPage conventional =
			FindCard(cards, entity + "PageV2")
			?? FindCard(cards, entity + "Page")
			?? cards.FirstOrDefault(page => page.SchemaName.StartsWith(entity, StringComparison.OrdinalIgnoreCase));
		return (conventional ?? cards[0]).SchemaName;
	}

	private static ClassicChildPage FindCard(IEnumerable<ClassicChildPage> cards, string schemaName) =>
		cards.FirstOrDefault(page => string.Equals(page.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase));

	// Writes what the child-page lookup learned onto the detail entries themselves. The engine resolves a detail's
	// child page by `[detail.editPage, detail.entity, detail.entity + "Page"]` and an explicit value on the entry WINS
	// over its own body scan — so a `*PageV2` card (i.e. most of the product) is reachable ONLY through the explicit
	// `editPage`. `editPage: false` is the engine's "verified: no Classic edit page exists" signal; it is written only
	// where the metadata actually established that, never as a stand-in for an unchecked detail.
	private static void AnnotateDetailSchemas(
		JObject detailSchemas, IReadOnlyDictionary<string, DetailChildPageInfo> detailChildPageInfo) {
		foreach (JProperty detail in detailSchemas.Properties()) {
			if (!detailChildPageInfo.TryGetValue(detail.Name, out DetailChildPageInfo info)
				|| detail.Value is not JObject entry) {
				continue;
			}
			if (!string.IsNullOrWhiteSpace(info.Entity)) {
				entry[EntityKey] = info.Entity;
			}
			if (!string.IsNullOrWhiteSpace(info.EditPage)) {
				entry[EditPageKey] = info.EditPage;
			}
			else if (info.VerifiedNoEditPage) {
				entry[EditPageKey] = false;
			}
		}
	}

	// The entity each gathered detail binds: the page's `details`-config override first (it outranks the detail's own
	// default), else the entity the detail body itself declares. A detail whose entity cannot be resolved is reported
	// as a gap — its child pages are simply not lookup-able, which must not read as "it has none".
	private IReadOnlyDictionary<string, string> ResolveDetailEntities(
		PageSourcesRunContext ctx, JObject detailSchemas, IReadOnlyDictionary<string, string> detailEntityOverrides) {
		var entityByDetail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var unresolved = new List<string>();
		foreach (JProperty detail in detailSchemas.Properties()) {
			string entity = detailEntityOverrides.TryGetValue(detail.Name, out string overridden)
				? overridden
				: InferEntityFromBody(ctx, detail.Value["body"]?.ToString());
			if (string.IsNullOrWhiteSpace(entity)) {
				unresolved.Add(detail.Name);
			}
			else {
				entityByDetail[detail.Name] = entity;
			}
		}
		if (unresolved.Count > 0) {
			bool single = unresolved.Count == 1;
			AddWarning(ctx,
				$"Could not determine the bound entity for {(single ? "detail" : "details")} " +
				$"{string.Join(", ", unresolved)}, so {(single ? "its" : "their")} child pages were not looked up in " +
				$"SysModuleEdit. That is NOT the same as '{(single ? "it has" : "they have")} no child page'.");
		}
		return entityByDetail;
	}

	// Entity -> the child pages it registers, from ONE batched SysModuleEdit lookup over every detail entity, plus the
	// set of entities the lookup actually RESOLVED: only an entity the metadata answered for lets a caller claim
	// "verified: this detail has no edit page". Note the two are independent — an entity can resolve fine and still
	// register no page (a legitimate verified none), while an entity the resolver warned about is absent from the set
	// even though the lookup as a whole succeeded. A lookup failure degrades to the body-scan route for every detail
	// and is surfaced as a response warning, never fatal — child pages are an enricher, not the sources' payload.
	private (IReadOnlyDictionary<string, IReadOnlyList<ClassicChildPage>> pagesByEntity, ISet<string> resolvedEntities)
		ResolveChildPagesByEntity(PageSourcesRunContext ctx, IReadOnlyDictionary<string, string> entityByDetail) {
		var pagesByEntity = new Dictionary<string, IReadOnlyList<ClassicChildPage>>(StringComparer.OrdinalIgnoreCase);
		var noneResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> entities = entityByDetail.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (entities.Count == 0) {
			return (pagesByEntity, noneResolved);
		}
		ClassicChildPageLookup lookup;
		try {
			lookup = _childPageResolver.ResolveChildPages(entities);
		}
		catch (Exception ex) {
			// The resolver contract is not to throw, but a substituted or faulty implementation must not fail the whole
			// assembly: degrade to the body-scan route exactly like a reported error does.
			ReportChildPageLookupFailure(ctx, ex.Message);
			return (pagesByEntity, noneResolved);
		}
		if (lookup.Error != null) {
			ReportChildPageLookupFailure(ctx, lookup.Error);
			return (pagesByEntity, noneResolved);
		}
		foreach (string warning in lookup.Warnings ?? []) {
			_logger.WriteWarning(warning);
			AddWarning(ctx, warning);
		}
		foreach (IGrouping<string, ClassicChildPage> group in (lookup.ChildPages ?? [])
			.GroupBy(page => page.EntityName, StringComparer.OrdinalIgnoreCase)) {
			pagesByEntity[group.Key] = group
				.Where(page => !string.IsNullOrWhiteSpace(page.SchemaName))
				.GroupBy(page => page.SchemaName, StringComparer.OrdinalIgnoreCase)
				.Select(byName => byName.First())
				.ToList();
		}
		return (pagesByEntity, new HashSet<string>(lookup.ResolvedEntities ?? [], StringComparer.OrdinalIgnoreCase));
	}

	private void ReportChildPageLookupFailure(PageSourcesRunContext ctx, string error) {
		string warning =
			$"Child-page lookup from SysModuleEdit failed ({error}); fell back to scanning the detail bodies for an " +
			"edit-page token, which resolves almost nothing on a stock product (measured 0 of 845 page-detail pairs). " +
			"An empty childPageSchemas here does NOT mean the details have no child pages.";
		_logger.WriteWarning(warning);
		AddWarning(ctx, warning);
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
			manifest[EntityKey] = entity;
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

	// Best-effort regex evaluation over server-supplied bodies. The compiled patterns carry a 1s match timeout, and
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
