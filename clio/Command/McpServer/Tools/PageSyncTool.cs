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
	IComponentInfoCatalog webComponentCatalog) {

	internal const string ToolName = "sync-pages";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Updates multiple Freedom UI page schemas in a single call. " +
	             "For each page: validates body client-side (optional), runs AI semantic review (optional), saves to Creatio, " +
	             "and verifies the update (optional). Continues processing remaining pages on failure. " +
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
		[Description("Parameters: environment-name (required); pages array (required); validate, verify (optional).")]
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
		List<PageSyncPageResult> results = ExecuteSyncBatch(args, pages, prePass, samplingResults);
		return new PageSyncResponse {
			Success = results.Count > 0 && results.All(r => r.Success),
			Pages = results
		};
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

	private static async Task<IReadOnlyList<PageSamplingReview?>> RunSamplingPrePassAsync(
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
			samplingResults[i] = await PageBodySamplingService.TrySamplingReviewAsync(
				server, page.SchemaName, page.Body, page.Resources, cancellationToken);
		}
		return samplingResults;
	}

	private List<PageSyncPageResult> ExecuteSyncBatch(
		PageSyncArgs args,
		IReadOnlyList<PageSyncPageInput> pages,
		PageSyncPrePassResults prePass,
		IReadOnlyList<PageSamplingReview?> samplingResults) {
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
				results.Add(BuildPrePassFailureResult(page, syntaxMsg));
				continue;
			}
			PageSyncPageResult deterministicFailure = TryMaterialiseDeterministicFailure(page, entry, validate);
			if (deterministicFailure != null) {
				results.Add(deterministicFailure);
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
		lock (McpToolExecutionLock.SyncRoot) {
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
				samplingResults);
			try {
				foreach (int idx in pendingIndices) {
					results[idx] = ProcessPendingPage(pages[idx], idx, ctx);
				}
				Thread.Sleep(500);
			} catch (Exception ex) {
				FillPendingWithError(results, pendingIndices, pages, ex.Message);
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
			error = ex.Message;
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

	private static PageSyncPageResult BuildPrePassFailureResult(PageSyncPageInput page, string failureMessage) =>
		new() {
			SchemaName = page.SchemaName,
			Success = false,
			Error = failureMessage,
			Validation = new PageSyncValidationResult {
				MarkersOk = true,
				JsSyntaxOk = false,
				ContentOk = true,
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
				MarkersOk = true,
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
			prePassEntry.LintFindings);
		return SyncSinglePage(page, opOptions);
	}

	private sealed record PageSyncBatchContext(
		PageUpdateCommand UpdateCommand,
		PageGetCommand GetCommand,
		bool Validate,
		bool Verify,
		string? OutputDirectory,
		PageSyncPrePassResults PrePass,
		IReadOnlyList<PageSamplingReview?> SamplingResults);

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
		IReadOnlyList<PageBodyLintFinding> LintFindings);

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
			// Pipeline order inside this method:
			//   1. Regex content validation (existing chain inside
			//      TryValidatePage / ValidateBody) — runs first so its
			//      established error wording wins on overlapping detections.
			//   2. AST lint pass — runs only when regex passed. Error-severity
			//      lint findings produce a fail-fast result; warnings are
			//      appended to the validation envelope.
			// Pre-pass already ran syntax + lint, so a syntax failure was
			// short-circuited in ExecuteSyncBatch; only lint results need to
			// be re-checked here.
			PageSyncValidationResult validationResult = null;
			if (opOptions.Validate) {
				PageSyncPageResult validationFailure = TryValidatePage(page, opOptions.SamplingReview, out validationResult);
				if (validationFailure != null)
					return validationFailure;
			}
			PageSyncPageResult lintFailure = TryMaterialiseLintError(page, opOptions, validationResult);
			if (lintFailure != null) {
				return lintFailure;
			}
			validationResult = AppendCommandWarnings(validationResult, GetLintWarningMessages(opOptions.LintFindings));
			if (opOptions.SamplingReview is { Ok: false, Skipped: false } && opOptions.SamplingReview.Issues?.Count > 0) {
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					SamplingReview = opOptions.SamplingReview,
					Error = "Sampling review found issues: " + string.Join("; ", opOptions.SamplingReview.Issues)
						+ ". Fix the page body and resubmit. Do NOT retry the same body with skip-sampling=true to bypass this check."
				};
			}
			PageUpdateOptions updateOptions = new() {
				SchemaName = page.SchemaName,
				Body = page.Body,
				DryRun = false,
				Resources = page.Resources,
				OptionalProperties = page.OptionalProperties
			};
			opOptions.UpdateCommand.TryUpdatePage(updateOptions, out PageUpdateResponse updateResponse);
			if (!updateResponse.Success) {
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					Error = updateResponse.Error
				};
			}
			validationResult = AppendCommandWarnings(validationResult, updateResponse.Warnings);
			if (opOptions.Verify && opOptions.GetCommand != null) {
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
				string? verifiedBodyFile = null;
				if (getResponse.Raw?.Body is not null) {
					string anchor = PageOutputDirectoryResolver.ResolveAnchor(
						fileSystem,
						fileSystem.Directory.GetCurrentDirectory(),
						Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
						ClioRuntimePaths.Home,
						opOptions.OutputDirectory);
					string schemaDir = fileSystem.Path.Combine(
						anchor, ".clio-pages", page.SchemaName);
					fileSystem.Directory.CreateDirectory(schemaDir);
					string bodyFile = fileSystem.Path.Combine(schemaDir, "body.js");
					fileSystem.File.WriteAllText(bodyFile, getResponse.Raw.Body);
					verifiedBodyFile = bodyFile;
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
				Error = ex.Message
			};
		}
	}

	private static PageSyncPageResult TryMaterialiseLintError(
		PageSyncPageInput page,
		PageSyncOperationOptions opOptions,
		PageSyncValidationResult validationResult) {
		IReadOnlyList<PageBodyLintFinding> errors = opOptions.LintFindings
			.Where(f => f.Severity == LintSeverity.Error)
			.ToArray();
		if (errors.Count == 0) {
			return null;
		}
		string lintError = PageBodyAstLinter.FormatErrors(errors);
		return new PageSyncPageResult {
			SchemaName = page.SchemaName,
			Success = false,
			Validation = new PageSyncValidationResult {
				MarkersOk = validationResult?.MarkersOk ?? true,
				JsSyntaxOk = validationResult?.JsSyntaxOk ?? true,
				ContentOk = false,
				Errors = [lintError]
			},
			SamplingReview = opOptions.SamplingReview,
			Error = lintError
		};
	}

	private static IReadOnlyList<string> GetLintWarningMessages(IReadOnlyList<PageBodyLintFinding> findings) =>
		findings
			.Where(f => f.Severity == LintSeverity.Warning)
			.Select(f => $"line {f.Line}, column {f.Column}: {f.Rule} — {f.Message}")
			.ToArray();

	private static PageSyncValidationResult ValidateBody(string body, string? resources) {
		SchemaValidationResult markerResult = SchemaValidationService.ValidateMarkerIntegrity(body);
		SchemaValidationResult syntaxResult = SchemaValidationService.ValidateJsSyntax(body);
		SchemaValidationResult contentResult = GetContentValidationResult(body, markerResult);
		Dictionary<string, string>? explicitResources = TryParseExplicitResources(resources, contentResult);
		SchemaValidationResult fieldResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateStandardFieldBindings(body, explicitResources));
		SchemaValidationResult insertSelfConsistencyResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateInsertedFieldSelfConsistency(body, explicitResources));
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
			syntaxResult,
			contentResult,
			fieldResult,
			insertSelfConsistencyResult,
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
		bool contentOk = IsContentValidationSuccessful(
			contentResult,
			fieldResult,
			insertSelfConsistencyResult,
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
		return BuildValidationResult(markerResult, syntaxResult, contentOk, errors, warnings);
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
		SchemaValidationResult syntaxResult,
		bool contentOk,
		List<string> errors,
		List<string> warnings) =>
		new() {
			MarkersOk = markerResult.IsValid,
			JsSyntaxOk = syntaxResult.IsValid,
			ContentOk = contentOk,
			Errors = errors.Count > 0 ? errors : null,
			Warnings = warnings.Count > 0 ? warnings : null
		};
}

/// <summary>
/// Top-level arguments for the <c>sync-pages</c> MCP tool.
/// </summary>
public sealed record PageSyncArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("pages")]
	[property: Description("Pages to update")]
	[property: Required]
	IEnumerable<PageSyncPageInput> Pages,

	[property: JsonPropertyName("validate")]
	[property: Description("Run client-side validation (markers and JS syntax) before saving. Default: true")]
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
	[property: Description("JSON object string of localizable string key-value pairs the platform does NOT auto-provide \u2014 e.g. custom tab/group titles, button captions, validator messages, and explicit overrides of inherited captions. IMPORTANT: only pass keys that have NO matching DS-bound view model attribute on the target page (or that intentionally override the inherited caption). Keys matching an existing DS-bound attribute are auto-provided by the platform from the entity column caption and MUST be omitted. See `page-schema-resources` guidance for the full check.")]
	string? Resources = null,
	[property: JsonPropertyName("optional-properties")]
	[property: Description("JSON array of {key, value} objects to merge into schema optionalProperties")]
	string? OptionalProperties = null
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
