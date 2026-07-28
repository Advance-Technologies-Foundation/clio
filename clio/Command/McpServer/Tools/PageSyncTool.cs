using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Acornima.Ast;
using Clio.Command;
using Clio.UserEnvironment;
using McpServerLib = ModelContextProtocol.Server;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that batches multiple Freedom UI page updates into a single call,
/// reducing MCP round-trips, lock acquisitions, and sleep overhead.
/// </summary>
[McpServerToolType]
public sealed class PageSyncTool(
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem,
	IMobileComponentInfoCatalog mobileComponentCatalog,
	IComponentInfoCatalog webComponentCatalog,
	IPageBodySamplingService samplingService,
	IPageBaselineGuard pageBaselineGuard,
	IPlatformVersionResolverFactory? resolverFactory = null) {

	internal const string ToolName = "sync-pages";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Updates multiple Freedom UI page schemas in a single call. " +
	             "For each page: validates body client-side (optional), runs AI semantic review (optional), saves to Creatio, " +
	             "and verifies the update (optional). Continues processing remaining pages on failure. " +
		             "CONFLICT DETECTION: when get-page previously stored a checksum baseline in .clio-pages/{schema}/meta.json for the same environment, a page whose schema was modified outside this session fails with per-page `conflict: true` + `conflict-details` (other pages in the batch are unaffected). On a conflict: do NOT retry with the same body — re-run get-page for that schema, re-apply your change on top of the fresh body, then retry; inform the user about the external changes and set the per-page `force: true` ONLY after they explicitly confirm overwriting them. " +
		             "When verify=true, the read-back body is written to .clio-pages/{schema-name}/body.js, anchored at the workspace root (or the `output-directory` argument); see get-page for the anchoring rules. " +
	             "Client-side validation, when enabled, also enforces VendorPrefix.Name format " +
	             "(SCHEMA_CONVERTERS and SCHEMA_VALIDATORS keys; SCHEMA_HANDLERS entry `request` values). " +
	             "Before editing page bodies or resource payloads, call get-guidance with name `page-modification` and use its pre-edit checklist to select specialized page-authoring guides. " +
	             "For conditional visibility, editability, required state based on field values or conditional set and clear value. Also filtering of lookups, based on condition or valur from other field (e.g. \"when Status=Closed, hide Description\"), use business rules instead of writing handlers or validators in page body \u2014 call get-guidance with name `business-rules` to learn more. " +
	             "Section authoring rules for the body payload: " +
	             "if the body changes SCHEMA_HANDLERS call get-guidance with name `page-schema-handlers` first; " +
	             "if the body changes SCHEMA_VALIDATORS call get-guidance with name `page-schema-validators` first; " +
	             "if the body changes SCHEMA_CONVERTERS call get-guidance with name `page-schema-converters` first; " +
	             "if the body adds or edits `@creatio-devkit/common` usage call get-guidance with name `page-schema-creatio-devkit-common` before editing SCHEMA_DEPS or SDK calls.")]
	public async Task<PageSyncResponse> SyncPages(
		[Description("Parameters: environment-name (required unless an authorized HTTP credential-passthrough header supplies the target tenant); pages array (required); validate, verify (optional).")]
		[Required] PageSyncArgs args,
		McpServerLib.McpServer server,
		CancellationToken cancellationToken = default) {
		// Materialise the input list once so every downstream stage can index
		// into a stable snapshot. The MCP contract types `Pages` as
		// IEnumerable; without this snapshot a non-list source would be
		// enumerated multiple times and the pre-pass / sampling / sync stages
		// could disagree on positions.
		IReadOnlyList<PageSyncPageInput> pages = args.Pages as IReadOnlyList<PageSyncPageInput> ?? args.Pages.ToArray();
		PageSyncPrePassResults prePass = BuildPrePassResults(pages);
		IReadOnlyList<PageSamplingReview?> samplingResults = await RunSamplingPrePassAsync(
			server, args, pages, prePass, cancellationToken);
		// Registry-driven chart-widget validation needs the async, version-scoped catalog. Scope it to the
		// target environment's platform version (probed the same way get-component-info resolves it) so the
		// batch validates against the component set the environment actually ships, not the broader 'latest'.
		// Resolve the merged type definitions once here on the async entry, then reuse them across the
		// synchronous per-page deterministic triage in ExecuteSyncBatch. Skip the probe entirely when
		// validation is disabled — the definitions would never be consumed. Null when validation is off or
		// the registry/version is unavailable (fail-open).
		IReadOnlyDictionary<string, System.Text.Json.JsonElement>? chartTypeDefinitions = null;
		if (args.Validate ?? true) {
			string? platformVersion = await ResolvePlatformVersionAsync(args.EnvironmentName, cancellationToken).ConfigureAwait(false);
			chartTypeDefinitions = await ChartWidgetValidation
				.ResolveTypeDefinitionsAsync(webComponentCatalog, platformVersion, cancellationToken).ConfigureAwait(false);
		}
		List<PageSyncPageResult> results = ExecuteSyncBatch(args, pages, prePass, samplingResults, chartTypeDefinitions);
		return new PageSyncResponse {
			Success = results.Count > 0 && results.All(r => r.Success),
			Pages = results
		};
	}

	/// <summary>
	/// Resolves the target environment's platform version so the chart-widget validation catalog is scoped
	/// to the component set the environment actually ships (mirroring <c>get-component-info</c>'s resolution).
	/// The guard below only checks for an ABSENT resolver dependency (e.g. a unit test that did not supply
	/// one) — NOT a blank environment name. A blank name is a legitimate, expected shape under authorized
	/// HTTP credential passthrough (the header carries the tenant, not an <c>environment-name</c> argument),
	/// so the probe below must still run and be resolved through the injected
	/// <see cref="IToolCommandResolver"/>: this is what lets the probe reach the header-selected tenant
	/// instead of silently degrading to <c>latest</c> without
	/// ever consulting the credential context (the regression this method previously had). Routing the
	/// settings lookup through <see cref="IToolCommandResolver.Resolve{TCommand}"/> also means a mixed-input
	/// call (header AND an explicit, different <c>environment-name</c>) is rejected by the resolver's
	/// passthrough guard BEFORE any named-tenant settings lookup — the same rejection-first ordering already
	/// enforced for the actual page save later in the batch.
	/// Fail-soft: any probe failure (including the resolver's own rejection, e.g. an unresolvable
	/// environment or a mixed-input rejection) yields <see langword="null"/>, which
	/// <see cref="ChartWidgetValidation"/> maps to the safe <c>latest</c> superset — version resolution
	/// must never block a save.
	/// </summary>
	private async Task<string?> ResolvePlatformVersionAsync(string? environmentName, CancellationToken cancellationToken) {
		if (resolverFactory is null) {
			return null;
		}
		try {
			EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(new EnvironmentOptions { Environment = environmentName });
			if (settings is null) {
				return null;
			}
			PlatformVersionResolution resolution = await resolverFactory.Create(settings)
				.ResolveAsync(cancellationToken).ConfigureAwait(false);
			return resolution?.ResolvedVersion;
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception) {
			// Fail-soft: a bad/unreachable environment, or the resolver's own passthrough/mixed-input
			// rejection, must not break a save. The catalog stays on 'latest' (ChartWidgetValidation maps
			// null -> latest), matching get-component-info's soft degrade.
			return null;
		}
	}

	// Pre-pass: runs the deterministic syntax + AST-lint gates on every web
	// body BEFORE invoking the LLM sampling service or resolving any
	// environment-bound command. Both gates are pure functions of the input
	// body — they need no environment, no network, no lock — so running them
	// here lets us:
	//   1. Materialise per-page fail-fast results from `ExecuteSyncBatch`
	//      without ever issuing a TryUpdatePage call for the broken body.
	//   2. Surface the deterministic diagnosis (`JavaScript syntax error`,
	//      `Page body lint failed`) regardless of environment validity —
	//      a missing or unreachable environment must not mask the gate's
	//      message (e2e fail-fast contract reported by reviewer 2026-06-11).
	//   3. Skip sampling for already-doomed bodies — no LLM tokens spent on
	//      bodies the deterministic gate will block anyway.
	// Per-page entries are keyed by INDEX (not SchemaName) so duplicate
	// schema-name submissions in a single batch do not cross-contaminate
	// (last-write-wins on a Dictionary keyed by SchemaName would lint one
	// page against another page's AST — bug reported by reviewer).
	// Mobile bodies are JSON and skip the JS gates — they are validated by
	// the mobile catalog later, inside the lock block.
	private static PageSyncPrePassResults BuildPrePassResults(IReadOnlyList<PageSyncPageInput> pages) {
		var entries = new List<PageSyncPrePassEntry>(pages.Count);
		foreach (PageSyncPageInput page in pages) {
			entries.Add(BuildPrePassEntry(page));
		}
		return new PageSyncPrePassResults(entries);
	}

	private static PageSyncPrePassEntry BuildPrePassEntry(PageSyncPageInput page) {
		if (PageSchemaTypeExtensions.FromBody(page.Body) == PageSchemaType.Mobile) {
			return PageSyncPrePassEntry.Empty;
		}
		PageBodySyntaxValidationResult syntaxResult =
			PageBodySyntaxValidator.ValidateAndParse(page.Body, out Script ast);
		if (!syntaxResult.IsValid) {
			return new PageSyncPrePassEntry(
				PageBodySyntaxValidator.FormatError(syntaxResult),
				Array.Empty<PageBodyLintFinding>());
		}
		if (ast is null) {
			return PageSyncPrePassEntry.Empty;
		}
		// Lint runs here so we can decide whether to skip sampling on doomed
		// bodies (any Error-severity finding short-circuits sampling); the
		// findings themselves are NOT materialised into a failure result yet
		// — regex content validation runs first inside SyncSinglePage so its
		// established error wording wins on overlapping detections, and lint
		// Errors are only surfaced when regex passes them through.
		IReadOnlyList<PageBodyLintFinding> findings = PageBodyAstLinter.Lint(ast);
		return new PageSyncPrePassEntry(null, findings);
	}

	private async Task<IReadOnlyList<PageSamplingReview?>> RunSamplingPrePassAsync(
		McpServerLib.McpServer server,
		PageSyncArgs args,
		IReadOnlyList<PageSyncPageInput> pages,
		PageSyncPrePassResults prePass,
		CancellationToken cancellationToken) {
		var samplingResults = new PageSamplingReview?[pages.Count];
		if (args.SkipSampling == true) {
			return samplingResults;
		}
		for (int i = 0; i < pages.Count; i++) {
			if (prePass.Entries[i].IsBodyDoomed) {
				continue;
			}
			PageSyncPageInput page = pages[i];
			samplingResults[i] = await samplingService.TrySamplingReviewAsync(
				server, page.SchemaName, page.Body, page.Resources, cancellationToken);
		}
		return samplingResults;
	}

	private List<PageSyncPageResult> ExecuteSyncBatch(
		PageSyncArgs args,
		IReadOnlyList<PageSyncPageInput> pages,
		PageSyncPrePassResults prePass,
		IReadOnlyList<PageSamplingReview?> samplingResults,
		IReadOnlyDictionary<string, System.Text.Json.JsonElement>? chartTypeDefinitions) {
		var results = new List<PageSyncPageResult>(pages.Count);
		var pendingIndices = new List<int>();
		// Step 1: Materialise EVERY deterministic failure (syntax, regex
		// content validation, AST lint Error) into a per-page result FIRST,
		// before any environment-bound command resolution. All three checks
		// are pure functions of the input body — they need no environment,
		// no network, no Creatio lock. Surfacing them ahead of env resolve
		// keeps the deterministic diagnosis visible regardless of environment
		// validity, which is what the new e2e fail-fast contracts assert.
		// Inside the dispatcher:
		//   - syntax failure: short-circuit (no AST, nothing else can run).
		//   - regex content validation runs first when `validate` is true and
		//     the body is a web page; its wording is authoritative on
		//     overlapping detections with the lint pass.
		//   - lint Errors only surface here when regex did not already catch
		//     them — preserves the precedence agreed with reviewers.
		// Mobile validation is async (catalog calls) and stays inside the
		// lock-protected SyncSinglePage path; this stage only triages web
		// pages and bodies whose regex/lint result can be computed offline.
		bool validate = args.Validate ?? true;
		for (int i = 0; i < pages.Count; i++) {
			PageSyncPrePassEntry entry = prePass.Entries[i];
			PageSyncPageInput page = pages[i];
			if (entry.SyntaxFailureMessage is { } syntaxMsg) {
				results.Add(BuildPrePassFailureResult(page, ResolvePrePassSyntaxFailureMessage(page, syntaxMsg, validate)));
				continue;
			}
			PageSyncPageResult deterministicFailure = TryMaterialiseDeterministicFailure(page, entry, validate);
			if (deterministicFailure != null) {
				results.Add(deterministicFailure);
				continue;
			}
			PageSyncPageResult chartFailure = TryMaterialiseChartWidgetFailure(page, validate, chartTypeDefinitions);
			if (chartFailure != null) {
				results.Add(chartFailure);
				continue;
			}
			results.Add(null);
			pendingIndices.Add(i);
		}
		if (pendingIndices.Count == 0) {
			return results;
		}
		// Step 2: Some pages still need to be saved — resolve the environment
		// command and process them under the lock. Any environment-resolution
		// failure replaces only the pending placeholders, leaving already-
		// materialised pre-pass failures untouched.
		bool verify = args.Verify ?? false;
		// FR-05: serialize on the per-tenant lock keyed by the same environment identity the batch's
		// commands resolve under (see TryResolveEnvironmentCommands), so different tenants run concurrently.
		string tenantKey = commandResolver.GetTenantKey(new PageUpdateOptions { Environment = args.EnvironmentName });
		lock (McpToolExecutionLock.GetLock(tenantKey)) {
			McpToolExecutionLock.MarkInUse(tenantKey);
			try {
				if (!TryResolveEnvironmentCommands(args, verify, out PageUpdateCommand updateCommand,
						out PageGetCommand getCommand, out string envError)) {
					FillPendingWithError(results, pendingIndices, pages, envError);
					return results;
				}
				var ctx = new PageSyncBatchContext(
					updateCommand,
					getCommand,
					args.Validate ?? true,
					verify,
					args.OutputDirectory,
					prePass,
					samplingResults) { EnvironmentName = args.EnvironmentName };
				try {
					foreach (int idx in pendingIndices) {
						results[idx] = ProcessPendingPage(pages[idx], idx, ctx);
					}
					Thread.Sleep(500);
				} catch (Exception ex) {
					FillPendingWithError(results, pendingIndices, pages, SensitiveErrorTextRedactor.Redact(ex.Message));
				}
			} finally {
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
		return results;
	}

	private bool TryResolveEnvironmentCommands(
		PageSyncArgs args,
		bool verify,
		out PageUpdateCommand updateCommand,
		out PageGetCommand getCommand,
		out string error) {
		updateCommand = null;
		getCommand = null;
		error = null;
		try {
			updateCommand = commandResolver.Resolve<PageUpdateCommand>(
				new PageUpdateOptions { Environment = args.EnvironmentName });
			if (verify) {
				getCommand = commandResolver.Resolve<PageGetCommand>(
					new PageGetOptions { Environment = args.EnvironmentName });
			}
			return true;
		} catch (Exception ex) {
			error = SensitiveErrorTextRedactor.Redact(ex.Message);
			return false;
		}
	}

	private static void FillPendingWithError(
		List<PageSyncPageResult> results,
		IEnumerable<int> pendingIndices,
		IReadOnlyList<PageSyncPageInput> pages,
		string errorMessage) {
		foreach (int idx in pendingIndices) {
			if (results[idx] != null) {
				continue;
			}
			results[idx] = new PageSyncPageResult {
				SchemaName = pages[idx].SchemaName,
				Success = false,
				Error = errorMessage
			};
		}
	}

	// Materialise a syntax-gate failure as a per-page result. MarkersOk and
	// ContentOk are set to false (not the previously-fabricated `true`) — a
	// syntactic failure means we never ran the markers or content validators
	// for this body, so the envelope must NOT claim those gates passed. Only
	// JsSyntaxOk has authoritative state at this point.
	// A whole-body JS syntax error is frequently a side effect of a more specific,
	// regex-detectable content problem: broken JSON inside a JSON-backed SCHEMA_*
	// marker (e.g. a stray double comma in SCHEMA_VIEW_CONFIG_DIFF), a converter /
	// validator declared with the wrong key shape, a proxy field binding, etc. The
	// generic Acornima message ("JavaScript syntax error at line X, column Y")
	// cannot name the offending section. The deterministic content chain (ValidateBody)
	// can — and it is regex-based, so it runs even on a body that does not parse as
	// JavaScript. When validation is on and that chain pinpoints a concrete problem,
	// prefer its specific, actionable error over the generic parser message. A genuine
	// JS-only syntax error (clean markers + content) leaves the chain with no errors,
	// so the ENG-89796 generic syntax wording is preserved for that case.
	private static string ResolvePrePassSyntaxFailureMessage(PageSyncPageInput page, string syntaxMessage, bool validate) {
		if (!validate) {
			return syntaxMessage;
		}
		// Only override the generic syntax error when the body is still a recognizable
		// page body — markers present and correctly paired. If marker integrity itself
		// fails (e.g. missing SCHEMA_DEPS / SCHEMA_ARGS), the body is not a usable page
		// at all and the generic JS syntax error is the more honest, actionable signal
		// (ENG-89796). This keeps FailFast-on-pure-syntax-error behaviour intact while
		// still pinpointing a marker/content problem inside an otherwise well-formed page.
		if (!SchemaValidationService.ValidateMarkerIntegrity(page.Body).IsValid) {
			return syntaxMessage;
		}
		PageSyncValidationResult content = ValidateBody(page.Body, page.Resources);
		IReadOnlyList<string> contentErrors = content.Errors ?? Array.Empty<string>();
		return contentErrors.Count == 0
			? syntaxMessage
			: "Client-side validation failed: " + string.Join("; ", contentErrors);
	}

	private static PageSyncPageResult BuildPrePassFailureResult(PageSyncPageInput page, string failureMessage) =>
		new() {
			SchemaName = page.SchemaName,
			Success = false,
			Error = failureMessage,
			Validation = new PageSyncValidationResult {
				MarkersOk = false,
				JsSyntaxOk = false,
				ContentOk = false,
				Errors = [failureMessage]
			}
		};

	// Runs the regex content-validation chain (when `validate` is true) and
	// the AST lint Error check on a single web page WITHOUT touching the
	// environment / network / lock. Returns the materialised failure if any
	// deterministic check failed, otherwise null. Mobile bodies short-circuit
	// here (they are validated inside the lock by MobilePageValidation).
	// Precedence on overlap: regex wins over lint — its wording is what
	// existing tests and operator habits depend on.
	// Registry-driven chart-widget required-field check, materialised into the same per-page failure
	// shape as TryMaterialiseDeterministicFailure. Web-only (mobile bodies carry no chart widgets) and
	// fail-open when the registry was unavailable (chartTypeDefinitions == null). The type definitions
	// are resolved once on the async entry (SyncPages) so this stays a synchronous per-page check.
	private static PageSyncPageResult TryMaterialiseChartWidgetFailure(
		PageSyncPageInput page,
		bool validate,
		IReadOnlyDictionary<string, System.Text.Json.JsonElement>? chartTypeDefinitions) {
		if (!validate || chartTypeDefinitions is null ||
		    PageSchemaTypeExtensions.FromBody(page.Body) == PageSchemaType.Mobile) {
			return null;
		}
		SchemaValidationResult chartResult =
			SchemaValidationService.ValidateChartWidgetConfig(page.Body, chartTypeDefinitions);
		if (chartResult.IsValid) {
			return null;
		}
		return new PageSyncPageResult {
			SchemaName = page.SchemaName,
			Success = false,
			Validation = new PageSyncValidationResult {
				MarkersOk = true,
				JsSyntaxOk = true,
				ContentOk = false,
				Errors = chartResult.Errors
			},
			Error = "Client-side validation failed: " + string.Join("; ", chartResult.Errors)
		};
	}

	private static PageSyncPageResult TryMaterialiseDeterministicFailure(
		PageSyncPageInput page,
		PageSyncPrePassEntry entry,
		bool validate) {
		if (PageSchemaTypeExtensions.FromBody(page.Body) == PageSchemaType.Mobile) {
			return null;
		}
		if (validate) {
			PageSyncValidationResult validationResult = ValidateBody(page.Body, page.Resources);
			if (!validationResult.MarkersOk || !validationResult.JsSyntaxOk || !validationResult.ContentOk) {
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					Error = "Client-side validation failed: " +
						string.Join("; ", validationResult.Errors ?? Array.Empty<string>())
				};
			}
		}
		IReadOnlyList<PageBodyLintFinding> lintErrors = entry.LintFindings
			.Where(f => f.Severity == LintSeverity.Error)
			.ToArray();
		if (lintErrors.Count == 0) {
			return null;
		}
		string lintError = PageBodyAstLinter.FormatErrors(lintErrors);
		return new PageSyncPageResult {
			SchemaName = page.SchemaName,
			Success = false,
			Validation = new PageSyncValidationResult {
				// JsSyntaxOk = true because the parser produced an AST (otherwise
				// the syntax pre-pass would have short-circuited before this).
				// MarkersOk reflects whether the regex chain actually ran:
				//   - validate=true → regex ran and passed (we only reach lint
				//     Error materialisation when regex returned a clean result)
				//   - validate=false → regex never ran, so we cannot claim the
				//     markers passed; leave it false rather than fabricate true.
				MarkersOk = validate,
				JsSyntaxOk = true,
				ContentOk = false,
				Errors = [lintError]
			},
			Error = lintError
		};
	}

	private PageSyncPageResult ProcessPendingPage(PageSyncPageInput page, int index, PageSyncBatchContext ctx) {
		PageSyncPrePassEntry prePassEntry = ctx.PrePass.Entries[index];
		PageSamplingReview samplingReview = ctx.SamplingResults[index];
		PageSyncOperationOptions opOptions = new(
			ctx.UpdateCommand,
			ctx.GetCommand,
			ctx.Validate,
			ctx.Verify,
			samplingReview,
			ctx.OutputDirectory,
			prePassEntry.LintFindings) { EnvironmentName = ctx.EnvironmentName };
		return SyncSinglePage(page, opOptions);
	}

	private sealed record PageSyncBatchContext(
		PageUpdateCommand UpdateCommand,
		PageGetCommand GetCommand,
		bool Validate,
		bool Verify,
		string? OutputDirectory,
		PageSyncPrePassResults PrePass,
		IReadOnlyList<PageSamplingReview?> SamplingResults) {
		// Environment identity for the conflict-baseline guard. Init-only property (not a
		// positional parameter) to keep the primary constructor under Sonar S107's limit.
		public string? EnvironmentName { get; init; }
	}

	private sealed record PageSyncPrePassResults(IReadOnlyList<PageSyncPrePassEntry> Entries);

	private sealed record PageSyncPrePassEntry(
		string? SyntaxFailureMessage,
		IReadOnlyList<PageBodyLintFinding> LintFindings) {
		public static readonly PageSyncPrePassEntry Empty = new(null, Array.Empty<PageBodyLintFinding>());

		// "Doomed" = sampling has no value because the body will be rejected
		// downstream regardless of the LLM verdict. Either a parse failure
		// (no AST) or any Error-severity lint finding qualifies.
		public bool IsBodyDoomed =>
			SyntaxFailureMessage != null
			|| LintFindings.Any(f => f.Severity == LintSeverity.Error);
	}

	// Per-page execution options for SyncSinglePage. Bundled into a record so
	// the method stays under Sonar S107's 7-parameter limit. The lint pass
	// itself runs once in the pre-pass (see BuildPrePassEntry); the full
	// findings list is carried per-page so that:
	//   - Error-severity findings can be materialised as a fail-fast result
	//     IF the regex content validator did not already reject the same
	//     issue with its own (authoritative) wording.
	//   - Warning-severity findings can be appended to the final validation
	//     result alongside the regex warnings.
	private sealed record PageSyncOperationOptions(
		PageUpdateCommand UpdateCommand,
		PageGetCommand GetCommand,
		bool Validate,
		bool Verify,
		PageSamplingReview SamplingReview,
		string? OutputDirectory,
		IReadOnlyList<PageBodyLintFinding> LintFindings) {
		// Environment identity for the conflict-baseline guard — see PageSyncBatchContext.
		public string? EnvironmentName { get; init; }
	}

	private PageSyncPageResult TryValidatePage(
		PageSyncPageInput page,
		PageSamplingReview samplingReview,
		out PageSyncValidationResult validationResult) {
		validationResult = null;
		if (PageSchemaTypeExtensions.FromBody(page.Body) == PageSchemaType.Mobile) {
			// PageSyncTool runs synchronously inside a McpToolExecutionLock; the MCP
			// server has no SynchronizationContext, so a sync-over-async wait on the
			// async catalog API is deadlock-free. Master's ENG-89649 also added the
			// `explicitResources` parameter for resource-binding validation, plumbed
			// through here.
			SchemaValidationService.TryParseResources(page.Resources, out Dictionary<string, string>? mobileResources, out _);
			validationResult = MobilePageValidation
				.RunAsync(page.Body, mobileComponentCatalog, webComponentCatalog, mobileResources)
				.GetAwaiter().GetResult();
			if (!validationResult.ContentOk)
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					SamplingReview = samplingReview,
					Error = "Mobile page validation failed: " + string.Join("; ", validationResult.Errors ?? [])
				};
		} else {
			validationResult = ValidateBody(page.Body, page.Resources);
			if (!validationResult.MarkersOk || !validationResult.JsSyntaxOk || !validationResult.ContentOk)
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					SamplingReview = samplingReview,
					Error = "Client-side validation failed: " +
						string.Join("; ", validationResult.Errors ?? Array.Empty<string>())
				};
		}
		return null;
	}

	private PageSyncPageResult SyncSinglePage(PageSyncPageInput page, PageSyncOperationOptions opOptions) {
		try {
			// Pages reaching SyncSinglePage already passed every deterministic
			// gate (syntax, regex, lint Errors) via ExecuteSyncBatch /
			// TryMaterialiseDeterministicFailure. The only validation work here
			// is the mobile-side async validator (web bodies already cleared
			// regex upstream) plus appending lint Warnings to the final
			// validation envelope.
			//
			// Nit (followup): for the surviving web pages this runs the regex
			// content chain a second time inside TryValidatePage so its
			// Warnings list can be captured into the per-page Validation
			// envelope. The duplicate work is pure-text and fast (~1-5 ms per
			// page), and the alternative — caching the result of the
			// deterministic-pass regex run on PageSyncPrePassEntry — adds a
			// new field that only the warnings path reads. A future cleanup
			// could thread the cached PageSyncValidationResult through
			// PageSyncOperationOptions and skip the second run.
			PageSyncValidationResult validationResult = null;
			if (opOptions.Validate) {
				PageSyncPageResult validationFailure = TryValidatePage(page, opOptions.SamplingReview, out validationResult);
				if (validationFailure != null)
					return validationFailure;
			}
			validationResult = AppendCommandWarnings(validationResult, GetLintWarningMessages(opOptions.LintFindings));
			PageSyncPageResult samplingFailure = CreateSamplingFailure(page, opOptions.SamplingReview, validationResult);
			if (samplingFailure != null)
				return samplingFailure;
			(string metaFilePath, bool baselineArmed, PageUpdateOptions updateOptions) =
				BuildUpdateRequest(page, opOptions);
			opOptions.UpdateCommand.TryUpdatePage(updateOptions, out PageUpdateResponse updateResponse);
			if (!updateResponse.Success) {
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					Error = updateResponse.Error,
					Conflict = updateResponse.Conflict,
					ConflictDetails = updateResponse.ConflictDetails
				};
			}
			validationResult = AppendCommandWarnings(validationResult, updateResponse.Warnings);
			if (opOptions.Verify && opOptions.GetCommand != null)
				return VerifySavedPage(page, opOptions, updateResponse, validationResult);
			if (baselineArmed) {
				pageBaselineGuard.RefreshOrDrop(metaFilePath, updateOptions, updateResponse);
			}
			return new PageSyncPageResult {
				SchemaName = page.SchemaName,
				Success = true,
				BodyLength = updateResponse.BodyLength,
				Validation = validationResult,
				SamplingReview = opOptions.SamplingReview,
				ResourcesRegistered = updateResponse.ResourcesRegistered
			};
		} catch (Exception ex) {
			return new PageSyncPageResult {
				SchemaName = page.SchemaName,
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message)
			};
		}
	}

	private PageSyncPageResult CreateSamplingFailure(
		PageSyncPageInput page,
		PageSamplingReview samplingReview,
		PageSyncValidationResult validationResult) {
		if (samplingReview is not { Ok: false, Skipped: false } || samplingReview.Issues?.Count <= 0) {
			return null;
		}
		return new PageSyncPageResult {
			SchemaName = page.SchemaName,
			Success = false,
			Validation = validationResult,
			SamplingReview = samplingReview,
			Error = "Sampling review found issues: " + string.Join("; ", samplingReview.Issues)
				+ ". Fix the page body and resubmit. Do NOT retry the same body with skip-sampling=true to bypass this check."
		};
	}

	private (string MetaFilePath, bool BaselineArmed, PageUpdateOptions UpdateOptions) BuildUpdateRequest(
		PageSyncPageInput page,
		PageSyncOperationOptions opOptions) {
		// sync-pages only ever knows the environment name (no per-page URI); set it on the options so
		// the shared guard's environment-identity check compares against EnvironmentName, matching the
		// prior MatchesEnvironment(baseline, opOptions.EnvironmentName, null) call.
		PageUpdateOptions updateOptions = new() {
			SchemaName = page.SchemaName,
			Body = page.Body,
			DryRun = false,
			Resources = page.Resources,
			OptionalProperties = page.OptionalProperties,
			Environment = opOptions.EnvironmentName,
			Force = page.Force ?? false,
			NotifyDesignerPresence = false
		};
		(string metaFilePath, bool baselineArmed) =
			pageBaselineGuard.TryArm(updateOptions, opOptions.OutputDirectory);
		return (metaFilePath, baselineArmed, updateOptions);
	}

	private PageSyncPageResult VerifySavedPage(
		PageSyncPageInput page,
		PageSyncOperationOptions opOptions,
		PageUpdateResponse updateResponse,
		PageSyncValidationResult validationResult) {
		PageGetOptions getOptions = new() { SchemaName = page.SchemaName };
		opOptions.GetCommand.TryGetPage(getOptions, out PageGetResponse getResponse);
		if (!getResponse.Success) {
			return new PageSyncPageResult {
				SchemaName = page.SchemaName,
				Success = false,
				BodyLength = updateResponse.BodyLength,
				Validation = validationResult,
				Error = $"Page saved but verification failed: {getResponse.Error}"
			};
		}
		string? verifiedBodyFile = WriteVerifiedBodyFile(page.SchemaName, opOptions.OutputDirectory, getResponse.Raw?.Body);
		if (verifiedBodyFile != null) {
			WriteFreshMetaAfterVerify(
				fileSystem.Path.GetDirectoryName(verifiedBodyFile),
				page.SchemaName,
				opOptions.EnvironmentName,
				getResponse);
		}
		return new PageSyncPageResult {
			SchemaName = page.SchemaName,
			Success = true,
			BodyLength = updateResponse.BodyLength,
			Validation = validationResult,
			SamplingReview = opOptions.SamplingReview,
			ResourcesRegistered = updateResponse.ResourcesRegistered,
			Page = getResponse.Page,
			VerifiedBodyFile = verifiedBodyFile
		};
	}

	private string? WriteVerifiedBodyFile(string schemaName, string? outputDirectory, string body) {
		if (body is null) {
			return null;
		}
		// H1: reading the process-global cwd to anchor output must serialize against the workspace tools
		// that PIN cwd, else a concurrent tenant's cwd pin could place this page under the wrong root.
		// This runs while ExecuteSyncBatch holds the per-tenant lock, so the ordering is per-tenant →
		// CwdLock (never the reverse) — no deadlock.
		string anchor;
		lock (McpToolExecutionLock.CwdLock) {
			anchor = PageOutputDirectoryResolver.ResolveAnchor(
				fileSystem,
				fileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				outputDirectory);
		}
		string schemaDir = fileSystem.Path.Combine(anchor, ".clio-pages", schemaName);
		fileSystem.Directory.CreateDirectory(schemaDir);
		string bodyFile = fileSystem.Path.Combine(schemaDir, "body.js");
		fileSystem.File.WriteAllText(bodyFile, body);
		return bodyFile;
	}

	// FR-13: the verify read-back already rewrites body.js; without also rewriting meta.json the
	// stored baseline would keep the PRE-save checksum and the next write would false-conflict.
	// The fresh meta carries the page metadata and the baseline captured by the verify get-page.
	private void WriteFreshMetaAfterVerify(
		string schemaDir, string schemaName, string? environmentName, PageGetResponse getResponse) {
		try {
			string fetchedAt = DateTime.UtcNow.ToString("o");
			string metaFile = fileSystem.Path.Combine(schemaDir, "meta.json");
			PageBaselineInfo baseline = BuildBaseline(schemaName, environmentName, getResponse.Editable, fetchedAt);
			// Preserve the environment identity (e.g. a URI-mode EnvironmentUri) captured by a prior
			// write so the verify rewrite stays byte-compatible with the update-page baseline and does
			// not silently disarm conflict detection for a later URI-mode update-page.
			baseline = PageBaselineStore.MergeEnvironmentIdentity(
				baseline, PageBaselineStore.TryReadBaseline(fileSystem, metaFile));
			fileSystem.File.WriteAllText(metaFile, System.Text.Json.JsonSerializer.Serialize(new PageMetaFileModel {
				FetchedAt = fetchedAt,
				Page = getResponse.Page,
				Baseline = baseline
			}));
		} catch {
			// best-effort — meta refresh must never fail a verified save.
		}
	}

	private static PageBaselineInfo BuildBaseline(
		string schemaName,
		string? environmentName,
		PageEditableSchemaInfo editableSchema,
		string capturedAt) {
		if (editableSchema is null) {
			return null;
		}
		return new PageBaselineInfo {
			SchemaName = schemaName,
			EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName,
			EditableSchemaExists = editableSchema.EditableSchemaExists,
			EditableSchemaUId = editableSchema.EditableSchemaUId,
			Checksum = editableSchema.Checksum,
			ModifiedOn = editableSchema.ModifiedOn,
			CapturedAt = capturedAt
		};
	}

	private static IReadOnlyList<string> GetLintWarningMessages(IReadOnlyList<PageBodyLintFinding> findings) =>
		findings
			.Where(f => f.Severity == LintSeverity.Warning)
			.Select(PageBodyAstLinter.FormatFinding)
			.ToArray();

	private static PageSyncValidationResult ValidateBody(string body, string? resources) {
		SchemaValidationResult markerResult = SchemaValidationService.ValidateMarkerIntegrity(body);
		// The legacy brace-counter ValidateJsSyntax is intentionally NOT
		// called here. Every web body reaching this method already parsed
		// through Acornima in BuildPrePassEntry — the deterministic syntax
		// gate is the active source of truth for JS syntax on this path,
		// and the brace counter can no longer fail (its only possible
		// rejections — empty body, unbalanced braces — are caught upstream
		// by the syntax gate or by the markers validator). Same justification
		// as PageValidateTool.Validate (see comment at its post-Acornima
		// branch). JsSyntaxOk is reported true unconditionally in
		// BuildValidationResult below.
		SchemaValidationResult contentResult = GetContentValidationResult(body, markerResult);
		Dictionary<string, string>? explicitResources = TryParseExplicitResources(resources, contentResult);
		SchemaValidationResult fieldResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateStandardFieldBindings(body, explicitResources));
		SchemaValidationResult insertSelfConsistencyResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateInsertedFieldSelfConsistency(body, explicitResources));
		SchemaValidationResult widgetCaptionResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateInsertedWidgetCaptionResources(body, explicitResources));
		SchemaValidationResult localizableTextResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateLocalizableTextLiterals(body));
		SchemaValidationResult handlerResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateHandlerStructure(body));
		SchemaValidationResult validatorBindingResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateValidatorControlBindings(body));
		SchemaValidationResult validatorPlacementResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateValidatorBindingPlacement(body));
		SchemaValidationResult validatorBindingShapeResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateValidatorBindingShape(body));
		SchemaValidationResult validatorParamResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateValidatorParamResourceBindings(body));
		SchemaValidationResult standardValidatorResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateStandardValidatorUsage(body));
		SchemaValidationResult validatorParamCompletenessResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateCustomValidatorParamCompleteness(body));
		SchemaValidationResult validatorFactoryShapeResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateCustomValidatorFactoryShape(body));
		SchemaValidationResult converterDeclResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateConverterDeclarations(body));
		SchemaValidationResult converterFunctionShapeResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateConverterFunctionShape(body));
		SchemaValidationResult validatorDeclResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateValidatorDeclarations(body));
		SchemaValidationResult bindingResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateColumnBindings(body));
		SchemaValidationResult schemaDepsResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateSchemaDepsCompleteness(body));
		SchemaValidationResult contextAwaitResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateContextAccessAwait(body));
		List<string> errors = CollectErrors(
			markerResult,
			contentResult,
			fieldResult,
			insertSelfConsistencyResult,
			localizableTextResult,
			handlerResult,
			validatorBindingResult,
			validatorPlacementResult,
			validatorBindingShapeResult,
			validatorParamResult,
			standardValidatorResult,
			validatorParamCompletenessResult,
			validatorFactoryShapeResult,
			converterDeclResult,
			converterFunctionShapeResult,
			validatorDeclResult);
		List<string> warnings = CollectWarnings(fieldResult, bindingResult, schemaDepsResult, contextAwaitResult);
		// Widget-caption resolvability is a body-only PRE-FLIGHT heuristic here (the pre-flight has no schema
		// context); surface it as a warning. The authoritative hard gate runs at save time via TryUpdatePage.
		if (!widgetCaptionResult.IsValid) {
			warnings.AddRange(widgetCaptionResult.Errors);
		}
		bool contentOk = IsContentValidationSuccessful(
			contentResult,
			fieldResult,
			insertSelfConsistencyResult,
			localizableTextResult,
			handlerResult,
			validatorBindingResult,
			validatorPlacementResult,
			validatorBindingShapeResult,
			validatorParamResult,
			standardValidatorResult,
			validatorParamCompletenessResult,
			validatorFactoryShapeResult,
			converterDeclResult,
			converterFunctionShapeResult,
			validatorDeclResult);
		return BuildValidationResult(markerResult, contentOk, errors, warnings);
	}

	private static SchemaValidationResult GetContentValidationResult(
		string body,
		SchemaValidationResult markerResult) =>
		markerResult.IsValid
			? SchemaValidationService.ValidateMarkerContent(body)
			: new SchemaValidationResult { IsValid = true };

	private static Dictionary<string, string>? TryParseExplicitResources(
		string? resources,
		SchemaValidationResult contentResult) {
		if (!contentResult.IsValid) {
			return null;
		}

		if (SchemaValidationService.TryParseResources(resources, out Dictionary<string, string>? explicitResources, out _)) {
			return explicitResources;
		}

		contentResult.IsValid = false;
		contentResult.Errors.Add("resources must be a valid JSON object string");
		return null;
	}

	private static SchemaValidationResult RunContentValidation(
		SchemaValidationResult contentResult,
		Func<SchemaValidationResult> validation) =>
		contentResult.IsValid
			? validation()
			: new SchemaValidationResult { IsValid = true };

	private static bool IsContentValidationSuccessful(params SchemaValidationResult[] results) =>
		results.All(result => result.IsValid);

	private static List<string> CollectErrors(params SchemaValidationResult[] results) {
		var errors = new List<string>();
		foreach (SchemaValidationResult result in results) {
			if (!result.IsValid) {
				errors.AddRange(result.Errors);
			}
		}

		return errors;
	}

	private static List<string> CollectWarnings(
		SchemaValidationResult fieldResult,
		SchemaValidationResult bindingResult,
		SchemaValidationResult schemaDepsResult,
		SchemaValidationResult contextAwaitResult) {
		var warnings = new List<string>();
		if (fieldResult.Warnings.Count > 0) {
			warnings.AddRange(fieldResult.Warnings);
		}

		if (!bindingResult.IsValid) {
			warnings.AddRange(bindingResult.Errors);
		}

		if (schemaDepsResult.Warnings.Count > 0) {
			warnings.AddRange(schemaDepsResult.Warnings);
		}

		if (contextAwaitResult.Warnings.Count > 0) {
			warnings.AddRange(contextAwaitResult.Warnings);
		}

		return warnings;
	}

	private static PageSyncValidationResult AppendCommandWarnings(
		PageSyncValidationResult validation, IReadOnlyList<string> commandWarnings) {
		if (commandWarnings == null || commandWarnings.Count == 0) {
			return validation;
		}
		var warnings = new List<string>();
		if (validation?.Warnings != null) {
			warnings.AddRange(validation.Warnings);
		}
		warnings.AddRange(commandWarnings);
		return new PageSyncValidationResult {
			MarkersOk = validation?.MarkersOk ?? true,
			JsSyntaxOk = validation?.JsSyntaxOk ?? true,
			ContentOk = validation?.ContentOk ?? true,
			Errors = validation?.Errors,
			Warnings = warnings
		};
	}

	private static PageSyncValidationResult BuildValidationResult(
		SchemaValidationResult markerResult,
		bool contentOk,
		List<string> errors,
		List<string> warnings) =>
		new() {
			MarkersOk = markerResult.IsValid,
			// Acornima already parsed the body successfully in BuildPrePassEntry
			// before this method runs, so JS syntax is true unconditionally on
			// this path. The dead brace-counter ValidateJsSyntax call was
			// removed (mirrors PageValidateTool.BuildResult).
			JsSyntaxOk = true,
			ContentOk = contentOk,
			Errors = errors.Count > 0 ? errors : null,
			Warnings = warnings.Count > 0 ? warnings : null
		};
}

/// <summary>
/// Top-level arguments for the <c>sync-pages</c> MCP tool.
/// </summary>
public sealed record PageSyncArgs(
	// FR-05a: conditionally required — forbidden under authorized HTTP credential passthrough (the header
	// carries the tenant), required/resolvable otherwise via ToolCommandResolver.ResolveSettingsAndKey's
	// existing EnvironmentResolutionException throw. [Required] is intentionally NOT applied here so a
	// header-only passthrough call is not rejected at pre-tool MCP schema binding (mirrors PageUpdateArgs).
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName,

	[property: JsonPropertyName("pages")]
	[property: Description("Pages to update")]
	[property: Required]
	IEnumerable<PageSyncPageInput> Pages,

	[property: JsonPropertyName("validate")]
	[property: Description("Toggle for the regex content-validation chain (markers, field bindings, validator/converter/handler shape, etc.). Default: true. The deterministic JavaScript syntax parser and the AST lint pass ALWAYS run regardless of this flag — they enforce the page-loadability floor and the platform-rejected anti-patterns the regex layer cannot express, so an opt-out for those is intentionally not provided.")]
	bool? Validate = null,

	[property: JsonPropertyName("verify")]
	[property: Description("Read back each page after saving to confirm the update. Default: false")]
	bool? Verify = null,

	[property: JsonPropertyName("skip-sampling")]
	[property: Description("Reserved escape hatch. Omit by default. Pre-condition for setting true: the immediately preceding user message in this turn contains an explicit instruction to skip the AI semantic review for this batch, OR the MCP host has reported sampling as unavailable in this session. Absent that evidence, omit this field. Default: false")]
	bool? SkipSampling = null,

	[property: JsonPropertyName("output-directory")]
	[property: Description("Optional. Directory to anchor verified-page .clio-pages output under — typically your project/workspace root. When omitted, the workspace root is auto-detected by walking up for .clio/workspaceSettings.json; if running from the home directory with no workspace found, output falls back to the clio home root rather than $HOME. Only relevant when verify=true.")]
	string? OutputDirectory = null
);

/// <summary>
/// A single page input for the <c>sync-pages</c> tool.
/// </summary>
public sealed record PageSyncPageInput(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("body")]
	[property: Description("Full JavaScript page body — read from body.js returned by get-page")]
	[property: Required]
	string Body,

	[property: JsonPropertyName("resources")]
	[property: Description(McpToolDescriptions.PageResources)]
	string? Resources = null,
	[property: JsonPropertyName("optional-properties")]
	[property: Description("JSON array of {key, value} objects to merge into schema optionalProperties")]
	string? OptionalProperties = null,
	[property: JsonPropertyName("force")]
	[property: Description("Skip the external-modification (checksum) conflict check for THIS page and deliberately overwrite out-of-band changes. Set true ONLY after the user explicitly confirms overwriting changes made outside this session. Default: false")]
	bool? Force = null
);

/// <summary>
/// Response from the <c>sync-pages</c> MCP tool.
/// </summary>
public sealed class PageSyncResponse {

	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("pages")]
	public IReadOnlyList<PageSyncPageResult> Pages { get; init; } = [];
}

/// <summary>
/// Result for a single page in a <c>sync-pages</c> response.
/// </summary>
public sealed class PageSyncPageResult {

	[JsonPropertyName("schema-name")]
	public string SchemaName { get; init; }

	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("body-length")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int BodyLength { get; init; }

	[JsonPropertyName("validation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageSyncValidationResult Validation { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	[JsonPropertyName("resources-registered")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int ResourcesRegistered { get; init; }

	[JsonPropertyName("page")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageMetadataInfo Page { get; init; }

	[JsonPropertyName("sampling-review")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageSamplingReview SamplingReview { get; init; }

	[JsonPropertyName("verified-body-file")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string VerifiedBodyFile { get; init; }

	/// <summary>
	/// <c>true</c> when this page's save was blocked because the schema was modified outside the
	/// current agent session (external-modification conflict). Other pages in the batch are
	/// unaffected.
	/// </summary>
	[JsonPropertyName("conflict")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Conflict { get; init; }

	/// <summary>Conflict details when <see cref="Conflict"/> is <c>true</c>.</summary>
	[JsonPropertyName("conflict-details")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageConflictDetails ConflictDetails { get; init; }
}

/// <summary>
/// Client-side validation result for a page body.
/// </summary>
public sealed class PageSyncValidationResult {

	[JsonPropertyName("markers-ok")]
	public bool MarkersOk { get; init; }

	[JsonPropertyName("js-syntax-ok")]
	public bool JsSyntaxOk { get; init; }

	[JsonPropertyName("content-ok")]
	public bool ContentOk { get; init; }

	[JsonPropertyName("errors")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Errors { get; init; }

	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }
}
