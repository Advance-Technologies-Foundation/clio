using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Acornima.Ast;
using Clio.Common;
using McpServerLib = ModelContextProtocol.Server;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageUpdateTool(
	PageUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IMobileComponentInfoCatalog mobileComponentCatalog,
	IComponentInfoCatalog webComponentCatalog,
	IPageBodySamplingService samplingService,
	IPageBaselineGuard pageBaselineGuard,
	IGuidanceAccessLedger guidanceAccessLedger,
	IPageLayoutCompositionDetector layoutCompositionDetector)
	: BaseTool<PageUpdateOptions>(command, logger, commandResolver) {

	private readonly IToolCommandResolver _commandResolver = commandResolver;
	private readonly IPageBodySamplingService _samplingService = samplingService;
	private readonly IGuidanceAccessLedger _guidanceAccessLedger = guidanceAccessLedger;
	private readonly IPageLayoutCompositionDetector _layoutCompositionDetector = layoutCompositionDetector;

	internal const string ToolName = "update-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Update Freedom UI page schema body. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows. " +
		"On successful non-dry-run saves, update-page also attempts a best-effort live Designer Presence `save` notification so active Creatio designers can be warned about outdated pages. This live notification requires forms-auth browser-session cookies (login/password-backed flow); in OAuth-only or credential-less environments the page save still succeeds and the response carries a warning when the live notification is skipped or fails. " +
		"CONFLICT DETECTION: when get-page previously stored a checksum baseline in .clio-pages/{schema}/meta.json for the same environment, this tool blocks the save with `conflict: true` + `conflictDetails` if the schema was modified outside this session (e.g. the user edited the page in the Creatio designer). On a conflict: do NOT retry with the same body — re-run get-page, re-apply your change on top of the fresh body, then retry; inform the user about the external changes and set force=true ONLY after they explicitly confirm overwriting them. A small race window between the check and the save remains (last write wins). " +
		"Before editing page bodies or resource payloads, call get-guidance with name `page-modification` and use its pre-edit checklist to select specialized page-authoring guides. " +
			"For conditional visibility, editability, required state based on field values or conditional set and clear value. Also filtering of lookups, based on condition or value from other field (e.g. \"when Status=Closed, hide Description\"), OR for writing/clearing column values when another field changes (e.g. \"when Type=Personal, clear Company\"; \"when Country=USA, set Currency=USD\"; two interdependent fields where changing one auto-fills or wipes the other), use business rules instead of writing handlers or validators in page body \u2014 business rules can both populate AND clear columns via the `set-values` action; call get-guidance with name `business-rules` to learn more. " +
			"To restrict / filter which records a lookup or ComboBox field offers (e.g. \"show only contacts who\u2026\", \"limit the Assignee field to\u2026\", \"only accounts that have\u2026\"), do NOT add filterConfig / staticFilters / dataSourceFilters to a datasource list attribute here \u2014 use create-entity-business-rule with apply-static-filter (call get-guidance with name `business-rules`). " +
			"This holds for ANY constraint mechanism \u2014 an attribute value, a now-relative period (date macro), a fixed calendar/clock part such as a time of day (datePart), the existence or count of related child records, or a constraint gated by another field's value \u2014 classify the mechanism, not the wording; all are apply-static-filter, never a handler or crt.InitRequest. A gated constraint puts the gate (X = Y) into the rule's condition group and the apply-static-filter action on the target lookup. " +
		"Section authoring rules for the body payload: " +
		"if the requirement involves display-only value transformation (email as mailto link, phone as tel link, text to uppercase, boolean inversion, number formatting, any value that should look different on screen without changing the underlying model) call get-guidance with name `page-schema-converters` first — this determines whether a converter is the right tool before choosing a component type; " +
		"if the body changes SCHEMA_HANDLERS call get-guidance with name `page-schema-handlers` first — NOTE: restricting which records a lookup/ComboBox offers is NEVER handler business logic, regardless of the constraint mechanism (attribute value, relative period, fixed time-of-day, child existence/count, or gating by another field); it is an entity business rule (apply-static-filter), so use create-entity-business-rule, not crt.InitRequest; " +
		"if the body changes SCHEMA_VALIDATORS call get-guidance with name `page-schema-validators` first; " +
		"if the body changes SCHEMA_CONVERTERS call get-guidance with name `page-schema-converters` first; " +
		"if the body adds or edits `@creatio-devkit/common` usage call get-guidance with name `page-schema-creatio-devkit-common` before editing SCHEMA_DEPS or SDK calls; " +
		"if the body contains `$Resources.Strings.*` or `#ResourceString(...)#`, or you plan to pass the `resources` parameter, call get-guidance with name `page-schema-resources` first — do NOT register localizable strings until this guidance tells you to do so. " +
		"if the body adds a button that runs a business process (a `clicked` bound to " +
			"`crt.RunBusinessProcessRequest`), call get-guidance with name `run-process-button` and resolve " +
			"the process with get-process-signature FIRST — parameter keys must be the process parameter " +
			"CODE (not caption); update-page validates the codes against the live signature and rejects " +
			"unknown ones. " +
			"if the body adds or lays out components — designing or laying out the page UI/UX (choosing a component for a concept, placing or ordering fields, grid columns/colSpan, container nesting, grouping into tabs/groups, captions/tooltips/placeholders) — call get-guidance with name `ui-guidelines` first (it routes to `ui-page-layout`, `ui-accessibility`, `ui-review-checklists`); author from it, not from memory, and match the existing page style (read the page with get-page first). " +
			"LAYOUT-GUIDANCE GATE: a body that adds or lays out components (a crt.* `insert` in viewConfigDiff) is REJECTED unless get-guidance name=`ui-page-layout` was already called this session — the `ui-guidelines` index alone does NOT satisfy the gate; fetch the `ui-page-layout` leaf. force=true overrides the gate. " +
			"INSERTED-FIELD CONTRACT: " + SchemaValidationService.InsertedFieldContractSummary)]
	public async Task<PageUpdateResponse> UpdatePage(
		[Description("Parameters: schema-name, body (required); resources, dry-run (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] PageUpdateArgs args,
		McpServerLib.McpServer server,
		CancellationToken cancellationToken = default) {
		PageUpdateOptions options = BuildOptions(args);
		(PageUpdateResponse earlyFailure,
			IReadOnlyList<string> validationWarnings,
			IReadOnlyList<string> lintWarnings,
			PageSamplingReview samplingReview) = await TryCreatePreExecutionFailureAsync(
				options,
				args,
				server,
				cancellationToken);
		if (earlyFailure != null)
			return earlyFailure;
		(string metaFilePath, bool baselineArmed) = pageBaselineGuard.TryArm(options, args.OutputDirectory);
		PageUpdateResponse response = ExecuteWithCleanLog(() => {
			PageUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageUpdateCommand>(options);
			} catch (Exception ex) {
				return new PageUpdateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryUpdatePage(options, out PageUpdateResponse inner);
			if (inner.Success && args.Verify == true)
				TryVerifyPage(args, inner);
			return inner;
		});
		if (baselineArmed && response.Success && !options.DryRun)
			pageBaselineGuard.RefreshOrDrop(metaFilePath, options, response);
		response.SamplingReview = samplingReview;
		IReadOnlyList<string> mergedWarnings = MergeWarnings(
			MergeWarnings(validationWarnings, response.Warnings),
			lintWarnings);
		response.Warnings = mergedWarnings.Count > 0 ? mergedWarnings : null;
		return response;
	}

	private async Task<(PageUpdateResponse Failure,
		IReadOnlyList<string> ValidationWarnings,
		IReadOnlyList<string> LintWarnings,
		PageSamplingReview SamplingReview)> TryCreatePreExecutionFailureAsync(
			PageUpdateOptions options,
			PageUpdateArgs args,
			McpServerLib.McpServer server,
			CancellationToken cancellationToken) {
		(bool bodyLoaded, string bodyLoadError) = PageUpdateBodyLoader.TryLoadBodyFromFile(options);
		if (!bodyLoaded)
			return (new PageUpdateResponse { Success = false, Error = bodyLoadError }, null, null, null);
		if (string.IsNullOrWhiteSpace(options.Body)) {
			return (new PageUpdateResponse {
				Success = false,
				Error = "Either 'body' or 'body-file' must provide page body content."
			}, null, null, null);
		}
		PageUpdateResponse syntaxFailure = TryValidateBodySyntax(options, out Script parsedAst);
		if (syntaxFailure != null)
			return (syntaxFailure, null, null, null);
		(PageUpdateResponse validationFailure, IReadOnlyList<string> validationWarnings) = ValidateBody(options);
		if (validationFailure != null)
			return (validationFailure, null, null, null);
		(PageUpdateResponse runProcessFailure, IReadOnlyList<string> runProcessWarnings) =
			ValidateRunProcessButtons(options);
		if (runProcessFailure != null)
			return (runProcessFailure, null, null, null);
		validationWarnings = MergeWarnings(validationWarnings, runProcessWarnings);
		(PageUpdateResponse lintFailure, IReadOnlyList<string> lintWarnings) = RunAstLintPass(parsedAst);
		if (lintFailure != null)
			return (lintFailure, null, null, null);
		(PageUpdateResponse samplingFailure, PageSamplingReview samplingReview) =
			await TryRunSamplingAsync(options, args, server, cancellationToken);
		if (samplingFailure != null)
			return (samplingFailure, validationWarnings, lintWarnings, samplingReview);
		// LAST pre-execution check (after every body/syntax/sampling validation, so genuine body
		// errors surface first): block layout composition authored without first reading the
		// ui-page-layout guidance leaf. Force overrides, mirroring the checksum-conflict override.
		PageUpdateResponse layoutGuidanceFailure = TryCreateLayoutGuidanceFailure(options);
		if (layoutGuidanceFailure != null)
			return (layoutGuidanceFailure, validationWarnings, lintWarnings, samplingReview);
		return (null, validationWarnings, lintWarnings, samplingReview);
	}

	// Fail-closed layout-guidance gate: a body that adds/lays out crt.* view components must be
	// authored from the ui-page-layout guidance leaf (the article carrying the layout mechanics),
	// NOT from memory. Satisfied ONLY by ui-page-layout — the thin ui-guidelines index is not
	// enough, because reading only the index is exactly the failure this gate fixes. Reading force
	// the same way TryCheckForExternalModification reads it (options.Force) keeps the override
	// semantics consistent with the checksum-conflict path.
	private PageUpdateResponse TryCreateLayoutGuidanceFailure(PageUpdateOptions options) {
		if (options.Force) {
			return null;
		}
		if (!_layoutCompositionDetector.BodyAddsOrLaysOutComponents(options.Body)) {
			return null;
		}
		if (_guidanceAccessLedger.WasFetched(PageLayoutGuidanceGate.RequiredGuidanceName)) {
			return null;
		}
		return new PageUpdateResponse {
			Success = false,
			Error = PageLayoutGuidanceGate.RejectionMessage
		};
	}

	private static PageUpdateResponse TryValidateBodySyntax(PageUpdateOptions options, out Script parsedAst) {
		// Deterministic JavaScript syntax check (ENG-89796). Mobile bodies are
		// JSON and are handled by their own validator below; for web bodies we
		// parse the body with Acornima BEFORE invoking the regex-based content
		// validators or the sampling service. A syntax error means the page
		// would not load in the browser — failing fast surfaces the precise
		// {line, column, message} to the operator without sinking time into
		// model-side review or persisting a broken body. The parsed AST is
		// then fed into PageBodyAstLinter further down (AFTER the regex
		// validators ran) so the established regex error messages still win
		// on overlapping detections; lint findings only ADD detections.
		parsedAst = null;
		if (PageSchemaTypeExtensions.FromBody(options.Body) == PageSchemaType.Mobile)
			return null;
		PageBodySyntaxValidationResult syntaxResult =
			PageBodySyntaxValidator.ValidateAndParse(options.Body, out parsedAst);
		if (syntaxResult.IsValid)
			return null;
		return new PageUpdateResponse {
			Success = false,
			Error = PageBodySyntaxValidator.FormatError(syntaxResult)
		};
	}

	// AST lint pass runs on the success path of the regex validators so the
	// regex messages — which existing tests and operator habits depend on —
	// remain authoritative for overlapping detections. Lint findings cover
	// only what the regex layer does not. The returned Failure value is
	// non-null when at least one Error-severity finding triggered; the
	// returned Warnings list carries any Warning-severity findings on the
	// success path.
	private static (PageUpdateResponse Failure, IReadOnlyList<string> Warnings) RunAstLintPass(Script parsedAst) {
		if (parsedAst is null) {
			return (null, Array.Empty<string>());
		}
		IReadOnlyList<PageBodyLintFinding> findings = PageBodyAstLinter.Lint(parsedAst);
		IReadOnlyList<PageBodyLintFinding> errors =
			findings.Where(f => f.Severity == LintSeverity.Error).ToArray();
		if (errors.Count > 0) {
			return (new PageUpdateResponse {
				Success = false,
				Error = PageBodyAstLinter.FormatErrors(errors)
			}, Array.Empty<string>());
		}
		string[] warnings = findings
			.Where(f => f.Severity == LintSeverity.Warning)
			.Select(PageBodyAstLinter.FormatFinding)
			.ToArray();
		return (null, warnings);
	}

	private static IReadOnlyList<string> MergeWarnings(IReadOnlyList<string> first, IReadOnlyList<string> second) {
		var combined = new List<string>();
		if (first != null) {
			combined.AddRange(first);
		}
		if (second != null) {
			combined.AddRange(second);
		}
		return combined;
	}

	private (PageUpdateResponse Failure, IReadOnlyList<string> Warnings) ValidateBody(PageUpdateOptions options) {
		if (PageSchemaTypeExtensions.FromBody(options.Body) == PageSchemaType.Mobile) {
			// Mobile body validation requires async catalogs (CDN+cache) AND the
			// parsed-resources lookup master added in ENG-89649. PageUpdateTool runs
			// under the McpToolExecutionLock; the MCP server has no SynchronizationContext,
			// so a sync-over-async wait is deadlock-free here. Refactoring the full
			// PageUpdate → ValidateBody chain to async is out of scope for this PR.
			SchemaValidationService.TryParseResources(options.Resources, out Dictionary<string, string>? mobileResources, out _);
			PageSyncValidationResult mobileResult = MobilePageValidation
				.RunAsync(options.Body, mobileComponentCatalog, webComponentCatalog, mobileResources)
				.GetAwaiter().GetResult();
			if (!mobileResult.ContentOk) {
				return (new PageUpdateResponse {
					Success = false,
					Error = "Validation failed: " + string.Join("; ", mobileResult.Errors ?? [])
				}, null);
			}
			// The web path runs this inside ValidateWebPageBody; mobile validation does not, so run the
			// run-process structural check (processName required) here too — the signature code check runs
			// afterwards in ValidateRunProcessButtons for both surfaces.
			SchemaValidationResult mobileRunProcess =
				SchemaValidationService.ValidateRunProcessButtonStructure(options.Body);
			if (!mobileRunProcess.IsValid) {
				return (new PageUpdateResponse {
					Success = false,
					Error = "Validation failed: " + string.Join("; ", mobileRunProcess.Errors)
				}, null);
			}
			return (null, mobileResult.Warnings);
		}
		(string bodyError, IReadOnlyList<string> webWarnings) = ValidateWebPageBody(options.Body);
		if (bodyError != null) {
			return (new PageUpdateResponse { Success = false, Error = bodyError }, null);
		}
		return (null, webWarnings);
	}

	private async Task<(PageUpdateResponse Failure, PageSamplingReview Review)> TryRunSamplingAsync(
		PageUpdateOptions options, PageUpdateArgs args, McpServerLib.McpServer server, CancellationToken cancellationToken) {
		if (options.DryRun || args.SkipSampling == true) {
			return (null, null);
		}
		PageSamplingReview samplingReview = await _samplingService.TrySamplingReviewAsync(
			server, args.SchemaName, options.Body, args.Resources, cancellationToken);
		if (samplingReview is { Ok: false, Skipped: false } && samplingReview.Issues?.Count > 0) {
			return (new PageUpdateResponse {
				Success = false,
				Error = "Sampling review found issues: " + string.Join("; ", samplingReview.Issues)
					+ ". Fix the page body and resubmit. Do NOT retry the same body with skip-sampling=true to bypass this check.",
				SamplingReview = samplingReview
			}, samplingReview);
		}
		return (null, samplingReview);
	}

	private static PageUpdateOptions BuildOptions(PageUpdateArgs args) =>
		new() {
			SchemaName = args.SchemaName,
			Body = args.Body,
			BodyFile = args.BodyFile,
			DryRun = args.DryRun ?? false,
			Resources = args.Resources,
			OptionalProperties = args.OptionalProperties,
			Mode = args.Mode,
			TargetPackageUId = args.TargetPackageUId,
			TargetSchemaUId = args.TargetSchemaUId,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password,
			Force = args.Force ?? false,
			NotifyDesignerPresence = true
		};

	/// <summary>
	/// Validates that every <c>crt.RunBusinessProcessRequest</c> button in the body references real
	/// process parameter CODES, by resolving the live process signature. A code that does
	/// not exist on the process is rejected here because the platform silently drops such values.
	/// When the environment cannot resolve the signature the call is downgraded to a warning rather
	/// than blocking the write.
	/// </summary>
	// internal (not private) so the run-process orchestration — environment gating, signature
	// caching, hard-fail vs. warning routing, and warning aggregation — is unit-testable without
	// driving the full UpdatePage body-validation pipeline. See PageUpdateToolRunProcessTests.
	internal (PageUpdateResponse Failure, IReadOnlyList<string> Warnings) ValidateRunProcessButtons(
		PageUpdateOptions options) {
		IReadOnlyList<RunProcessButtonConfig> configs = RunProcessButtonConfigReader.Read(options.Body);
		if (configs.Count == 0) {
			return (null, null);
		}
		bool hasEnvironment = !string.IsNullOrWhiteSpace(options.Environment)
			|| !string.IsNullOrWhiteSpace(options.Uri);
		if (!hasEnvironment) {
			return (null, [
				"Run-process button parameter codes were not validated because no environment was provided. "
				+ "Pass environment-name so update-page can verify codes against the process signature."
			]);
		}
		// Resolve signatures WITHOUT holding the global MCP execution lock — the generator makes a
		// retrying HTTP call (up to 3x10s) and must not block the single-lane MCP control plane.
		// The generator writes log warnings to the shared logger; drain them afterwards under the
		// lock (no network inside) so they do not leak into the next tool response.
		(PageUpdateResponse Failure, IReadOnlyList<string> Warnings) result =
			ValidateRunProcessButtonsAgainstSignatures(options, configs);
		return ExecuteWithCleanLog(() => result);
	}

	internal (PageUpdateResponse Failure, IReadOnlyList<string> Warnings) ValidateRunProcessButtonsAgainstSignatures(
		PageUpdateOptions options, IReadOnlyList<RunProcessButtonConfig> configs) {
		var warnings = new List<string>();
		var signatures = new Dictionary<string, GetProcessSignatureResponse>(StringComparer.OrdinalIgnoreCase);
		foreach (RunProcessButtonConfig config in configs) {
			(PageUpdateResponse failure, IReadOnlyList<string> buttonWarnings) =
				EvaluateButtonSignature(options, config, signatures);
			if (failure != null) {
				return (failure, null);
			}
			warnings.AddRange(buttonWarnings);
		}
		return (null, warnings.Count > 0 ? warnings : null);
	}

	private (PageUpdateResponse Failure, IReadOnlyList<string> Warnings) EvaluateButtonSignature(
		PageUpdateOptions options, RunProcessButtonConfig config,
		IDictionary<string, GetProcessSignatureResponse> signatures) {
		if (string.IsNullOrWhiteSpace(config.ProcessName)) {
			return (null, []); // structural validation already reported the missing processName
		}
		if (!TryGetCachedSignature(options, config.ProcessName, signatures,
				out GetProcessSignatureResponse signature, out string resolveWarning)) {
			return (null, resolveWarning is null ? [] : [resolveWarning]);
		}
		if (!signature.Success) {
			if (signature.ProcessResolutionFailed) {
				return (ProcessNotResolvedFailure(config, signature), null);
			}
			// Transient/transport failure — do not block the write on a backend hiccup.
			return (null, [TransientValidationWarning(config, signature)]);
		}
		RunProcessButtonSignatureValidator.Result validation =
			RunProcessButtonSignatureValidator.Validate(config, signature.Parameters);
		if (validation.Error != null) {
			return (new PageUpdateResponse { Success = false, Error = validation.Error }, null);
		}
		return (null, validation.Warnings);
	}

	private bool TryGetCachedSignature(PageUpdateOptions options, string processName,
		IDictionary<string, GetProcessSignatureResponse> signatures,
		out GetProcessSignatureResponse signature, out string warning) {
		warning = null;
		if (signatures.TryGetValue(processName, out signature)) {
			return signature is not null;
		}
		if (!TryResolveSignature(options, processName, out signature, out warning)) {
			return false;
		}
		signatures[processName] = signature;
		return signature is not null;
	}

	private static PageUpdateResponse ProcessNotResolvedFailure(
		RunProcessButtonConfig config, GetProcessSignatureResponse signature) =>
		new() {
			Success = false,
			Error = $"Run-process button references process '{config.ProcessName}' which could not be "
				+ $"uniquely resolved on the environment: {signature.Error} "
				+ "Resolve it with get-process-signature and use the returned processCode."
		};

	private static string TransientValidationWarning(
		RunProcessButtonConfig config, GetProcessSignatureResponse signature) =>
		$"Run-process button parameter codes for process '{config.ProcessName}' were not validated: "
		+ signature.Error;

	private bool TryResolveSignature(PageUpdateOptions options, string processName,
		out GetProcessSignatureResponse signature, out string warning) {
		signature = null;
		warning = null;
		GetProcessSignatureOptions signatureOptions = new() {
			ProcessName = processName,
			Environment = options.Environment,
			Uri = options.Uri,
			Login = options.Login,
			Password = options.Password
		};
		GetProcessSignatureCommand signatureCommand;
		try {
			signatureCommand = _commandResolver.Resolve<GetProcessSignatureCommand>(signatureOptions);
		} catch (Exception ex) {
			warning = $"Run-process button parameter codes for process '{processName}' were not validated: {ex.Message}";
			return false;
		}
		try {
			signatureCommand.TryGetSignature(signatureOptions, out signature);
			return true;
		} catch (Exception ex) {
			warning = $"Run-process button parameter codes for process '{processName}' were not validated: {ex.Message}";
			return false;
		}
	}

	private static (string Error, IReadOnlyList<string> Warnings) ValidateWebPageBody(string body) {
		var errors = new List<string>();
		Collect(SchemaValidationService.ValidateMarkerContent(body), errors);
		Collect(SchemaValidationService.ValidateLocalizableTextLiterals(body), errors);
		Collect(SchemaValidationService.ValidateValidatorParamResourceBindings(body), errors);
		Collect(SchemaValidationService.ValidateValidatorControlBindings(body), errors);
		Collect(SchemaValidationService.ValidateValidatorBindingPlacement(body), errors);
		Collect(SchemaValidationService.ValidateValidatorBindingShape(body), errors);
		Collect(SchemaValidationService.ValidateStandardValidatorUsage(body), errors);
		Collect(SchemaValidationService.ValidateCustomValidatorParamCompleteness(body), errors);
		Collect(SchemaValidationService.ValidateCustomValidatorFactoryShape(body), errors);
		Collect(SchemaValidationService.ValidateConverterDeclarations(body), errors);
		Collect(SchemaValidationService.ValidateConverterFunctionShape(body), errors);
		Collect(SchemaValidationService.ValidateHandlerStructure(body), errors);
		Collect(SchemaValidationService.ValidateRunProcessButtonStructure(body), errors);
		Collect(SchemaValidationService.ValidateValidatorDeclarations(body), errors);
		var warnings = new List<string>();
		warnings.AddRange(SchemaValidationService.ValidateSchemaDepsCompleteness(body).Warnings);
		warnings.AddRange(SchemaValidationService.ValidateContextAccessAwait(body).Warnings);
		string error = errors.Count > 0 ? "Validation failed: " + string.Join("; ", errors) : null;
		return (error, warnings.Count > 0 ? warnings : null);
	}

	private static void Collect(SchemaValidationResult result, List<string> errors) {
		if (!result.IsValid) errors.AddRange(result.Errors);
	}

	private void TryVerifyPage(PageUpdateArgs args, PageUpdateResponse response) {
		try {
			PageGetOptions getOptions = new() {
				SchemaName = args.SchemaName,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			PageGetCommand getCommand = _commandResolver.Resolve<PageGetCommand>(getOptions);
			if (getCommand.TryGetPage(getOptions, out PageGetResponse getResponse) && getResponse.Success)
				response.Page = getResponse.Page;
		} catch {
			// verify is best-effort; failure does not fail the update
		}
	}

}

public sealed record PageUpdateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name, e.g. 'UsrMyApp_FormPage'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("body")]
	[property: Description("Full JavaScript page body with markers. Pass either `body` (inline string) or `body-file` (path); one is required. WARNING: do NOT send the full get-page `raw.body` back verbatim — that re-applies existing merges and fails server-side with 'Object vs Array'. Send ONLY the new viewConfigDiff/handlers operations plus the required marker envelope.")]
	string? Body,

	[property: JsonPropertyName("resources")]
	[property: Description("JSON object string of localizable string key-value pairs the platform does NOT auto-provide \u2014 e.g. custom tab/group titles, button captions, validator messages, and explicit overrides of inherited captions \u2014 e.g. '{\"UsrDetailsTab_caption\": \"Details\"}'. IMPORTANT: only pass keys that have NO matching DS-bound view model attribute on the target page (or that intentionally override the inherited caption). Keys matching an existing DS-bound attribute are auto-provided by the platform from the entity column caption and MUST be omitted. Inline placeholder/title/label/caption/tooltip literals in the body are REJECTED — bind each via $Resources.Strings.<Key> and register the key's default-language value here. See `page-schema-resources` guidance for the full check.")]
	string? Resources,

	[property: JsonPropertyName("dry-run")]
	[property: Description("If true, validate without saving. Default: false")]
	bool? DryRun,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'. Preferred for normal MCP work.")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string? Uri,
	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login,
	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password,
	[property: JsonPropertyName("skip-sampling")]
	[property: Description("Reserved escape hatch. Omit by default. Pre-condition for setting true: the immediately preceding user message in this turn contains an explicit instruction to skip the AI semantic review, OR the MCP host has reported sampling as unavailable in this session. Absent that evidence, omit this field. Default: false")]
	bool? SkipSampling = null,
	[property: JsonPropertyName("optional-properties")]
	[property: Description("JSON array of {key, value} objects to merge into schema optionalProperties, e.g. '[{\"key\":\"entitySchemaName\",\"value\":\"UsrMyEntity\"}]'")]
	string? OptionalProperties = null,
	[property: JsonPropertyName("verify")]
	[property: Description("If true, read the page back after saving and return its metadata. Best-effort — verify failure does not fail the update. Default: false")]
	bool? Verify = null,
	[property: JsonPropertyName("body-file")]
	[property: Description("Absolute path to a file containing the page body. Used when `body` is empty. Enables passing large bodies without inline JSON escaping.")]
	string? BodyFile = null,
	[property: JsonPropertyName("mode")]
	[property: Description("Write mode. 'replace' (default) saves the body verbatim. 'append' merges the incoming body fragment with the schema's current body on the server — viewConfigDiff entries dedupe by `name` (incoming wins), handlers dedupe by `request`. Use 'append' when adding a component without clobbering existing customizations.")]
	string? Mode = null,
	[property: JsonPropertyName("target-package-uid")]
	[property: Description("Explicit target package UId for the replacing schema. Overrides automatic design-package resolution. Required when multiple apps replace the same platform page and automatic resolution would land the edit in the wrong app's design package.")]
	string? TargetPackageUId = null,
	[property: JsonPropertyName("target-schema-uid")]
	[property: Description("Explicit schema UId to save into directly. Bypasses hierarchy resolution entirely. Use when you already know the exact replacing schema you want to modify (obtained via list-pages filter by name) and want to skip the design-package inference.")]
	string? TargetSchemaUId = null,
	[property: JsonPropertyName("force")]
	[property: Description("Skip the external-modification (checksum) conflict check and deliberately overwrite out-of-band changes. Set true ONLY after the user explicitly confirms overwriting changes made outside this session. Default: false")]
	bool? Force = null,
	[property: JsonPropertyName("output-directory")]
	[property: Description("Optional. Directory that anchors the .clio-pages baseline lookup — pass the same value that was passed to get-page when it differs from the auto-detected workspace root. Used only for conflict-baseline discovery; does not change where the page is saved.")]
	string? OutputDirectory = null
);
