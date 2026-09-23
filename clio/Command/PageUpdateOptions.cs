namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics.CodeAnalysis;
	using System.IO;
	using System.Linq;
	using Clio.Command.McpServer;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	/// <summary>
	/// Options for the <c>update-page</c> command.
	/// </summary>
	[Verb("update-page", Aliases = ["page-update"], HelpText = "Update Freedom UI page schema body")]
	public class PageUpdateOptions : EnvironmentOptions
	{
		/// <summary>
		/// Gets or sets the page schema name to update.
		/// </summary>
		[Option("schema-name", Required = true, HelpText = "Page schema name")]
		public string SchemaName { get; set; }

		/// <summary>
		/// Gets or sets the full raw JavaScript body to save.
		/// </summary>
		[Option("body", Required = false, HelpText = "New JSON body content (inline)")]
		public string Body { get; set; }

		/// <summary>
		/// Gets or sets path to a file containing the new body. Alternative to --body.
		/// </summary>
		[Option("body-file", Required = false, HelpText = "Path to a file containing the new body. Alternative to --body.")]
		public string? BodyFile { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the command should validate without saving.
		/// </summary>
		[Option("dry-run", Required = false, HelpText = "Validate only, don't save")]
		public bool DryRun { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the MCP page-body validation chain should run.
		/// </summary>
		/// <remarks>
		/// The MCP adapter owns this escape hatch; the CLI command does not expose it as an option.
		/// JavaScript syntax and AST loadability checks remain mandatory when validation is disabled.
		/// </remarks>
		public bool Validate { get; set; } = true;

		/// <summary>
		/// Gets or sets the explicit resource captions used for <c>#ResourceString(key)#</c> macros.
		/// </summary>
		[Option("resources", Required = false, HelpText = "JSON object of resource key-value pairs for #ResourceString(key)# macros")]
		public string Resources { get; set; }

		/// <summary>
		/// Gets or sets the optional properties to merge into the schema, as a JSON array of {key, value} objects.
		/// </summary>
		[Option("optional-properties", Required = false, HelpText = "JSON array of {key, value} objects to merge into schema optionalProperties")]
		public string? OptionalProperties { get; set; }

		/// <summary>
		/// Gets or sets the write mode. <c>replace</c> (default) saves the provided body verbatim.
		/// <c>append</c> merges the provided body fragment with the current schema body on the server,
		/// including converters and validators by type key with incoming entries winning.
		/// </summary>
		[Option("mode", Required = false, HelpText = "Write mode: 'replace' (default) or 'append' (merge diffs, handlers, converters, and validators with the existing body)")]
		public string? Mode { get; set; }

		/// <summary>
		/// Optional explicit design package UId override. When set, bypasses
		/// <c>GetDesignPackageUId</c> and saves the replacing schema into the specified package.
		/// Use this when multiple apps replace the same platform page and the automatic resolution
		/// picks the wrong app.
		/// </summary>
		[Option("target-package-uid", Required = false, HelpText = "Explicit target package UId for the replacing schema (overrides automatic design-package resolution)")]
		public string? TargetPackageUId { get; set; }

		/// <summary>
		/// Optional explicit schema UId to save into directly. When set, bypasses hierarchy resolution
		/// entirely — <c>update-page</c> loads the schema by this UId, applies the body (or appends to
		/// it when <c>mode</c> is <c>append</c>), and saves. Use this when the page name is replaced by
		/// multiple apps and you already know the exact schema UId (for example, obtained via
		/// <c>list-pages</c>) of the replacement you want to modify.
		/// </summary>
		[Option("target-schema-uid", Required = false, HelpText = "Explicit schema UId to save into (bypasses hierarchy resolution)")]
		public string? TargetSchemaUId { get; set; }

		/// <summary>
		/// Gets or sets the baseline <c>SysSchema.Checksum</c> of the editable schema. When set,
		/// the save is blocked with a structured conflict when the server-side checksum differs —
		/// i.e. the schema was modified outside the current session.
		/// </summary>
		[Option("expected-checksum", Required = false, HelpText = "Baseline SysSchema checksum of the editable schema; blocks the save with a conflict when the server value differs")]
		public string? ExpectedChecksum { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the external-modification check is skipped,
		/// deliberately overwriting any out-of-band changes.
		/// </summary>
		[Option("force", Required = false, HelpText = "Skip the external-modification check and deliberately overwrite")]
		public bool Force { get; set; }

		/// <summary>
		/// Gets or sets the editable schema UId recorded in the baseline. MCP-internal: populated
		/// from <c>.clio-pages/{schema}/meta.json</c> by the MCP layer; not exposed as a CLI option
		/// because it only makes sense together with the on-disk baseline.
		/// </summary>
		public string? ExpectedSchemaUId { get; set; }

		/// <summary>
		/// Gets or sets a value indicating that the baseline recorded NO editable schema (a write
		/// was expected to create a new replacing schema). MCP-internal — see
		/// <see cref="ExpectedSchemaUId"/>.
		/// </summary>
		public bool ExpectedSchemaAbsent { get; set; }

		/// <summary>
		/// Gets or sets the editable schema UId the on-disk baseline was captured for, when a
		/// <c>target-package-uid</c> / <c>target-schema-uid</c> selector is present. MCP-internal.
		/// </summary>
		/// <remarks>
		/// A selector is not by itself a redirect: naming the package that ALREADY owns the schema
		/// resolves to exactly the schema the baseline describes, and dropping the baseline there let a
		/// stale body overwrite a concurrent writer's save with <c>success: true</c>. The baseline
		/// therefore travels as a CONDITIONAL one — it is promoted into
		/// <see cref="ExpectedChecksum"/>/<see cref="ExpectedSchemaUId"/>/<see cref="ExpectedSchemaAbsent"/>
		/// only once the target is resolved and turns out to be this very schema, and is otherwise
		/// discarded exactly as before. Resolution happens after the guard runs, which is why the
		/// decision cannot be taken inside it.
		/// </remarks>
		internal string? ConditionalBaselineSchemaUId { get; set; }

		/// <summary>Gets or sets the conditional baseline's checksum. See <see cref="ConditionalBaselineSchemaUId"/>.</summary>
		internal string? ConditionalBaselineChecksum { get; set; }

		/// <summary>Gets or sets the conditional baseline's "no editable schema existed" marker. See <see cref="ConditionalBaselineSchemaUId"/>.</summary>
		internal bool ConditionalBaselineSchemaAbsent { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the resolved target turned out to be the very schema the
		/// conditional baseline describes. The save must then refresh the on-disk baseline like any other
		/// armed save, or the next unpinned save auto-arms from a superseded checksum.
		/// Set whether or not the baseline was also promoted to govern the conflict check: a caller-pinned
		/// checksum keeps that role, but the target still matched, so the refresh is still due.
		/// </summary>
		internal bool ConditionalBaselineApplied { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the successful save path should attempt a
		/// best-effort Designer Presence push. Internal orchestration flag; not exposed as a CLI
		/// option and enabled only by the dedicated <c>update-page</c> entry points.
		/// </summary>
		internal bool NotifyDesignerPresence { get; set; }

		// The persisted-resource-key read used to be memoized on THIS type (PersistedResourceKeysRead /
		// Snapshot / Failure). It is not any more: a cache keyed on options-INSTANCE identity cannot serve
		// sync-pages, which builds a fresh options object per page and runs its first validation gate
		// before any options exist. IPersistedResourceKeyReader owns the read and keys it by
		// (environment, schema) instead (issue #1464).
	}

	/// <summary>
	/// Validates and saves raw Freedom UI page bodies.
	/// </summary>
	public class PageUpdateCommand : Command<PageUpdateOptions>
	{
		private const string LocalizableStringsKey = "localizableStrings";
		private const string ChecksumColumnName = "Checksum";
		private const string ModifiedOnColumnName = "ModifiedOn";
		private const string AppendMode = "append";

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;
		private readonly IPageDesignerHierarchyClient _hierarchyClient;
		private readonly Func<IJsonDiffApplier> _viewConfigApplierFactory;
		private readonly IPageDesignerPresenceNotifier? _pageDesignerPresenceNotifier;
		private readonly IPageBaselineGuard _pageBaselineGuard;
		private readonly IPersistedResourceKeyReader _persistedResourceKeyReader;

		/// <summary>
		/// Initializes a new instance of the <see cref="PageUpdateCommand"/> class.
		/// </summary>
		/// <param name="applicationClient">Remote Creatio client.</param>
		/// <param name="serviceUrlBuilder">Service URL builder.</param>
		/// <param name="logger">Logger used for CLI output.</param>
		/// <param name="pageBaselineGuard">Required shared conflict-detection baseline orchestrator. The CLI
		/// entry point auto-discovers the on-disk <c>.clio-pages/{schema}/meta.json</c> baseline
		/// before a save and refreshes it afterwards — so CLI users get the same external-modification
		/// protection as the MCP tools without passing <c>--expected-checksum</c> by hand. Injected as a
		/// required dependency so a broken DI registration fails loudly at resolve time instead of
		/// silently reverting to overwrite-without-checking.</param>
		/// <param name="persistedResourceKeyReader">Required owner of the persisted-resource-key read. It
		/// keys the read by (environment, schema) for the duration of one logical page write, so the three
		/// validation gates that can ask for it resolve the schema hierarchy once between them instead of
		/// once each. Injected as a REQUIRED dependency rather than an optional one: an absent reader is
		/// invisible to every existing test construction, and silently reverting to an uncached read would
		/// restore the duplicate round trips this collaborator exists to remove.</param>
		/// <param name="hierarchyClient">Designer hierarchy client used to resolve replacing schemas.</param>
		/// <param name="viewConfigApplierFactory">Creates the platform diff interpreter for mandatory parent validation.</param>
		/// <param name="pageDesignerPresenceNotifier">Best-effort notifier used by the update-page
		/// entry points to publish Designer Presence save events.</param>
		[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
			Justification = "DI constructor: each collaborator owns a separate write concern. The factory creates a fresh stateful diff interpreter per hierarchy; bundling unrelated services would hide dependencies.")]
		public PageUpdateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger,
			IPageBaselineGuard pageBaselineGuard,
			IPersistedResourceKeyReader persistedResourceKeyReader,
			IPageDesignerHierarchyClient hierarchyClient = null,
			IPageDesignerPresenceNotifier? pageDesignerPresenceNotifier = null,
			Func<IJsonDiffApplier> viewConfigApplierFactory = null) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
			_hierarchyClient = hierarchyClient;
			_viewConfigApplierFactory = viewConfigApplierFactory;
			_pageDesignerPresenceNotifier = pageDesignerPresenceNotifier;
			_pageBaselineGuard = pageBaselineGuard;
			_persistedResourceKeyReader = persistedResourceKeyReader;
		}

		/// <summary>
		/// Attempts to validate and save the requested raw page body.
		/// </summary>
		/// <param name="options">Command options.</param>
		/// <param name="response">Structured command response.</param>
		/// <returns><c>true</c> when the page was updated successfully; otherwise <c>false</c>.</returns>
		public bool TryUpdatePage(PageUpdateOptions options, out PageUpdateResponse response) {
			bool succeeded = TryUpdatePageCore(options, out response);
			// GH-1150: stamped HERE rather than at each failure site, because the failure sites upstream of the
			// dry-run branch outnumber the ones inside it - body-file load, required-field, common-input,
			// context resolution, external-modification and input validation all return before the mode is ever
			// branched on, and TryUpdatePageCore's catch returns a bare envelope. Stamping them individually is
			// a rule someone has to remember at every new exit; stamping the one exit they all funnel through is
			// not. Without it a failed `--dry-run` is byte-identical to a failed real save and the caller cannot
			// tell whether anything was written - the property this ticket exists to establish.
			// The MCP tool has its OWN pre-execution exits that never reach here and stamps them with the same
			// helper; see PageUpdateResponse.MarkDryRunFailure.
			response?.MarkDryRunFailure(options.DryRun, options.SchemaName);
			return succeeded;
		}

		/// <summary>
		/// The body of <see cref="TryUpdatePage"/>. Split out so every failure exit is stamped in one place;
		/// see the comment in that method's body.
		/// </summary>
		private bool TryUpdatePageCore(PageUpdateOptions options, out PageUpdateResponse response) {
			try {
				if (!TryLoadBodyFromFile(options, out response)) return false;
				// Single chokepoint for update-page, sync-pages, and the CLI: run the registered before-save
				// page-body preprocessors before validating/saving. Fail-safe; a no-op for bodies no preprocessor
				// applies to. See PageBodyBeforeSavePreprocessingPipeline.
				options.Body = PageBodyBeforeSavePreprocessingPipeline.Preprocess(options.Body);
				PageUpdateResponse earlyError = ValidateRequiredFields(options);
				if (earlyError != null) { response = earlyError; return false; }
				PageUpdateResponse commonValidationError = ValidateCommonInput(
					options, out Dictionary<string, string> explicitResources, out JArray parsedOptionalProperties);
				if (commonValidationError != null) { response = commonValidationError; return false; }
				if (!TryResolveContext(options, out EditableSchemaContext context, out response)) return false;
				if (!TryCheckForExternalModification(options, context, out response)) return false;
				// The context is already in hand here, so the read delegate reuses it and resolves NOTHING.
				// Routing it through the reader is still what makes this gate free when an MCP pre-execution
				// gate already read the same (environment, schema) earlier in the same call — and what makes
				// the reason reachable through GetFailureWarning when this gate is the one that read.
				PageUpdateResponse validationError = ValidateInput(
					options, context.SchemaType, explicitResources,
					() => _persistedResourceKeyReader
						.Read(options, () => ReadPersistedResourceKeys(context)).Keys);
				if (validationError != null) { response = validationError; return false; }
				return options.DryRun
					? TryCompleteDryRun(options, context, explicitResources, parsedOptionalProperties, out response)
					: TrySaveValidatedPage(options, context, explicitResources, parsedOptionalProperties, out response);
			} catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		/// <summary>
		/// Everything a write needs, resolved once: the schema DTO with the new body and the final
		/// <c>localizableStrings</c> already merged in, the body that would be written, the append
		/// projection, the resource keys the save would register, and the outcome of every body check.
		/// </summary>
		/// <remarks>
		/// A dry run and a save differ only in what they DO with this, which is the point. Before
		/// ENG-96262 they assembled their own validation independently and drifted: the save ran the
		/// authoritative caption gate against the merged body and the final registration set, while the dry
		/// run ran an advisory variant against the caller's fragment and the explicit resources alone. That
		/// disagreed in both directions - a caption bound to a string already registered on the server
		/// warned on the dry run and passed the save, and a caption the save would REJECT was invisible to
		/// the dry run. A preview that can disagree with the commit is the defect this ticket exists to
		/// remove, so there is now one gate, on one body, and severity is the only per-path difference.
		/// </remarks>
		private sealed record PreparedWrite(
			JObject Schema,
			string BodyToWrite,
			PageAppendProjection Projection,
			List<string> RegisteredKeys,
			IReadOnlyList<string> DowngradeWarnings,
			IReadOnlyList<string> InertWarnings,
			PageUpdateResponse CaptionGateFailure);

		/// <summary>
		/// Resolves the write both paths would perform, WITHOUT saving anything. Read-only against the
		/// environment: it fetches the schema and mutates an in-memory DTO; nothing reaches the server until
		/// <see cref="TrySaveSchema"/>, which a dry run never calls.
		/// </summary>
		/// <returns><c>true</c> when the write could be resolved; <c>false</c> with a failure response.</returns>
		private bool TryPrepareWrite(
			PageUpdateOptions options,
			EditableSchemaContext context,
			Dictionary<string, string> explicitResources,
			JArray parsedOptionalProperties,
			out PreparedWrite prepared,
			out PageUpdateResponse response) {
			prepared = null;
			if (!TryLoadSchemaForSave(options.SchemaName, context, out JObject schemaToSave, out response)) return false;
			if (!TryResolveBodyToWrite(schemaToSave, options, out string bodyToWrite,
				out PageAppendProjection projection, out response)) return false;
			if (!TryValidateParents(bodyToWrite, context, out response)) return false;
			// Captured BEFORE UpdateSchemaBody overwrites `body` with the resolved one.
			IReadOnlyList<string> downgradeWarnings =
				PageInsertDowngradeDetector.Detect(schemaToSave["body"]?.ToString(), bodyToWrite);
			IReadOnlyList<string> inertWarnings = PageInertOperationDetector.Detect(bodyToWrite);
			List<string> registeredKeys = UpdateSchemaBody(
				schemaToSave, bodyToWrite, context.SchemaType, explicitResources, parsedOptionalProperties);
			PageUpdateResponse captionGateFailure =
				ValidateInsertedWidgetCaptionsResolve(options, schemaToSave, bodyToWrite, context.SchemaType);
			prepared = new PreparedWrite(schemaToSave, bodyToWrite, projection, registeredKeys,
				downgradeWarnings, inertWarnings, captionGateFailure);
			return true;
		}

		private bool TryValidateParents(string body, EditableSchemaContext context, out PageUpdateResponse response) {
			response = null;
			if (context.SchemaType == PageSchemaType.Mobile) return true;
			JArray candidate = PageParentNameValidation.ReadDiff(body);
			if (!candidate.OfType<JObject>().Any(x => (x.Value<string>("operation") is "insert" or "move" or "set")
				&& (!string.IsNullOrEmpty(x.Value<string>("parentName")) || !string.IsNullOrEmpty(x.Value<string>("nameTo"))))) return true;
			if (_hierarchyClient is null || _viewConfigApplierFactory is null)
				throw new InvalidOperationException("Page parent validation requires the designer hierarchy and diff applier.");
			IEnumerable<PageDesignerHierarchySchema> inherited = GetInheritedHierarchy(context);
			IJsonDiffApplier applier = _viewConfigApplierFactory();
			JToken view = new JArray();
			foreach (PageDesignerHierarchySchema part in inherited.Reverse().Where(part => !string.IsNullOrWhiteSpace(part.Body))) {
				view = applier.Apply(view, PageParentNameValidation.ReadDiff(part.Body),
					new JsonApplierOperationsOptions { ApplyMoveIfIndirectParentMoved = part.SchemaVersion >= 1 });
			}
			applier.Apply(view, candidate, new JsonApplierOperationsOptions { RejectUnresolvedParents = true });
			return true;
		}

		private IEnumerable<PageDesignerHierarchySchema> GetInheritedHierarchy(EditableSchemaContext context) {
			string uid = context.IsCreateReplacing ? context.TemplateSchemaUId : context.EditableSchemaUId;
			string package = context.DesignPackageUId ?? _hierarchyClient.GetDesignPackageUId(uid);
			IReadOnlyList<PageDesignerHierarchySchema> hierarchy = context.ResolvedHierarchy ?? _hierarchyClient.GetParentSchemas(uid, package);
			if (hierarchy is null || hierarchy.Count == 0)
				throw new InvalidOperationException("Cannot validate parentName: page hierarchy is unavailable.");
			IEnumerable<PageDesignerHierarchySchema> inherited = hierarchy;
			if (!context.IsCreateReplacing) {
				int own = hierarchy.ToList().FindIndex(x => SchemaUIdsMatch(x.UId, uid));
				if (own < 0) throw new InvalidOperationException("Cannot validate parentName: target schema is missing from the hierarchy.");
				inherited = hierarchy.Skip(own + 1);
			}
			return inherited;
		}

		private bool TryCompleteDryRun(
			PageUpdateOptions options,
			EditableSchemaContext context,
			Dictionary<string, string> explicitResources,
			JArray parsedOptionalProperties,
			out PageUpdateResponse response) {
			if (!IsAppendMode(options)) {
				if (!TryValidateParents(options.Body, context, out response)) return false;
				// Parent references require the target hierarchy even during a dry run. No schema is saved.
				// Caption checks remain fragment-scoped because replace does not fetch localizableStrings.
				response = CreateSuccessResponse(options, dryRun: true, registeredKeys: null);
				response.Warnings = CombineWarnings(
					BuildDryRunWidgetCaptionWarnings(options.Body, context.SchemaType, explicitResources),
					PageInertOperationDetector.Detect(options.Body));
				return true;
			}
			// ENG-96262 / GH-1150: append used to return before the merge, so a dry run reported `success`
			// while naming nothing the write would change, and its caption gate read the caller's fragment
			// while the save read the merged body and the final registration set - disagreeing in BOTH
			// directions. Append already fetches the schema, so it now resolves exactly the write the save
			// would perform and runs the save's own gate, with severity the only difference.
			// A failure here needs no stamping: TryUpdatePage marks every dry-run failure on the way out.
			if (!TryPrepareWrite(options, context, explicitResources, parsedOptionalProperties,
				out PreparedWrite prepared, out response)) return false;
			response = CreateSuccessResponse(options, dryRun: true, registeredKeys: null);
			response.AppendProjection = prepared.Projection;
			// The same four sources the save reports, against the same body. The caption gate is the one that
			// changes SEVERITY rather than content: blocking on a save, advisory here, because a dry run's job
			// is to tell you what would happen rather than to refuse.
			response.Warnings = CombineWarnings(
				BuildCaptionGateWarnings(prepared.CaptionGateFailure),
				BuildProjectedLossWarnings(prepared.Projection),
				prepared.DowngradeWarnings,
				prepared.InertWarnings);
			return true;
		}

		private bool TrySaveValidatedPage(
			PageUpdateOptions options,
			EditableSchemaContext context,
			Dictionary<string, string> explicitResources,
			JArray parsedOptionalProperties,
			out PageUpdateResponse response) {
			if (!TryPrepareWrite(options, context, explicitResources, parsedOptionalProperties,
				out PreparedWrite prepared, out response)) return false;
			if (prepared.CaptionGateFailure != null) { response = prepared.CaptionGateFailure; return false; }
			if (!TrySaveSchema(prepared.Schema, out response)) return false;
			response = CreateSuccessResponse(options, dryRun: false, prepared.RegisteredKeys);
			// The save reports the same projection: a caller who skipped the dry run has no other place to
			// learn what the merge did.
			response.AppendProjection = prepared.Projection;
			response.Warnings = CombineWarnings(
				BuildProjectedLossWarnings(prepared.Projection), prepared.DowngradeWarnings, prepared.InertWarnings,
				explicitResources?.Count > 0 || prepared.RegisteredKeys?.Count > 0
					? new List<string> { ResourceWorkspaceCaptureWarning } : null);
			PopulatePostSaveChecksum(options, context, response);
			AppendDesignerPresenceWarning(options, response);
			return true;
		}

		internal const string ResourceWorkspaceCaptureWarning =
			"Page resources were saved on the server; update-page does not capture your workspace source. " +
			"A push-workspace from stale metadata/resource XML can revert these changes. Preserve local edits, " +
			"capture the affected package with restore-workspace (pull-workspace), and review its schema metadata " +
			"and culture resource XML before pushing. For linked FSM workspaces, follow the workspace's capture instructions.";

		/// <summary>
		/// Reads the resource keys already persisted on the target schema, resolving the schema hierarchy
		/// itself. The entry point for a caller that has NO resolved schema context of its own — the MCP
		/// pre-execution gates of <c>update-page</c> and <c>sync-pages</c>.
		/// </summary>
		/// <param name="options">The pending write request identifying the schema and environment.</param>
		/// <returns>
		/// The keys, and the reason when the read produced none. Never throws and never <c>null</c>.
		/// </returns>
		/// <remarks>
		/// Best-effort and intended for the FAILURE path only: it costs a hierarchy resolution plus a
		/// <c>GetSchema</c> round-trip, and an empty result simply restores the previous, stricter
		/// behaviour rather than letting an unvalidated body through. Route calls through
		/// <see cref="IPersistedResourceKeyReader"/> so the resolution is paid once per (environment,
		/// schema) across every gate of one logical save.
		/// </remarks>
		internal PersistedResourceKeyRead ReadPersistedResourceKeys(PageUpdateOptions options) {
			try {
				if (!TryResolveContext(options, out EditableSchemaContext context,
					out PageUpdateResponse resolutionFailure)) {
					// A CLEAN resolution failure produced no warning at all before, leaving the caller with the
					// misleading "resource is neither auto-provided nor registered" (issue #1320).
					return LogPersistedResourceKeyFailure(resolutionFailure?.Error);
				}
				return ReadPersistedResourceKeys(context);
			} catch (Exception ex) when (ex is not OperationCanceledException) {
				return LogPersistedResourceKeyFailure(ex.Message);
			}
		}

		/// <summary>
		/// Reads the resource keys already persisted on the schema's <c>localizableStrings</c>, for a
		/// caller that has ALREADY resolved the target schema context.
		/// </summary>
		/// <param name="context">The resolved target schema.</param>
		/// <returns>The keys, and the reason when the read produced none.</returns>
		/// <remarks>
		/// Used ONLY on the failure path of the label-resource validators, so the extra <c>GetSchema</c>
		/// round-trip is not paid by a body that validates cleanly, and a body that fails on structure
		/// still reports its own error rather than a network error. A key already stored on the schema
		/// resolves at runtime whether or not the current call repeats it in <c>resources</c>; without
		/// this the second and every later save of the same page was rejected unless the caller re-sent
		/// every key it had ever registered (issue #1320). Best-effort: any failure degrades to an empty
		/// set, which restores the previous, stricter behaviour instead of letting the save through.
		/// <para>
		/// PURE with respect to the request — the memo that used to live on <see cref="PageUpdateOptions"/>
		/// is gone. Caching is <see cref="IPersistedResourceKeyReader"/>'s job, keyed by the thing actually
		/// being read rather than by the identity of one options instance (issue #1464).
		/// </para>
		/// </remarks>
		private PersistedResourceKeyRead ReadPersistedResourceKeys(EditableSchemaContext context) {
			try {
				if (context.IsCreateReplacing) {
					// Nothing is persisted yet on a schema this save is about to create.
					return PersistedResourceKeyRead.None;
				}
				// A CLEAN GetSchema refusal is the third way this read ends with no keys, and it used to be
				// the only silent one: the designer service answers success:false (schema not found, access
				// denied, a redirected target UId), TryGetSchema returns false with the server's own message,
				// and discarding it through `out _` handed the caller back the misleading "resource 'X' is
				// neither auto-provided ... nor registered" that issue #1320 opened with.
				if (!TryGetSchema(context.TemplateSchemaUId, out JObject schema, out string schemaError)) {
					return LogPersistedResourceKeyFailure(schemaError);
				}
				return PersistedResourceKeyRead.FromKeys(
					ResourceStringHelper.GetExistingKeys(schema[LocalizableStringsKey] as JArray));
			} catch (Exception ex) when (ex is not OperationCanceledException) {
				return LogPersistedResourceKeyFailure(ex.Message);
			}
		}

		/// <summary>
		/// Records why the persisted-resource-key rescue could not read the schema, and returns the failed
		/// result carrying that reason.
		/// </summary>
		/// <remarks>
		/// The verdict deliberately stays unchanged - the caller falls back to the stricter one - but the
		/// reason must not vanish. Without this, a 401, an unreachable environment or a failed hierarchy
		/// resolution reaches the caller as "resource 'X' is neither auto-provided ... nor registered",
		/// i.e. exactly the misleading cause issue #1320 opened with, one layer down. The log line is for
		/// the CLI reader; the returned warning is what reaches an MCP caller's typed response.
		/// </remarks>
		private PersistedResourceKeyRead LogPersistedResourceKeyFailure(string detail) {
			PersistedResourceKeyRead failure = PersistedResourceKeyRead.Failure(detail);
			_logger?.WriteWarning(failure.FailureWarning);
			return failure;
		}

		/// <summary>
		/// Validates widget caption resource resolutions for a REPLACE dry run (web pages only), returning
		/// advisory warnings. Weaker than the save's gate on purpose: without the server's
		/// <c>localizableStrings</c> it can only resolve against the explicitly supplied resources, and
		/// fetching them would cost this path its offline guarantee. An append dry run does not use this -
		/// it already has the schema, so it runs the authoritative gate instead.
		/// </summary>
		/// <returns>Warning messages for unresolved captions, or <c>null</c> if none.</returns>
		private static List<string> BuildDryRunWidgetCaptionWarnings(
				string body, PageSchemaType schemaType, Dictionary<string, string> explicitResources) {
			if (schemaType == PageSchemaType.Mobile) {
				return null;
			}
			SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionResources(body, explicitResources);
			return result.IsValid ? null : new List<string>(result.Errors);
		}

		/// <summary>
		/// Renders the authoritative caption gate's rejection as an advisory warning for an append dry run,
		/// so the preview states exactly what the save would refuse, in the save's own words.
		/// </summary>
		private static IReadOnlyList<string> BuildCaptionGateWarnings(PageUpdateResponse captionGateFailure) =>
			captionGateFailure is null ? null : [captionGateFailure.Error];

		/// <summary>
		/// Whether the caller asked for the incoming body to be merged with the schema's current body
		/// rather than written verbatim. One predicate, because three separate call sites now branch on it -
		/// whether the merge runs, whether marker integrity is validated, and whether a dry run fetches the
		/// server's body at all - and they have to stay in lockstep.
		/// </summary>
		private static bool IsAppendMode(PageUpdateOptions options) =>
			string.Equals(options.Mode, AppendMode, StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Turns the losses an append merge can inflict on the <c>viewConfigDiff</c> array into advisory
		/// warnings: the per-identity superseded-drop sentences the merge itself produced, and the case
		/// where the merged array never reaches the written body at all.
		/// </summary>
		/// <remarks>
		/// Two channels warn, a third deliberately does not. A REPLACEMENT is not a loss - the operation
		/// survives carrying the caller's values - and warning on it would fire on most appends. A
		/// COLLAPSED INCOMING entry is a real loss that is still not warned about; that reasoning has one
		/// owner, on <see cref="PageAppendProjection.CollapsedIncomingOperations"/>.
		/// The superseded-drop sentences are built by the merge rather than rebuilt here, so the wording
		/// and the one-per-identity rule live in one place.
		/// </remarks>
		private static IReadOnlyList<string> BuildProjectedLossWarnings(PageAppendProjection projection) {
			if (projection is null) {
				return null;
			}
			List<string> warnings = null;
			if (projection.SupersededDropWarnings is { Count: > 0 }) {
				(warnings ??= []).AddRange(projection.SupersededDropWarnings);
			}
			// Gated on the fragment actually carrying viewConfigDiff operations. A handlers-only fragment
			// against a body with no SCHEMA_VIEW_CONFIG_DIFF pair merges and lands correctly - warning that
			// "EVERY viewConfigDiff operation is discarded" would describe a loss that did not happen and
			// send the caller to --mode replace for a write that succeeded.
			if (!projection.ViewConfigDiffApplied && projection.IncomingOperationCount > 0) {
				(warnings ??= []).Add(
					"The page's current body has no SCHEMA_VIEW_CONFIG_DIFF marker pair, so the merged " +
					"viewConfigDiff array cannot be written back and EVERY viewConfigDiff operation in the " +
					"fragment is discarded - the counts in appendProjection describe an array the write throws " +
					"away. Use --mode replace with a body that carries the marker pair. " +
					"See docs://mcp/guides/page-modification.");
			}
			return warnings;
		}

		/// <summary>
		/// Builds the user-facing conflict guidance shown when an external modification is detected.
		/// </summary>
		private static string BuildConflictErrorMessage(string schemaName) =>
			$"Page schema '{schemaName}' was modified outside this session (external modification detected). " +
			"Do NOT retry with the same body. Re-run get-page for this schema, re-apply your change on top of the fresh body, then retry. " +
			"Re-sending this response's actualChecksum as the checksum argument is NOT a resolution - it discards the external change exactly like force=true and needs the same explicit user confirmation. " +
			"Use force=true ONLY after the user explicitly confirms overwriting the external changes.";

		/// <summary>
		/// Compares the caller-supplied baseline (expected checksum / schema UId / absence marker)
		/// against the resolved editable schema state and blocks the save with a structured conflict
		/// when the schema was modified outside the current session. Skipped entirely when
		/// <see cref="PageUpdateOptions.Force"/> is set or no baseline information was supplied.
		/// </summary>
		/// <returns><c>true</c> when the write may proceed; <c>false</c> with a conflict response otherwise.</returns>
		/// <summary>
		/// Arms the baseline that <see cref="PageBaselineGuard"/> could not decide on, once the resolved
		/// target turns out to be the very schema that baseline describes.
		/// </summary>
		/// <remarks>
		/// The guard runs BEFORE the target is resolved, so a save carrying <c>target-package-uid</c> /
		/// <c>target-schema-uid</c> cannot be told apart there from a genuine redirect. Treating every
		/// selector as a redirect dropped the still-applicable baseline: passing the page's own existing
		/// package as the target made a stale body save with <c>success: true</c> over a concurrent
		/// writer's change, while the same call without the selector correctly reported a conflict.
		/// A caller-pinned checksum still wins — it is the stronger, explicitly supplied witness — and a
		/// target that resolves elsewhere still leaves the baseline dropped, which is the redirect case
		/// the guard exists to handle.
		/// </remarks>
		/// <summary>
		/// Compares two schema UIds by VALUE rather than by spelling.
		/// </summary>
		/// <remarks>
		/// The two sides reach this comparison from different places and are written by different people.
		/// The left one comes off disk, in whatever form clio recorded; the right one can be the raw
		/// <c>--target-schema-uid</c> the caller typed, which <c>TryResolveContext</c> uses verbatim. GUIDs
		/// have several legal spellings, and a braced <c>{xxxxxxxx-…}</c> selector against a bare recorded
		/// UId is the same schema written two ways. A plain string comparison read that as a redirect and
		/// silently skipped the post-save refresh — the very failure issue #1538 is about, reintroduced for
		/// one input shape. Only a value that does not parse as a GUID at all falls back to the string
		/// comparison, so nothing that used to match stops matching.
		/// </remarks>
		/// <param name="recordedSchemaUId">The schema UId carried over from the on-disk baseline.</param>
		/// <param name="resolvedSchemaUId">The schema UId the write actually resolved to.</param>
		/// <returns><c>true</c> when both name the same schema.</returns>
		private static bool SchemaUIdsMatch(string recordedSchemaUId, string resolvedSchemaUId) {
			if (string.IsNullOrWhiteSpace(recordedSchemaUId) || string.IsNullOrWhiteSpace(resolvedSchemaUId)) {
				return false;
			}
			if (Guid.TryParse(recordedSchemaUId, out Guid recorded) && Guid.TryParse(resolvedSchemaUId, out Guid resolved)) {
				return recorded == resolved;
			}
			return string.Equals(recordedSchemaUId, resolvedSchemaUId, StringComparison.OrdinalIgnoreCase);
		}

		private static void PromoteConditionalBaselineWhenTargetMatches(
				PageUpdateOptions options, EditableSchemaContext context) {
			if (!SchemaUIdsMatch(options.ConditionalBaselineSchemaUId, context.EditableSchemaUId)) {
				return;
			}
			// The write landed on the very schema the on-disk baseline describes, so that baseline is the
			// one a successful save must rewrite - independently of which witness governed the conflict
			// check. Leaving this false for a pinned save left the baseline holding a superseded checksum,
			// and the caller's next UNPINNED save then conflicted with its own previous one (issue #1538).
			options.ConditionalBaselineApplied = true;
			if (!string.IsNullOrWhiteSpace(options.ExpectedChecksum)) {
				// A caller-pinned checksum is the stronger, explicitly supplied witness: it keeps governing
				// the conflict check, and the match decides only the post-save refresh.
				return;
			}
			options.ExpectedChecksum = options.ConditionalBaselineChecksum;
			options.ExpectedSchemaUId = options.ConditionalBaselineSchemaUId;
			options.ExpectedSchemaAbsent = options.ConditionalBaselineSchemaAbsent;
		}

		private bool TryCheckForExternalModification(
				PageUpdateOptions options,
				EditableSchemaContext context,
				out PageUpdateResponse response) {
			response = null;
			PromoteConditionalBaselineWhenTargetMatches(options, context);
			if (options.Force) return true;
			bool hasChecksum = !string.IsNullOrWhiteSpace(options.ExpectedChecksum);
			if (!hasChecksum && !options.ExpectedSchemaAbsent) return true;
			if (options.ExpectedSchemaAbsent) {
				if (context.IsCreateReplacing) return true;
				response = CreateConflictResponse(options, new PageConflictDetails {
					Reason = PageConflictReasons.SchemaCreatedExternally,
					// Echoed so the refusal is diagnosable: it tells the caller which baseline the
					// verdict was formed against instead of leaving "modified outside this session"
					// as the only clue.
					ExpectedChecksum = options.ExpectedChecksum,
					ActualSchemaUId = context.EditableSchemaUId
				});
				return false;
			}
			if (context.IsCreateReplacing) {
				response = CreateConflictResponse(options, new PageConflictDetails {
					Reason = PageConflictReasons.SchemaDeletedExternally,
					ExpectedChecksum = options.ExpectedChecksum,
					ExpectedSchemaUId = options.ExpectedSchemaUId
				});
				return false;
			}
			if (!string.IsNullOrWhiteSpace(options.ExpectedSchemaUId)
				&& !SchemaUIdsMatch(options.ExpectedSchemaUId, context.EditableSchemaUId)) {
				response = CreateConflictResponse(options, new PageConflictDetails {
					Reason = PageConflictReasons.SchemaUIdMismatch,
					ExpectedChecksum = options.ExpectedChecksum,
					ExpectedSchemaUId = options.ExpectedSchemaUId,
					ActualSchemaUId = context.EditableSchemaUId
				});
				return false;
			}
			(JToken row, _) = PageSchemaMetadataHelper.QuerySysSchemaRowByUId(
				_applicationClient, _serviceUrlBuilder, context.EditableSchemaUId,
				(ChecksumColumnName, ChecksumColumnName), (ModifiedOnColumnName, ModifiedOnColumnName));
			if (row is null) {
				response = CreateConflictResponse(options, new PageConflictDetails {
					Reason = PageConflictReasons.SchemaDeletedExternally,
					ExpectedChecksum = options.ExpectedChecksum,
					ExpectedSchemaUId = options.ExpectedSchemaUId ?? context.EditableSchemaUId
				});
				return false;
			}
			string actualChecksum = row[ChecksumColumnName]?.ToString();
			if (string.IsNullOrWhiteSpace(actualChecksum)) {
				// Fail open: a present row with a NULL/blank Checksum (e.g. an unpublished schema) means
				// "checksum unavailable", not proof of an external edit. Reporting a conflict here is
				// misleading and loops the agent — skip the check, consistent with the fail-toward-no-check contract.
				return true;
			}
			if (!string.Equals(actualChecksum, options.ExpectedChecksum, StringComparison.Ordinal)) {
				response = CreateConflictResponse(options, new PageConflictDetails {
					Reason = PageConflictReasons.ChecksumMismatch,
					ExpectedChecksum = options.ExpectedChecksum,
					ActualChecksum = actualChecksum,
					ExpectedSchemaUId = options.ExpectedSchemaUId ?? context.EditableSchemaUId,
					ActualSchemaUId = context.EditableSchemaUId,
					ModifiedOn = row[ModifiedOnColumnName]?.ToString()
				});
				return false;
			}
			return true;
		}

		private static PageUpdateResponse CreateConflictResponse(PageUpdateOptions options, PageConflictDetails details) =>
			new() {
				Success = false,
				Conflict = true,
				ConflictDetails = details,
				SchemaName = options.SchemaName,
				Error = BuildConflictErrorMessage(options.SchemaName)
			};

		/// <summary>
		/// Best-effort post-save checksum refresh: queries the fresh <c>SysSchema.Checksum</c> /
		/// <c>ModifiedOn</c> of the schema the save wrote to. Runs only when the caller supplied
		/// baseline information (or <c>force</c>) so the no-baseline path costs zero extra queries.
		/// Query failure leaves the fields <c>null</c> — callers holding an on-disk baseline must
		/// then discard it instead of keeping a stale checksum.
		/// </summary>
		private void PopulatePostSaveChecksum(
				PageUpdateOptions options,
				EditableSchemaContext context,
				PageUpdateResponse response) {
			bool baselineInPlay = options.Force
				|| options.ExpectedSchemaAbsent
				|| !string.IsNullOrWhiteSpace(options.ExpectedChecksum);
			if (!baselineInPlay) return;
			response.SavedSchemaUId = context.EditableSchemaUId;
			try {
				(JToken row, _) = PageSchemaMetadataHelper.QuerySysSchemaRowByUId(
					_applicationClient, _serviceUrlBuilder, context.EditableSchemaUId,
					(ChecksumColumnName, ChecksumColumnName), (ModifiedOnColumnName, ModifiedOnColumnName));
				if (row is null) return;
				response.NewChecksum = row[ChecksumColumnName]?.ToString();
				response.NewModifiedOn = row[ModifiedOnColumnName]?.ToString();
			} catch {
				// best-effort — the save already succeeded; null NewChecksum signals the MCP layer
				// to delete the on-disk baseline rather than keep a stale one.
			}
		}

		private static bool TryLoadBodyFromFile(PageUpdateOptions options, out PageUpdateResponse response) {
			(bool ok, string error) = PageUpdateBodyLoader.TryLoadBodyFromFile(options);
			response = ok ? null : new PageUpdateResponse { Success = false, Error = error };
			return ok;
		}

		private bool TryResolveContext(PageUpdateOptions options, out EditableSchemaContext context, out PageUpdateResponse response) {
			if (string.IsNullOrWhiteSpace(options.TargetSchemaUId)) {
				if (!TryResolveEditableSchemaContext(options.SchemaName, options.TargetPackageUId, out context, out response))
					return false;
				if (context.SchemaType == PageSchemaType.Unknown)
					context.SchemaType = PageSchemaTypeExtensions.FromBody(options.Body);
				return true;
			}
			PageSchemaType pageSchemaType = PageSchemaTypeExtensions.FromBody(options.Body);
			context = new EditableSchemaContext {
				SchemaName = options.SchemaName,
				EditableSchemaUId = options.TargetSchemaUId,
				TemplateSchemaUId = options.TargetSchemaUId,
				IsCreateReplacing = false,
				SchemaType = pageSchemaType,
			};
			response = null;
			return true;
		}

		private static bool TryResolveBodyToWrite(JObject schemaToSave, PageUpdateOptions options,
			out string bodyToWrite, out PageAppendProjection projection, out PageUpdateResponse response) {
			projection = null;
			bodyToWrite = options.Body;
			response = null;
			if (IsAppendMode(options)) {
				string currentBody = schemaToSave["body"]?.ToString();
				if (!string.IsNullOrWhiteSpace(currentBody)) {
					try {
						bodyToWrite = PageBodyMerger.Merge(currentBody, options.Body, out projection);
					} catch (Exception ex) {
						// A full-config rejection (identified by its dedicated exception type, not by re-parsing the
						// message) is already a complete, self-contained sentence — it names the offending body
						// (incoming vs the server's) and points at replace mode — so it needs neither the "Append merge
						// failed:" prefix (which double-states the verb) nor the generic marker-pairs hint (a full-config
						// body HAS valid markers, it is just the wrong form). Keep both only for genuine marker-shape
						// merge failures, and phrase the hint role-agnostically so it never blames the incoming body for
						// a server-side blocker (ENG-94422).
						string error = ex is PageBodyMerger.FullConfigAppendNotSupportedException
							? $"{ex.Message} [hint: see docs://mcp/guides/page-modification for the append diff-form contract.]"
							: $"Append merge failed: {ex.Message} [hint: the body must contain valid marker pairs with new viewConfigDiff/handlers operations. See docs://mcp/guides/page-modification.]";
						response = new PageUpdateResponse { Success = false, Error = error };
						return false;
					}
				}
			}
			if (!IsAppendMode(options) ||
				PageSchemaTypeExtensions.FromBody(bodyToWrite) == PageSchemaType.Mobile) {
				return true;
			}
			if (!options.Validate) {
				return true;
			}
			SchemaValidationResult validatorReferences =
				SchemaValidationService.ValidateCustomValidatorReferences(bodyToWrite);
			if (validatorReferences.IsValid) {
				return true;
			}
			response = ContentValidationFailure(
				$"Body contains unresolved custom validator references: {string.Join("; ", validatorReferences.Errors)}");
			return false;
		}

		/// <summary>
		/// Executes the command and writes the structured response to the CLI output.
		/// </summary>
		/// <param name="options">Command options.</param>
		/// <returns>Command exit code.</returns>
		public override int Execute(PageUpdateOptions options) {
			// One CLI invocation is one logical page write, so it gets one persisted-key caching scope. It
			// changes no verdict — with no scope every read simply runs uncached — but it is what makes the
			// failure reason reachable below, on the surface that has no MCP tool above it.
			using IDisposable persistedResourceKeyScope = _persistedResourceKeyReader.BeginRequestScope();
			options.NotifyDesignerPresence = true;
			// Mirror the MCP tool: auto-discover the on-disk baseline so a CLI save (e.g. an AI agent
			// running `clio update-page --body-file .clio-pages/<schema>/body.js`) is blocked when the
			// schema was modified out-of-band, instead of silently overwriting the external edit.
			(string metaFilePath, bool refreshBaseline, string baselineWarning) =
				_pageBaselineGuard.TryArm(options, outputDirectory: null);
			bool success = TryUpdatePage(options, out PageUpdateResponse response);
			if ((refreshBaseline || options.ConditionalBaselineApplied) && success && !options.DryRun) {
				// A failed refresh cannot fail a save that already landed on the server, so it surfaces as a
				// warning on the response instead (ENG-95262 AC-02).
				AppendBaselineWarning(response, _pageBaselineGuard.RefreshOrDrop(metaFilePath, options, response));
			}
			AppendBaselineWarning(response, baselineWarning);
			// A failed persisted-key read never changes the verdict, but its reason must reach the caller
			// on the response - not only the log - so a 401 or an unresolved hierarchy is not reported as
			// "resource is neither auto-provided nor registered" (issue #1320).
			AppendBaselineWarning(response, _persistedResourceKeyReader.GetFailureWarning(options));
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		// Surfaces a baseline discovery/refresh diagnostic on the response envelope. The baseline path is
		// best-effort by contract, so its failures are warnings, never errors — but they must be visible:
		// a silently lost refresh leaves the stored checksum behind the server and the next save can then
		// report a conflict that never happened.
		private static void AppendBaselineWarning(PageUpdateResponse response, string warning) {
			if (response is null || string.IsNullOrWhiteSpace(warning)) {
				return;
			}
			List<string> warnings = response.Warnings?.ToList() ?? [];
			warnings.Add(warning);
			response.Warnings = warnings;
		}

		/// <summary>
		/// Folds several advisory warning sources into one list, or <c>null</c> when every source is empty.
		/// </summary>
		/// <remarks>
		/// This exists because the success path has more than one warning producer: assigning
		/// <c>response.Warnings</c> per source would let the last one silently discard the others.
		/// <see cref="AppendDesignerPresenceWarning"/> appends afterwards and is unaffected.
		/// <para>
		/// Returns <c>null</c> rather than an empty list on purpose: <c>PageUpdateResponse.Warnings</c> is
		/// serialized with null-omission, so an empty list would emit <c>"warnings":[]</c> on a clean save.
		/// The detectors feeding this return empty-never-null, which is the opposite convention — the
		/// conversion happens here, once, and a consumer of the response must null-guard.
		/// </para>
		/// </remarks>
		private static IReadOnlyList<string> CombineWarnings(params IReadOnlyList<string>[] sources) {
			List<string> combined = null;
			foreach (IReadOnlyList<string> source in sources) {
				if (source is not { Count: > 0 }) {
					continue;
				}
				combined ??= [];
				combined.AddRange(source);
			}
			return combined;
		}

		private void AppendDesignerPresenceWarning(PageUpdateOptions options, PageUpdateResponse response) {
			if (!options.NotifyDesignerPresence || _pageDesignerPresenceNotifier is null) {
				return;
			}
			string? warning = _pageDesignerPresenceNotifier.TryNotifyPageSaved(options.SchemaName, options.SchemaName);
			if (string.IsNullOrWhiteSpace(warning)) {
				return;
			}
			List<string> warnings = response.Warnings?.ToList() ?? [];
			warnings.Add(warning);
			response.Warnings = warnings;
		}

		private string TargetPackageUIdOverride { get; set; }

		private bool TryResolveEditableSchemaContext(string schemaName, string targetPackageUIdOverride, out EditableSchemaContext context, out PageUpdateResponse response) {
			TargetPackageUIdOverride = targetPackageUIdOverride;
			context = null;
			(JToken metadata, string queryError) = PageSchemaMetadataHelper.QuerySysSchemaRow(_applicationClient, _serviceUrlBuilder, schemaName, ("UId", "UId"));
			if (metadata == null) { response = new PageUpdateResponse { Success = false, Error = queryError }; return false; }
			string rawSchemaUId = metadata["UId"]?.ToString();
			if (string.IsNullOrWhiteSpace(rawSchemaUId)) { response = new PageUpdateResponse { Success = false, Error = $"Schema '{schemaName}' metadata is missing UId" }; return false; }
			if (_hierarchyClient == null) {
				context = new EditableSchemaContext {
					SchemaName = schemaName,
					EditableSchemaUId = rawSchemaUId,
					TemplateSchemaUId = rawSchemaUId,
					IsCreateReplacing = false,
					SchemaType = PageSchemaType.Unknown
				};
				response = null;
				return true;
			}
			if (!TryGetDesignPackageUId(rawSchemaUId, schemaName, out string designPackageUId, out response)) return false;
			if (!TryGetHierarchy(rawSchemaUId, designPackageUId, schemaName, out IReadOnlyList<PageDesignerHierarchySchema> hierarchy, out response)) return false;
			PageDesignerHierarchySchema head = hierarchy[0];
			PageSchemaType pageSchemaType = PageSchemaTypeExtensions.FromNumericValue(head.SchemaType);
			string rootUId = FindRootSchemaUId(hierarchy, schemaName);
			PageDesignerHierarchySchema root = !string.IsNullOrWhiteSpace(rootUId)
				? hierarchy.FirstOrDefault(s => string.Equals(s.UId, rootUId, StringComparison.OrdinalIgnoreCase)) ?? head : head;
			(string editableUId, bool isCreateReplacing) = ResolveEditableUId(head, schemaName, designPackageUId);
			// Fail-closed backstop (mobile): if hierarchy resolution would materialize a REPLACING schema in
			// the design package while the base schema (head) is freshly created / body-empty in a different
			// package, the write would leave that empty base behind — and the Creatio Mobile app loads the
			// empty base and CRASHES. Refuse the write with an actionable fix rather than produce the split.
			// (Web pages legitimately use replacing schemas across apps, so this guard is mobile-only; a
			// non-empty head is a real platform page being replaced and is NOT blocked. The target-schema-uid
			// path bypasses this method entirely and stays available as the escape hatch named below.)
			if (isCreateReplacing && pageSchemaType == PageSchemaType.Mobile && string.IsNullOrWhiteSpace(head.Body)) {
				response = new PageUpdateResponse {
					Success = false,
					Error = $"Refusing to write mobile page '{schemaName}': this would create a REPLACING schema in "
						+ $"design package '{designPackageUId}' and leave the empty base schema '{head.UId}' in package "
						+ $"'{head.PackageName}' unrendered — the Creatio Mobile app loads that empty base and crashes. "
						+ $"Pass target-schema-uid={head.UId} to write the body into the base schema, or create the "
						+ "page directly in the design package."
				};
				return false;
			}
			context = new EditableSchemaContext {
				SchemaName = schemaName,
				EditableSchemaUId = editableUId,
				DesignPackageUId = designPackageUId,
				IsCreateReplacing = isCreateReplacing,
				ParentSchemaUId = isCreateReplacing ? root.UId : null,
				ParentSchemaName = root.Name,
				TemplateSchemaUId = isCreateReplacing ? root.UId : editableUId,
				SchemaType = pageSchemaType,
                ResolvedHierarchy = isCreateReplacing ? null : hierarchy
			};
			response = null;
			return true;
		}

		private bool TryGetDesignPackageUId(string rawSchemaUId, string schemaName, out string designPackageUId, out PageUpdateResponse response) {
			if (!string.IsNullOrWhiteSpace(TargetPackageUIdOverride)) {
				designPackageUId = TargetPackageUIdOverride;
				response = null;
				return true;
			}
			try {
				designPackageUId = _hierarchyClient.GetDesignPackageUId(rawSchemaUId);
			} catch (Exception ex) {
				designPackageUId = null;
				response = new PageUpdateResponse { Success = false, Error = $"Failed to resolve design package for '{schemaName}': {ex.Message}" };
				return false;
			}
			if (!string.IsNullOrWhiteSpace(designPackageUId)) { response = null; return true; }
			response = new PageUpdateResponse { Success = false, Error = $"Failed to resolve design package for '{schemaName}': no package returned" };
			return false;
		}

		private bool TryGetHierarchy(string rawSchemaUId, string designPackageUId, string schemaName, out IReadOnlyList<PageDesignerHierarchySchema> hierarchy, out PageUpdateResponse response) {
			try {
				hierarchy = _hierarchyClient.GetParentSchemas(rawSchemaUId, designPackageUId);
			} catch (Exception ex) {
				hierarchy = null;
				response = new PageUpdateResponse { Success = false, Error = PageHierarchyRecoveryHint.Append($"Failed to load hierarchy for '{schemaName}': {ex.Message}") };
				return false;
			}
			if (hierarchy != null && hierarchy.Count > 0) { response = null; return true; }
			// F1 (ENG-94418 review): an empty hierarchy is not a phantom-cache signal (it has non-phantom
			// causes), so it does not get the recovery hint — only the empty-IN() SqlException does.
			response = new PageUpdateResponse { Success = false, Error = $"Schema '{schemaName}' hierarchy is empty" };
			return false;
		}

		private (string editableUId, bool isCreateReplacing) ResolveEditableUId(PageDesignerHierarchySchema head, string schemaName, string designPackageUId) {
			if (string.Equals(head.PackageUId, designPackageUId, StringComparison.OrdinalIgnoreCase))
				return (head.UId, false);
			string existingInPkg = PageSchemaMetadataHelper.FindExistingSchemaInPackage(_applicationClient, _serviceUrlBuilder, schemaName, designPackageUId);
			return string.IsNullOrWhiteSpace(existingInPkg) ? (Guid.NewGuid().ToString(), true) : (existingInPkg, false);
		}

		internal sealed class EditableSchemaContext {
            public IReadOnlyList<PageDesignerHierarchySchema> ResolvedHierarchy { get; set; }
			public string SchemaName { get; set; }
			public string EditableSchemaUId { get; set; }
			public string DesignPackageUId { get; set; }
			public bool IsCreateReplacing { get; set; }
			public string ParentSchemaUId { get; set; }
			public string ParentSchemaName { get; set; }
			public string TemplateSchemaUId { get; set; }
			public PageSchemaType SchemaType { get; set; }
		}

		private static string FindRootSchemaUId(IReadOnlyList<PageDesignerHierarchySchema> hierarchy, string schemaName) {
			for (int i = hierarchy.Count - 1; i >= 0; i--) {
				if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
					return hierarchy[i].UId;
				}
			}
			return null;
		}

		private static List<string> UpdateSchemaBody(JObject schemaToSave, string body, PageSchemaType schemaType,
				Dictionary<string, string> explicitResources, JArray optionalProperties = null) {
			schemaToSave["body"] = body;
			if (optionalProperties != null) {
				MergeOptionalProperties(schemaToSave, optionalProperties);
			}
			HashSet<string> bodyKeys = ResourceStringHelper.ExtractKeys(body);
			Dictionary<string, string> modelPaths = schemaType == PageSchemaType.Mobile
				? SchemaValidationService.CollectMobileViewModelPaths(body)
				: SchemaValidationService.CollectViewModelPaths(body);
			var dsBoundKeys = modelPaths.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var existingStrings = schemaToSave[LocalizableStringsKey] as JArray;
			(JArray cleaned, List<string> registered) = ResourceStringHelper.CleanAndMerge(existingStrings, explicitResources, bodyKeys, dsBoundKeys);
			schemaToSave[LocalizableStringsKey] = cleaned;
			return registered.Count > 0 ? registered : null;
		}

		/// <summary>
		/// Authoritative widget-caption resolvability gate. After <see cref="UpdateSchemaBody"/>
		/// has produced the final <c>localizableStrings</c>, this rejects the save when a freshly inserted
		/// widget/container caption binds a localizable key that is neither
		/// present in that final set nor auto-provided by a DS-bound attribute
		/// </summary>
		/// <returns>A failure response when a saved inserted widget caption would render raw; otherwise <c>null</c>.</returns>
		private static PageUpdateResponse ValidateInsertedWidgetCaptionsResolve(
				PageUpdateOptions options, JObject schemaToSave, string body, PageSchemaType schemaType) {
			// validate=false is the explicit escape hatch for a pre-existing page defect: skip the
			// client-side content checks here rather than at the call site, so TryUpdatePage stays flat.
			if (!options.Validate) {
				return null;
			}
			if (schemaType == PageSchemaType.Mobile) {
				return null;
			}
			HashSet<string> registeredNames = ResourceStringHelper.GetExistingKeys(schemaToSave[LocalizableStringsKey] as JArray);
			HashSet<string> dsBoundKeys = SchemaValidationService.CollectViewModelPaths(body).Keys
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionsRegistered(
				body, registeredNames, dsBoundKeys);
			if (result.IsValid) {
				return null;
			}
			return new PageUpdateResponse {
				Success = false,
				Error = $"Body contains inserted widget captions bound to unregistered localizable strings: {string.Join("; ", result.Errors)}"
			};
		}

		private static void MergeOptionalProperties(JObject schemaToSave, JArray incoming) {
			var existing = schemaToSave["optionalProperties"] as JArray ?? new JArray();
			var merged = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
			foreach (JToken item in existing) {
				string key = item["key"]?.ToString();
				if (!string.IsNullOrWhiteSpace(key)) {
					merged[key] = item;
				}
			}
			foreach (JToken item in incoming) {
				string key = item["key"]?.ToString();
				if (!string.IsNullOrWhiteSpace(key)) {
					merged[key] = item;
				}
			}
			schemaToSave["optionalProperties"] = new JArray(merged.Values);
		}

		private bool TryLoadSchemaForSave(
				string schemaName,
				EditableSchemaContext context,
				out JObject schemaToSave,
				out PageUpdateResponse response) {
			if (!TryGetSchema(context.TemplateSchemaUId, out JObject template, out string loadError)) {
				schemaToSave = null;
				response = new PageUpdateResponse {
					Success = false,
					Error = loadError ?? $"Failed to load schema '{schemaName}'"
				};
				return false;
			}
			schemaToSave = context.IsCreateReplacing
				? BuildNewReplacingSchemaDto(template, context)
				: template;
			response = null;
			return true;
		}

		private bool TryGetSchema(string schemaUId, out JObject schema, out string error) {
			var request = new JObject {
				["schemaUId"] = schemaUId,
				["useFullHierarchy"] = false
			};
			string url = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
			string json = _applicationClient.ExecutePostRequest(url, request.ToString(Formatting.None));
			var response = JObject.Parse(json);
			if (!(response["success"]?.Value<bool>() ?? false) || response["schema"] is not JObject loaded) {
				schema = null;
				error = response["errorInfo"]?["message"]?.ToString() ?? $"Failed to load schema '{schemaUId}'";
				return false;
			}
			schema = loaded;
			error = null;
			return true;
		}

		private static JObject BuildNewReplacingSchemaDto(JObject template, EditableSchemaContext context) {
			string originalName = template["name"]?.ToString() ?? context.SchemaName;
			JObject dto = (JObject)template.DeepClone();
			dto["uId"] = context.EditableSchemaUId;
			dto["name"] = originalName;
			dto["isReadOnly"] = false;
			dto["extendParent"] = true;
			dto[LocalizableStringsKey] = template[LocalizableStringsKey]?.DeepClone() ?? new JArray();
			dto["package"] = new JObject {
				["uId"] = context.DesignPackageUId,
				["name"] = string.Empty
			};
			dto["parent"] = new JObject {
				["uId"] = context.ParentSchemaUId,
				["name"] = context.ParentSchemaName ?? originalName
			};
			dto["body"] = BuildEmptyReplacingBody(originalName, context.SchemaType);
			return dto;
		}

		private static string BuildEmptyReplacingBody(string schemaName, PageSchemaType schemaType) {
			if (schemaType == PageSchemaType.Mobile) {
				return "{\n\t\"viewConfigDiff\": [],\n\t\"viewModelConfigDiff\": [],\n\t\"modelConfigDiff\": []\n}";
			}
			return "define(\"" + schemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {\n" +
				"\treturn {\n" +
				"\t\tviewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,\n" +
				"\t\tviewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,\n" +
				"\t\tmodelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,\n" +
				"\t\thandlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,\n" +
				"\t\tconverters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,\n" +
				"\t\tvalidators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/\n" +
				"\t};\n" +
				"});";
		}

		private bool TrySaveSchema(JObject schemaToSave, out PageUpdateResponse response) {
			string saveUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
			string saveJson = _applicationClient.ExecutePostRequest(saveUrl, schemaToSave.ToString(Formatting.None));
			var saveResponse = JObject.Parse(saveJson);
			if (saveResponse["success"]?.Value<bool>() ?? false) {
				TryResetScriptCache();
				response = null;
				return true;
			}
			response = new PageUpdateResponse {
				Success = false,
				Error = BuildSaveErrorMessage(saveResponse)
			};
			return false;
		}

		/// <summary>
		/// Mirrors the frontend PageDesigner post-save behaviour by invalidating the workplace
		/// script cache via <c>/rest/WorkplaceService/ResetScriptCache</c>. Without this call,
		/// Creatio serves stale bundle JSON after a schema save — runtime pages omit fresh
		/// replacing schemas and the next <c>GetParentSchemas</c> returns the pre-save hierarchy,
		/// which tricks subsequent update-page calls into the CREATE branch and spawns duplicate
		/// replacing schemas in the design package.
		/// </summary>
		private void TryResetScriptCache() {
			try {
				string resetUrl = _serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache");
				_applicationClient.ExecutePostRequest(resetUrl, string.Empty);
			} catch {
				// Cache reset is best-effort; never block a successful save on it.
			}
		}

		private static string BuildSaveErrorMessage(JObject saveResponse) =>
			AppendActionableHint(PageSchemaMetadataHelper.ParseSaveErrorMessage(saveResponse, "Failed to save page schema"));

		private static string AppendActionableHint(string serverError) {
			if (string.IsNullOrEmpty(serverError)) {
				return serverError;
			}
			if (serverError.Contains("requires an element of type 'Object'", StringComparison.OrdinalIgnoreCase) &&
				serverError.Contains("type 'Array'", StringComparison.OrdinalIgnoreCase)) {
				return serverError + " [hint: this typically happens when re-sending the full get-page body verbatim in " +
					"mode='replace' — the mode in which the body reaches the server; " +
					"backend re-applies existing merges that now conflict with parent hierarchy. " +
					"Send only NEW viewConfigDiff/handlers operations (the new component insert + matching handler), " +
					"not the entire inherited body. See docs://mcp/guides/page-modification for the minimal-diff pattern.]";
			}
			if (serverError.Contains("Item with name", StringComparison.OrdinalIgnoreCase) &&
				serverError.Contains("not found", StringComparison.OrdinalIgnoreCase)) {
				return serverError + " [hint: the schema manager cache may be holding a stale phantom replacing schema " +
					"from an earlier failed save. Restart Creatio to clear the cache, or verify the schema UId via list-pages.]";
			}
			if (serverError.Contains("third-party publisher", StringComparison.OrdinalIgnoreCase) ||
				serverError.Contains("installed from the file archive", StringComparison.OrdinalIgnoreCase)) {
				return serverError + " [hint: the schema is owned by a package whose maintainer differs from the " +
					"current workspace maintainer, so Creatio blocks direct in-place edits. " +
					"Fix by saving into a replacing schema in your design package: call update-page with " +
					"mode=append (auto-detects design package and creates a replacement there) or pass " +
					"target-package-uid explicitly to the app's design package. See " +
					"docs://mcp/guides/page-modification section 'multi-app replacements'.]";
			}
			return serverError;
		}

		private static PageUpdateResponse CreateSuccessResponse(
			PageUpdateOptions options,
			bool dryRun,
			List<string> registeredKeys) {
			return new PageUpdateResponse {
				Success = true,
				SchemaName = options.SchemaName,
				BodyLength = options.Body.Length,
				DryRun = dryRun,
				ResourcesRegistered = registeredKeys?.Count ?? 0,
				RegisteredResourceKeys = registeredKeys
			};
		}

		private static PageUpdateResponse ValidateRequiredFields(PageUpdateOptions options) {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				return new PageUpdateResponse { Success = false, Error = "schemaName is required" };
			}
			if (string.IsNullOrWhiteSpace(options.Body)) {
				return new PageUpdateResponse {
					Success = false,
					Error = "body is required and must not be empty. Reuse the get-page body (CLI: raw.body; MCP: the contents of the file at files.bodyFile) instead of bundle or viewConfig fragments."
				};
			}
			return null;
		}

		private static PageUpdateResponse ValidateCommonInput(
			PageUpdateOptions options,
			out Dictionary<string, string> explicitResources,
			out JArray parsedOptionalProperties) {
			parsedOptionalProperties = null;
			if (!SchemaValidationService.TryParseResources(options.Resources, out explicitResources, out _)) {
				return new PageUpdateResponse {
					Success = false,
					Error = InvalidResourcesError
				};
			}
			if (!PageOptionalPropertiesHelper.TryParse(
					options.OptionalProperties, out parsedOptionalProperties, out string optionalPropertiesError)) {
				return new PageUpdateResponse {
					Success = false,
					Error = optionalPropertiesError
				};
			}
			return null;
		}

		/// <summary>The canonical error for a malformed <c>resources</c> payload.</summary>
		internal const string InvalidResourcesError = "resources must be a valid JSON object string";
		internal const string MobileValidationFailedPrefix = "Mobile page validation failed: ";

		/// <summary>
		/// Text appended to every CONTENT-validation failure so the caller learns about the escape hatch at
		/// the point of failure rather than only from the tool description or the curated contract. Only the
		/// skippable half of the chain carries it - a structural-floor failure is not bypassable and must not
		/// advertise a flag that will not help.
		/// </summary>
		internal const string ValidationEscapeHatchHint =
			" If this defect pre-exists on the page and is unrelated to your edit, re-run with validate=false.";

		/// <summary>
		/// Builds a failure for a rule in the SKIPPABLE half of the chain. The hint itself is NOT appended
		/// here: <see cref="PageUpdateCommand"/> is the CLI-reachable command and <c>Validate</c> carries no
		/// <c>[Option]</c>, so a CLI user would be told to re-run with a flag their parser does not accept.
		/// The response is only MARKED, and the MCP adapter appends the hint for the callers that can act on it.
		/// </summary>
		private static PageUpdateResponse ContentValidationFailure(string error) => new() {
			Success = false,
			Error = error,
			ContentValidationFailure = true
		};

		/// <summary>
		/// Validates the <c>resources</c> and <c>optional-properties</c> argument payloads WITHOUT
		/// parsing the page body or touching the network. Returns the canonical, user-facing error
		/// string for the first malformed payload, or <c>null</c> when both are well-formed (or absent).
		/// Used by the MCP <c>update-page</c> tool to surface a specific, actionable argument error over
		/// the generic whole-body JavaScript syntax error when a body fails to parse but a payload
		/// argument is also malformed (ENG-90640 shadowing fix). The wording is shared with
		/// <see cref="ValidateCommonInput"/> so both code paths report identically.
		/// </summary>
		/// <param name="resources">The <c>resources</c> JSON object string argument, or <c>null</c>.</param>
		/// <param name="optionalProperties">The <c>optional-properties</c> JSON array string argument, or <c>null</c>.</param>
		/// <returns>The canonical error message for the first malformed payload, or <c>null</c> when valid.</returns>
		public static string ValidateArgumentPayloads(string resources, string optionalProperties) {
			if (!SchemaValidationService.TryParseResources(resources, out _, out _)) {
				return InvalidResourcesError;
			}
			if (!PageOptionalPropertiesHelper.TryParse(optionalProperties, out _, out string optionalPropertiesError)) {
				return optionalPropertiesError;
			}
			return null;
		}

		/// <summary>
		/// Runs the input validation chain. The chain has two halves and <c>validate=false</c> only skips
		/// the second one:
		/// <list type="bullet">
		/// <item>the STRUCTURAL floor - marker integrity (replace mode) and JavaScript syntax on web,
		/// JSON-parses-to-an-object on mobile - always runs, because a body that fails it produces a page
		/// the tool itself can no longer read back;</item>
		/// <item>the CONTENT rules - handler structure, field bindings, insert self-consistency, validator
		/// placement, mobile AMD/shape rules - run only when <see cref="Validate"/> is true.</item>
		/// </list>
		/// </summary>
		private static PageUpdateResponse ValidateInput(
			PageUpdateOptions options,
			PageSchemaType schemaType,
			Dictionary<string, string> explicitResources,
			Func<IReadOnlySet<string>> persistedResourceKeysProvider = null) {
			return schemaType == PageSchemaType.Mobile
				? ValidateMobileInput(options)
				: ValidateWebInput(options, explicitResources, persistedResourceKeysProvider);
		}

		private static PageUpdateResponse ValidateMobileInput(PageUpdateOptions options) {
			// Structural floor - runs even behind validate=false. It is the mobile counterpart of the web
			// syntax gate: a body that is not a JSON object is not a page, and nothing downstream re-checks
			// it (UpdateSchemaBody's only parse, CollectMobileViewModelPaths, is fail-soft).
			SchemaValidationResult structureResult = SchemaValidationService.ValidateMobileBodyStructure(options.Body);
			if (!structureResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = MobileValidationFailedPrefix + string.Join("; ", structureResult.Errors)
				};
			}
			if (!options.Validate) {
				return null;
			}
			SchemaValidationResult mobileResult = SchemaValidationService.ValidateMobileBody(options.Body);
			if (!mobileResult.IsValid) {
				return ContentValidationFailure(
					MobileValidationFailedPrefix + string.Join("; ", mobileResult.Errors));
			}
			return null;
		}

		private static PageUpdateResponse ValidateWebInput(
			PageUpdateOptions options,
			Dictionary<string, string> explicitResources,
			Func<IReadOnlySet<string>> persistedResourceKeysProvider = null) {
			// Structural floor - marker integrity and JS syntax run even behind validate=false. A markerless
			// body is valid JavaScript, so it would save, after which PageSchemaSectionReader can no longer
			// extract sections and append-merge is dead on that page. ResolveSyntaxFailure already treats
			// markers as the "is this still a recognizable page" test for the same reason.
			bool isAppendMode = IsAppendMode(options);
			if (isAppendMode) {
				// Append relaxes COMPLETENESS, not recognizability. A fragment may omit sections; a body that
				// carries none at all is always a mistake, and accepting it meant reporting success for a merge
				// that discarded the caller's whole fragment. See ValidateAppendFragmentIsRecognizable.
				SchemaValidationResult fragmentResult =
					SchemaValidationService.ValidateAppendFragmentIsRecognizable(options.Body);
				if (!fragmentResult.IsValid) {
					return new PageUpdateResponse {
						Success = false,
						Error = $"Append body carries no recognizable page section: {string.Join("; ", fragmentResult.Errors)}"
					};
				}
			} else {
				SchemaValidationResult integrityResult = SchemaValidationService.ValidateMarkerIntegrity(options.Body);
				if (!integrityResult.IsValid) {
					return new PageUpdateResponse {
						Success = false,
						Error = $"Body is missing required marker pairs: {string.Join("; ", integrityResult.Errors)}"
					};
				}
			}
			SchemaValidationResult syntaxResult = SchemaValidationService.ValidateJsSyntax(options.Body);
			if (!syntaxResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid JavaScript syntax: {string.Join("; ", syntaxResult.Errors)}"
				};
			}
			// Content rules - the half the escape hatch skips.
			if (!options.Validate) {
				return null;
			}
			SchemaValidationResult handlerResult = SchemaValidationService.ValidateHandlerStructure(options.Body);
			if (!handlerResult.IsValid) {
				return ContentValidationFailure($"Body contains invalid handlers: {string.Join("; ", handlerResult.Errors)}");
			}
			(SchemaValidationResult semanticResult, SchemaValidationResult insertSelfConsistencyResult) =
				SchemaValidationService.ValidateFieldLabelResources(
					options.Body, explicitResources, persistedResourceKeysProvider);
			if (!semanticResult.IsValid) {
				return ContentValidationFailure($"Body contains invalid form field bindings: {string.Join("; ", semanticResult.Errors)}");
			}
			if (!insertSelfConsistencyResult.IsValid) {
				return ContentValidationFailure($"Body contains inserted field controls without required bindings or resources: {string.Join("; ", insertSelfConsistencyResult.Errors)}");
			}
			SchemaValidationResult validatorPlacementResult = SchemaValidationService.ValidateValidatorBindingPlacement(options.Body);
			if (!validatorPlacementResult.IsValid) {
				return ContentValidationFailure($"Body contains invalid validator bindings: {string.Join("; ", validatorPlacementResult.Errors)}");
			}
			return null;
		}
	}
}
