namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
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
		/// <c>append</c> merges the provided body fragment with the current schema body on the server.
		/// </summary>
		[Option("mode", Required = false, HelpText = "Write mode: 'replace' (default) or 'append' (merge with existing body)")]
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
		/// Gets or sets a value indicating whether the successful save path should attempt a
		/// best-effort Designer Presence push. Internal orchestration flag; not exposed as a CLI
		/// option and enabled only by the dedicated <c>update-page</c> entry points.
		/// </summary>
		internal bool NotifyDesignerPresence { get; set; }
	}

	/// <summary>
	/// Validates and saves raw Freedom UI page bodies.
	/// </summary>
	public class PageUpdateCommand : Command<PageUpdateOptions>
	{
		private const string LocalizableStringsKey = "localizableStrings";
		private const string ChecksumColumnName = "Checksum";
		private const string ModifiedOnColumnName = "ModifiedOn";

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;
		private readonly IPageDesignerHierarchyClient _hierarchyClient;
		private readonly IPageDesignerPresenceNotifier? _pageDesignerPresenceNotifier;
		private readonly IPageBaselineGuard _pageBaselineGuard;

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
		/// <param name="hierarchyClient">Designer hierarchy client used to resolve replacing schemas.</param>
		/// <param name="pageDesignerPresenceNotifier">Best-effort notifier used by the update-page
		/// entry points to publish Designer Presence save events.</param>
		public PageUpdateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger,
			IPageBaselineGuard pageBaselineGuard,
			IPageDesignerHierarchyClient hierarchyClient = null,
			IPageDesignerPresenceNotifier? pageDesignerPresenceNotifier = null) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
			_hierarchyClient = hierarchyClient;
			_pageDesignerPresenceNotifier = pageDesignerPresenceNotifier;
			_pageBaselineGuard = pageBaselineGuard;
		}

		/// <summary>
		/// Attempts to validate and save the requested raw page body.
		/// </summary>
		/// <param name="options">Command options.</param>
		/// <param name="response">Structured command response.</param>
		/// <returns><c>true</c> when the page was updated successfully; otherwise <c>false</c>.</returns>
		public bool TryUpdatePage(PageUpdateOptions options, out PageUpdateResponse response) {
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
				PageUpdateResponse validationError = ValidateInput(options, context.SchemaType, explicitResources);
				if (validationError != null) { response = validationError; return false; }
				if (options.DryRun) {
					response = CreateSuccessResponse(options, dryRun: true, registeredKeys: null);
					response.Warnings = BuildDryRunWidgetCaptionWarnings(options.Body, context.SchemaType, explicitResources);
					return true;
				}
				if (!TryLoadSchemaForSave(options.SchemaName, context, out JObject schemaToSave, out response)) return false;
				if (!TryResolveBodyToWrite(schemaToSave, options, out string bodyToWrite, out response)) return false;
				IReadOnlyList<string> downgradeWarnings = PageInsertDowngradeDetector.Detect(schemaToSave["body"]?.ToString(), bodyToWrite);
				List<string> registeredKeys = UpdateSchemaBody(schemaToSave, bodyToWrite, context.SchemaType, explicitResources, parsedOptionalProperties);
				PageUpdateResponse captionError = ValidateInsertedWidgetCaptionsResolve(schemaToSave, bodyToWrite, context.SchemaType);
				if (captionError != null) { response = captionError; return false; }
				if (!TrySaveSchema(schemaToSave, out response)) return false;
				response = CreateSuccessResponse(options, dryRun: false, registeredKeys);
				response.Warnings = downgradeWarnings.Count > 0 ? downgradeWarnings : null;
				PopulatePostSaveChecksum(options, context, response);
				AppendDesignerPresenceWarning(options, response);
				return true;
			} catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		/// <summary>
		/// Builds the user-facing conflict guidance shown when an external modification is detected.
		/// </summary>
		private static string BuildConflictErrorMessage(string schemaName) =>
			$"Page schema '{schemaName}' was modified outside this session (external modification detected). " +
			"Do NOT retry with the same body. Re-run get-page for this schema, re-apply your change on top of the fresh body, then retry. " +
			"Use force=true ONLY after the user explicitly confirms overwriting the external changes.";

		/// <summary>
		/// Compares the caller-supplied baseline (expected checksum / schema UId / absence marker)
		/// against the resolved editable schema state and blocks the save with a structured conflict
		/// when the schema was modified outside the current session. Skipped entirely when
		/// <see cref="PageUpdateOptions.Force"/> is set or no baseline information was supplied.
		/// </summary>
		/// <returns><c>true</c> when the write may proceed; <c>false</c> with a conflict response otherwise.</returns>
		private bool TryCheckForExternalModification(
				PageUpdateOptions options,
				EditableSchemaContext context,
				out PageUpdateResponse response) {
			response = null;
			if (options.Force) return true;
			bool hasChecksum = !string.IsNullOrWhiteSpace(options.ExpectedChecksum);
			if (!hasChecksum && !options.ExpectedSchemaAbsent) return true;
			if (options.ExpectedSchemaAbsent) {
				if (context.IsCreateReplacing) return true;
				response = CreateConflictResponse(options, new PageConflictDetails {
					Reason = PageConflictReasons.SchemaCreatedExternally,
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
				&& !string.Equals(options.ExpectedSchemaUId, context.EditableSchemaUId, StringComparison.OrdinalIgnoreCase)) {
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
				SchemaType = pageSchemaType
			};
			response = null;
			return true;
		}

		private static bool TryResolveBodyToWrite(JObject schemaToSave, PageUpdateOptions options, out string bodyToWrite, out PageUpdateResponse response) {
			bodyToWrite = options.Body;
			response = null;
			if (!string.Equals(options.Mode, "append", StringComparison.OrdinalIgnoreCase)) return true;
			string currentBody = schemaToSave["body"]?.ToString();
			if (string.IsNullOrWhiteSpace(currentBody)) return true;
			try {
				bodyToWrite = PageBodyMerger.Merge(currentBody, options.Body);
				return true;
			} catch (Exception ex) {
				response = new PageUpdateResponse { Success = false, Error = $"Append merge failed: {ex.Message} [hint: the incoming body must contain valid marker pairs with new viewConfigDiff/handlers operations. See docs://mcp/guides/page-modification.]" };
				return false;
			}
		}

		/// <summary>
		/// Executes the command and writes the structured response to the CLI output.
		/// </summary>
		/// <param name="options">Command options.</param>
		/// <returns>Command exit code.</returns>
		public override int Execute(PageUpdateOptions options) {
			options.NotifyDesignerPresence = true;
			// Mirror the MCP tool: auto-discover the on-disk baseline so a CLI save (e.g. an AI agent
			// running `clio update-page --body-file .clio-pages/<schema>/body.js`) is blocked when the
			// schema was modified out-of-band, instead of silently overwriting the external edit.
			(string metaFilePath, bool baselineArmed) = _pageBaselineGuard.TryArm(options, outputDirectory: null);
			bool success = TryUpdatePage(options, out PageUpdateResponse response);
			if (baselineArmed && success && !options.DryRun) {
				_pageBaselineGuard.RefreshOrDrop(metaFilePath, options, response);
			}
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
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
			context = new EditableSchemaContext {
				SchemaName = schemaName,
				EditableSchemaUId = editableUId,
				DesignPackageUId = designPackageUId,
				IsCreateReplacing = isCreateReplacing,
				ParentSchemaUId = isCreateReplacing ? root.UId : null,
				ParentSchemaName = root.Name,
				TemplateSchemaUId = isCreateReplacing ? root.UId : editableUId,
				SchemaType = pageSchemaType
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
				response = new PageUpdateResponse { Success = false, Error = $"Failed to load hierarchy for '{schemaName}': {ex.Message}" };
				return false;
			}
			if (hierarchy != null && hierarchy.Count > 0) { response = null; return true; }
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
		/// Authoritative widget-caption resolvability gate (ENG-93098). After <see cref="UpdateSchemaBody"/>
		/// has produced the final <c>localizableStrings</c>, this rejects the save when a freshly inserted
		/// widget/container caption (title/caption/tooltip/placeholder) binds a localizable key that is neither
		/// present in that final set nor auto-provided by a DS-bound attribute — such a binding would compile to
		/// <c>$Resources.Strings.&lt;Key&gt;</c> and render raw. Because it checks the real post-merge
		/// registration outcome (existing entries + explicit resources + auto-derived Usr keys, all folded in by
		/// <see cref="ResourceStringHelper.CleanAndMerge"/>), it never false-positives on a re-inserted caption
		/// whose key a prior save already registered. Web bodies only — a mobile body has no marker-delimited
		/// <c>viewConfigDiff</c> section for the scan to read.
		/// </summary>
		/// <returns>A failure response when a saved inserted widget caption would render raw; otherwise <c>null</c>.</returns>
		/// <summary>
		/// Dry-run analog of <see cref="ValidateInsertedWidgetCaptionsResolve"/>. A dry run validates without
		/// loading or saving the schema, so the authoritative post-merge gate cannot run; instead surface the
		/// body-only heuristic (<see cref="SchemaValidationService.ValidateInsertedWidgetCaptionResources"/>)
		/// as a WARNING so <c>update-page --dry-run</c> no longer reports green for exactly the ENG-93098 body
		/// a real save rejects. Kept a warning (not an error) because — like validate-page — a dry run has no
		/// schema context and a hard reject here would false-positive on a re-inserted caption whose key a
		/// prior save already registered. Web bodies only (a mobile body carries no marker-delimited section).
		/// </summary>
		/// <returns>The advisory warning messages, or <c>null</c> when there is nothing to warn about.</returns>
		private static List<string> BuildDryRunWidgetCaptionWarnings(
				string body, PageSchemaType schemaType, Dictionary<string, string> explicitResources) {
			if (schemaType == PageSchemaType.Mobile) {
				return null;
			}
			SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionResources(body, explicitResources);
			return result.IsValid ? null : new List<string>(result.Errors);
		}

		private static PageUpdateResponse ValidateInsertedWidgetCaptionsResolve(
				JObject schemaToSave, string body, PageSchemaType schemaType) {
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
				return serverError + " [hint: this typically happens when re-sending the full get-page raw.body — " +
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
					Error = "body is required and must not be empty. Reuse get-page raw.body instead of bundle or viewConfig fragments."
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

		private static PageUpdateResponse ValidateInput(
			PageUpdateOptions options,
			PageSchemaType schemaType,
			Dictionary<string, string> explicitResources) {
			return schemaType == PageSchemaType.Mobile
				? ValidateMobileInput(options)
				: ValidateWebInput(options, explicitResources);
		}

		private static PageUpdateResponse ValidateMobileInput(PageUpdateOptions options) {
			SchemaValidationResult mobileResult = SchemaValidationService.ValidateMobileBody(options.Body);
			if (!mobileResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = "Mobile page validation failed: " + string.Join("; ", mobileResult.Errors)
				};
			}
			return null;
		}

		private static PageUpdateResponse ValidateWebInput(
			PageUpdateOptions options,
			Dictionary<string, string> explicitResources) {
			bool isAppendMode = string.Equals(options.Mode, "append", StringComparison.OrdinalIgnoreCase);
			if (!isAppendMode) {
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
			SchemaValidationResult handlerResult = SchemaValidationService.ValidateHandlerStructure(options.Body);
			if (!handlerResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid handlers: {string.Join("; ", handlerResult.Errors)}"
				};
			}
			SchemaValidationResult semanticResult = SchemaValidationService.ValidateStandardFieldBindings(options.Body, explicitResources);
			if (!semanticResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid form field bindings: {string.Join("; ", semanticResult.Errors)}"
				};
			}
			SchemaValidationResult insertSelfConsistencyResult = SchemaValidationService.ValidateInsertedFieldSelfConsistency(options.Body, explicitResources);
			if (!insertSelfConsistencyResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains inserted field controls without required bindings or resources: {string.Join("; ", insertSelfConsistencyResult.Errors)}"
				};
			}
			SchemaValidationResult validatorPlacementResult = SchemaValidationService.ValidateValidatorBindingPlacement(options.Body);
			if (!validatorPlacementResult.IsValid) {
				return new PageUpdateResponse {
					Success = false,
					Error = $"Body contains invalid validator bindings: {string.Join("; ", validatorPlacementResult.Errors)}"
				};
			}
			return null;
		}
	}
}
