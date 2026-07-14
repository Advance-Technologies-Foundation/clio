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
using Clio.UserEnvironment;
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
	IPlatformVersionResolverFactory? resolverFactory = null,
	ISettingsRepository? settingsRepository = null)
	: BaseTool<PageUpdateOptions>(command, logger, commandResolver) {

	private readonly IToolCommandResolver _commandResolver = commandResolver;
	private readonly IPageBodySamplingService _samplingService = samplingService;

	internal const string ToolName = "update-page";

	// Prefix shared by every offline validation-failure response so the wording stays consistent.
	private const string ValidationFailedPrefix = "Validation failed: ";

	// Prefix for the up-front append/full-config rejection. Exposed as a shared constant so the unit and
	// e2e tests assert against it instead of a duplicated string literal (ENG-93090 RC-5).
	internal const string AppendFullConfigRejectionPrefix = "Append merge cannot use this body: ";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Update a Freedom UI page schema body. environment-name preferred; uri/login/password fallback only. " +
		"On a successful non-dry-run save it also best-effort notifies active Creatio designers (Designer Presence); the save still succeeds if that notification is skipped (carried as a warning). " +
		"CONFLICT DETECTION: if get-page stored a checksum baseline for the same environment and the schema changed outside this session, the save is blocked with `conflict: true` + `conflictDetails` — do NOT retry the same body; re-run get-page, re-apply your change, retry, and set force=true only after the user confirms overwriting. " +
		"BEFORE editing the body call get-guidance `page-modification` and follow its pre-edit checklist — it routes visibility/required/value-set and lookup-filter work to business rules (not handlers/validators), display-only transforms to converters, run-process buttons to `run-process-button` (resolve parameter CODEs with get-process-signature first), and localizable strings to `page-schema-resources`. " +
		"INSERTED-FIELD CONTRACT: " + SchemaValidationService.InsertedFieldContractSummary)]
	public async Task<PageUpdateResponse> UpdatePage(
		[Description("schema-name, body (required); resources, dry-run (optional); environment-name preferred; uri/login/password fallback only.")]
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
				return new PageUpdateResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
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
		PageUpdateResponse appendFormFailure = TryValidateAppendBodyForm(options);
		if (appendFormFailure != null) {
			return (appendFormFailure, null, null, null);
		}
		PageUpdateResponse syntaxFailure = TryValidateBodySyntax(options, out Script parsedAst);
		if (syntaxFailure != null) {
			return (ResolveSyntaxFailure(options, syntaxFailure), null, null, null);
		}
		string? requestedVersion = await ResolvePlatformVersionAsync(options, cancellationToken).ConfigureAwait(false);
		(PageUpdateResponse validationFailure, IReadOnlyList<string> validationWarnings) = ValidateBody(options, requestedVersion);
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
		return (samplingFailure, validationWarnings, lintWarnings, samplingReview);
	}

	// Up-front (offline, no server round-trip) guard for append mode. `append` requires the incoming
	// body to be in diff form; a full-config body (web: SCHEMA_VIEW_MODEL_CONFIG / SCHEMA_MODEL_CONFIG,
	// mobile: viewModelConfig / modelConfig) cannot be merged. The merger already refuses such a CURRENT
	// server body after fetching it, but detecting the same shape in the INCOMING body here prevents a
	// wasted fetch+merge attempt and surfaces the precise corrective hint before execution (ENG-93090).
	// Replace mode is unaffected — a full-config body is a legitimate verbatim replacement.
	private static PageUpdateResponse TryValidateAppendBodyForm(PageUpdateOptions options) {
		if (!string.Equals(options.Mode, "append", StringComparison.OrdinalIgnoreCase)) {
			return null;
		}
		if (!PageBodyMerger.UsesUnsupportedFullConfigForm(options.Body, out string message)) {
			return null;
		}
		// `message` (the shared PageBodyMerger constant) already states both corrective actions
		// ("Use 'replace' mode, or convert the body to the diff form ... before append."). Add ONLY
		// what it lacks — the docs pointer — instead of re-stating those actions (ENG-93090 RC-3).
		return new PageUpdateResponse {
			Success = false,
			Error = AppendFullConfigRejectionPrefix + message
				+ " See docs://mcp/guides/page-modification for the append diff-form contract."
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

	// A whole-body JavaScript syntax error is frequently a SIDE EFFECT of a more specific,
	// offline-detectable problem the operator can actually act on: a malformed `resources` /
	// `optional-properties` argument payload, a run-process button missing its required
	// `processName`, a broken JSON SCHEMA_* marker / wrong-shaped converter-validator-handler, or
	// simply an unregistered environment name. The generic "JavaScript syntax error at line X,
	// column Y" message names none of those. Prefer the specific, actionable error when one of the
	// OFFLINE validators pinpoints it — preserving the ENG-89796 fail-fast-on-pure-syntax wording
	// when nothing more specific is found (clean payloads + a genuine JS-only typo).
	//
	// ENG-92049 constraint (honored here): only OFFLINE validators run on this path. No HTTP
	// signature resolution (ValidateRunProcessButtons resolves process signatures over the wire and
	// deliberately stays on the success path); the run-process STRUCTURAL check below is a pure
	// regex over the body. The environment check resolves the command, which is offline up to the
	// EnvironmentResolutionException throw (the unknown-environment / missing-settings guard runs
	// before any network call); the resolved command is discarded, so a body that cannot parse
	// triggers no Creatio I/O even in dry-run.
	internal PageUpdateResponse ResolveSyntaxFailure(PageUpdateOptions options, PageUpdateResponse syntaxFailure) {
		// 1. Argument-payload validation — pure offline, independent of body parsing or markers.
		string argumentError = PageUpdateCommand.ValidateArgumentPayloads(options.Resources, options.OptionalProperties);
		if (argumentError != null) {
			return new PageUpdateResponse { Success = false, Error = argumentError };
		}
		// 2. Run-process STRUCTURAL check (processName / processRunType required) — pure offline
		//    regex. Runs regardless of marker integrity so a run-process button that omits
		//    processName is surfaced even when the body is otherwise not a recognizable page.
		SchemaValidationResult runProcessStructure =
			SchemaValidationService.ValidateRunProcessButtonStructure(options.Body);
		if (!runProcessStructure.IsValid) {
			return new PageUpdateResponse {
				Success = false,
				Error = ValidationFailedPrefix + string.Join("; ", runProcessStructure.Errors)
			};
		}
		// 3. Offline content chain — only when the body is still a recognizable page (markers present
		//    and paired). If marker integrity fails the body is not a usable page and the generic JS
		//    syntax error is the more honest signal (ENG-89796).
		if (SchemaValidationService.ValidateMarkerIntegrity(options.Body).IsValid) {
			// Offline syntax-failure path: chart validation is version-scoped, but no environment
			// probe runs here, so validate against the 'latest' superset (requestedVersion: null).
			(PageUpdateResponse contentFailure, _) = ValidateBody(options, requestedVersion: null);
			if (contentFailure != null) {
				return contentFailure;
			}
		}
		// 4. Environment resolution — offline up to the unknown-environment guard. Surface a missing
		//    environment over the generic syntax error so the operator fixes the right thing first.
		PageUpdateResponse environmentFailure = TryResolveEnvironmentFailure(options);
		if (environmentFailure != null) {
			return environmentFailure;
		}
		// 5. Genuine JS-only syntax error — nothing more specific was found.
		return syntaxFailure;
	}

	// Attempts to resolve the update-page command purely to detect an unknown-environment /
	// broken-settings argument error WITHOUT making any network call (the resolver's environment
	// guard throws before any HTTP). Returns a structured failure on EnvironmentResolutionException,
	// or null when the environment resolves (or no resolver / environment was supplied). Any other
	// exception is swallowed so this offline probe never masks the original syntax error.
	private PageUpdateResponse TryResolveEnvironmentFailure(PageUpdateOptions options) {
		bool hasEnvironment = !string.IsNullOrWhiteSpace(options.Environment)
			|| !string.IsNullOrWhiteSpace(options.Uri);
		if (!hasEnvironment) {
			return null;
		}
		try {
			ResolveCommand<PageUpdateCommand>(options);
			return null;
		} catch (EnvironmentResolutionException ex) {
			return new PageUpdateResponse { Success = false, Error = ex.Message };
		} catch (Exception) {
			// A DI/bootstrap failure here is unexpected; do not let it mask the syntax error the
			// caller actually needs to fix. Fall back to the generic syntax message.
			return null;
		}
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

	/// <summary>
	/// Resolves the target environment's platform version so the chart-widget validation catalog is scoped
	/// to the component set the environment actually ships (mirroring <c>get-component-info</c>'s resolution).
	/// Returns <see langword="null"/> for mobile bodies (chart validation is web-only — no probe needed) and
	/// fail-soft on any resolution failure or absent resolver dependencies; <see cref="ChartWidgetValidation"/>
	/// maps <see langword="null"/> to the safe <c>latest</c> superset so version resolution never blocks a save.
	/// </summary>
	private async Task<string?> ResolvePlatformVersionAsync(PageUpdateOptions options, CancellationToken cancellationToken) {
		if (PageSchemaTypeExtensions.FromBody(options.Body) == PageSchemaType.Mobile
			|| resolverFactory is null || settingsRepository is null) {
			return null;
		}
		try {
			EnvironmentSettings settings = settingsRepository.GetEnvironment(new EnvironmentOptions {
				Environment = options.Environment,
				Uri = options.Uri,
				Login = options.Login,
				Password = options.Password
			});
			if (settings is null) {
				return null;
			}
			PlatformVersionResolution resolution = await resolverFactory.Create(settings)
				.ResolveAsync(cancellationToken).ConfigureAwait(false);
			return resolution?.ResolvedVersion;
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception) {
			// Fail-soft: a bad/unreachable environment must not break a save; the catalog stays on 'latest'.
			return null;
		}
	}

	private (PageUpdateResponse Failure, IReadOnlyList<string> Warnings) ValidateBody(
		PageUpdateOptions options, string? requestedVersion) {
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
					Error = ValidationFailedPrefix + string.Join("; ", mobileResult.Errors ?? [])
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
					Error = ValidationFailedPrefix + string.Join("; ", mobileRunProcess.Errors)
				}, null);
			}
			return (null, mobileResult.Warnings);
		}
		// Field-binding validators suppress label-resource errors for keys supplied via the
		// `resources` parameter, so this pre-resolution gate must see them too — otherwise a page
		// whose label is provided in `resources` is falsely rejected here, before the
		// resource-aware post-resolution validation runs (matches PageUpdateOptions / PageSyncTool / PageValidateTool).
		SchemaValidationService.TryParseResources(options.Resources, out Dictionary<string, string>? explicitResources, out _);
		(string bodyError, IReadOnlyList<string> webWarnings) = ValidateWebPageBody(options.Body, explicitResources);
		if (bodyError != null) {
			return (new PageUpdateResponse { Success = false, Error = bodyError }, null);
		}
		// Registry-driven chart-widget validation needs the async, version-scoped component catalog.
		// Sync-over-async is deadlock-free here under the McpToolExecutionLock (no SynchronizationContext),
		// the same pattern the mobile branch above uses. Fail-open inside when the registry is unavailable.
		// requestedVersion scopes the catalog to the target environment's resolved platform version
		// (probed the same way get-component-info resolves it); null validates against 'latest' (fail-soft).
		SchemaValidationResult chartResult = ChartWidgetValidation
			.ValidateAsync(options.Body, webComponentCatalog, requestedVersion, CancellationToken.None)
			.GetAwaiter().GetResult();
		if (!chartResult.IsValid) {
			return (new PageUpdateResponse {
				Success = false,
				Error = "Validation failed: " + string.Join("; ", chartResult.Errors)
			}, null);
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
			warning = SensitiveErrorTextRedactor.Redact($"Run-process button parameter codes for process '{processName}' were not validated: {ex.Message}");
			return false;
		}
		try {
			signatureCommand.TryGetSignature(signatureOptions, out signature);
			return true;
		} catch (Exception ex) {
			warning = SensitiveErrorTextRedactor.Redact($"Run-process button parameter codes for process '{processName}' were not validated: {ex.Message}");
			return false;
		}
	}

	private static (string Error, IReadOnlyList<string> Warnings) ValidateWebPageBody(
		string body, IReadOnlyDictionary<string, string>? explicitResources = null) {
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
		CollectWithPrefix(SchemaValidationService.ValidateStandardFieldBindings(body, explicitResources), "invalid form field bindings", errors);
		CollectWithPrefix(SchemaValidationService.ValidateInsertedFieldSelfConsistency(body, explicitResources), "invalid form field bindings", errors);
		var warnings = new List<string>();
		warnings.AddRange(SchemaValidationService.ValidateSchemaDepsCompleteness(body).Warnings);
		warnings.AddRange(SchemaValidationService.ValidateContextAccessAwait(body).Warnings);
		string error = errors.Count > 0 ? ValidationFailedPrefix + string.Join("; ", errors) : null;
		return (error, warnings.Count > 0 ? warnings : null);
	}

	private static void Collect(SchemaValidationResult result, List<string> errors) {
		if (!result.IsValid) errors.AddRange(result.Errors);
	}

	private static void CollectWithPrefix(SchemaValidationResult result, string prefix, List<string> errors) {
		if (result.IsValid) return;
		foreach (string err in result.Errors)
			errors.Add(prefix + ": " + err);
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
	[property: Description("Full JavaScript page body with markers, passed as a RAW STRING (not a JSON object/dict) — the schema source text with its /**MARKER*/ pairs. Pass either `body` (inline string) or `body-file` (path); one is required. WARNING: do NOT send the full get-page `raw.body` back verbatim — that re-applies existing merges and fails server-side with 'Object vs Array'. Send ONLY the new viewConfigDiff/handlers operations plus the required marker envelope. APPEND mode additionally requires the diff form (SCHEMA_VIEW_MODEL_CONFIG_DIFF / SCHEMA_MODEL_CONFIG_DIFF); a full-config body is rejected up-front — use mode='replace' for a full-config body.")]
	string? Body,

	[property: JsonPropertyName("resources")]
	[property: Description(McpToolDescriptions.PageResources)]
	string? Resources,

	[property: JsonPropertyName("dry-run")]
	[property: Description("If true, validate without saving. Default: false")]
	bool? DryRun,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri,
	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login,
	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
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
	[property: Description("Write mode. 'replace' (default) saves the body verbatim. 'append' merges the incoming body fragment with the schema's current body on the server — viewConfigDiff entries dedupe by `name` (incoming wins), handlers dedupe by `request`. Use 'append' when adding a component without clobbering existing customizations. Append requires the diff form; a full-config body (SCHEMA_VIEW_MODEL_CONFIG / SCHEMA_MODEL_CONFIG, or mobile viewModelConfig / modelConfig) is rejected up-front — use 'replace' for those.")]
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
