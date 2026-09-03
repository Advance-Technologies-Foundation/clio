using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Acornima.Ast;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageValidateTool(
	IMobileComponentInfoCatalog mobileComponentCatalog,
	IComponentInfoCatalog webComponentCatalog) {

	internal const string ToolName = "validate-page";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("Validates a Freedom UI page body client-side without saving to Creatio (markers, JS syntax, field/column bindings, handler/converter/validator structure for web; disallowed constructs, diff-apply, and placement checks for mobile — `type` must sit inside `values`, a `merge` must not author child elements in a Scaffold slot the template already fills, and a button in the Scaffold `actions` slot is flagged). " +
		"Run before update-page. See get-guidance `page-schema-converters` / `page-schema-handlers` / `page-schema-validators` / `mobile-page-modification` for the contracts it enforces.")]
	public async Task<PageValidateResponse> ValidatePage(
		[Description("Parameters: body or body-file (required); resources, version (optional)")]
		[Required] PageValidateArgs args,
		CancellationToken cancellationToken = default) {
		// `body-file` is the documented escape hatch for large bodies. Resolve it through the SAME loader the
		// save path uses (PageUpdateBodyLoader), so precedence and the file-not-found wording cannot drift
		// between validate-page and update-page (issue #1297).
		(bool bodyLoaded, string body, string bodyLoadError) =
			PageUpdateBodyLoader.TryResolveBody(args.Body, args.BodyFile);
		if (!bodyLoaded) {
			return InvalidRequest(bodyLoadError);
		}
		if (string.IsNullOrWhiteSpace(body)) {
			return InvalidRequest("Either 'body' or 'body-file' must provide page body content.");
		}
		args = args with { Body = body };
		// Mobile path: MobilePageValidation.RunAsync applies the diff sections through the faithful client-engine
		// clones (JsonDiffApplier / JsonPathDiffApplier) and returns any differ exception (e.g. a not-a-container
		// insert) to the caller for analysis — no heuristic body normalization.
		if (PageSchemaTypeExtensions.FromBody(args.Body) == PageSchemaType.Mobile) {
			SchemaValidationService.TryParseResources(args.Resources, out Dictionary<string, string>? mobileResources, out _);
			// No templateBaseContext: validate-page has no schema/environment identity, so the apply-oracle seeds
			// its own base. cancellationToken is now named (it moved past templateBaseContext, CA1068).
			PageSyncValidationResult mobileResult = await MobilePageValidation.RunAsync(
				args.Body, mobileComponentCatalog, webComponentCatalog, mobileResources,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			// Run-process button structure is a purely offline check (no environment), and validate-page is the
			// pre-flight the agent runs before update-page — so it must reach the same structural gate update-page
			// applies, otherwise a green validate-page misreads as "the button is wired" (ENG-95822). The mobile
			// apply-oracle does not cover it, so fold it in here for the mobile body too.
			SchemaValidationResult mobileRunProcessResult =
				SchemaValidationService.ValidateRunProcessButtonStructure(args.Body);
			if (!mobileRunProcessResult.IsValid) {
				mobileResult = FoldInContentErrors(mobileResult, mobileRunProcessResult);
			}
			return new PageValidateResponse {
				Valid = mobileResult.ContentOk,
				Validation = mobileResult
			};
		}
		PageSyncValidationResult result = Validate(args.Body, args.Resources);
		// Registry-driven chart-widget validation needs the (async, version-scoped) component catalog,
		// so it runs here rather than in the static content-validation pipeline. Fail-open inside.
		SchemaValidationResult chartResult =
			await ChartWidgetValidation.ValidateAsync(args.Body, webComponentCatalog, args.Version, cancellationToken).ConfigureAwait(false);
		if (!chartResult.IsValid) {
			result = FoldInContentErrors(result, chartResult);
		}
		// Same offline run-process structural gate on the web body — validate-page mirrors update-page (ENG-95822).
		SchemaValidationResult runProcessResult =
			SchemaValidationService.ValidateRunProcessButtonStructure(args.Body);
		if (!runProcessResult.IsValid) {
			result = FoldInContentErrors(result, runProcessResult);
		}
		return new PageValidateResponse {
			Valid = result.MarkersOk && result.JsSyntaxOk && result.ContentOk,
			Validation = result
		};
	}

	// Envelope for a request-shape rejection (no body supplied, or an unreadable body-file). Reported through
	// the normal validation envelope so a caller parsing `validation.errors` needs no second error shape.
	private static PageValidateResponse InvalidRequest(string error) =>
		new() {
			Valid = false,
			Validation = new PageSyncValidationResult {
				MarkersOk = false,
				JsSyntaxOk = false,
				ContentOk = false,
				Errors = [error]
			}
		};

	// Folds an extra content-validation result's errors into the envelope and forces ContentOk=false; shared by the
	// async chart-widget and run-process structural checks that run outside the static content-validation pipeline.
	private static PageSyncValidationResult FoldInContentErrors(
		PageSyncValidationResult result, SchemaValidationResult extraResult) {
		List<string> mergedErrors = result.Errors is null ? new List<string>() : new List<string>(result.Errors);
		mergedErrors.AddRange(extraResult.Errors);
		return new PageSyncValidationResult {
			MarkersOk = result.MarkersOk,
			JsSyntaxOk = result.JsSyntaxOk,
			ContentOk = false,
			Errors = mergedErrors.Count > 0 ? mergedErrors : null,
			Warnings = result.Warnings
		};
	}

	private static PageSyncValidationResult Validate(string body, string? resources) {
		// Run the deterministic syntax parser first — same gate as PageUpdateTool
		// / PageSyncTool so the pre-flight tool catches the production-incident
		// shape (`await X = Y`) instead of letting the regex syntax validator
		// pass it as syntax-OK.
		PageBodySyntaxValidationResult parserResult = PageBodySyntaxValidator.ValidateAndParse(body, out Script parsedAst);
		if (!parserResult.IsValid) {
			string syntaxError = PageBodySyntaxValidator.FormatError(parserResult);
			return new PageSyncValidationResult {
				MarkersOk = false,
				JsSyntaxOk = false,
				ContentOk = false,
				Errors = [syntaxError]
			};
		}
		SchemaValidationResult markerResult = SchemaValidationService.ValidateMarkerIntegrity(body);
		// The legacy brace-counter ValidateJsSyntax is intentionally NOT
		// called here. Reaching this line means Acornima already parsed the
		// body successfully (PageBodySyntaxValidator above), so JS syntax is
		// guaranteed valid — the brace-counter would be a dead check.
		// JsSyntaxOk is reported as true in BuildResult below.
		SchemaValidationResult contentResult = markerResult.IsValid
			? SchemaValidationService.ValidateMarkerContent(body)
			: new SchemaValidationResult { IsValid = true };
		Dictionary<string, string>? explicitResources = TryParseExplicitResources(resources, contentResult);
		ContentValidationResults content = RunContentValidations(body, contentResult, explicitResources);
		PageSyncValidationResult result = BuildResult(markerResult, contentResult, content);
		// Fold AST lint findings into the validation envelope — same source of
		// truth the write-path tools use. Error-severity findings demote
		// ContentOk to false and join the Errors[] list; Warning-severity
		// findings join the Warnings[] list.
		return FoldInLintFindings(result, parsedAst);
	}

	private static PageSyncValidationResult FoldInLintFindings(PageSyncValidationResult result, Script parsedAst) {
		if (parsedAst is null) {
			return result;
		}
		IReadOnlyList<PageBodyLintFinding> findings = PageBodyAstLinter.Lint(parsedAst);
		if (findings.Count == 0) {
			return result;
		}
		IReadOnlyList<PageBodyLintFinding> errors = findings.Where(f => f.Severity == LintSeverity.Error).ToArray();
		IReadOnlyList<PageBodyLintFinding> warnings = findings.Where(f => f.Severity == LintSeverity.Warning).ToArray();
		List<string> mergedErrors = result.Errors is null ? new List<string>() : new List<string>(result.Errors);
		List<string> mergedWarnings = result.Warnings is null ? new List<string>() : new List<string>(result.Warnings);
		if (errors.Count > 0) {
			mergedErrors.Add(PageBodyAstLinter.FormatErrors(errors));
		}
		if (warnings.Count > 0) {
			mergedWarnings.AddRange(warnings.Select(PageBodyAstLinter.FormatFinding));
		}
		return new PageSyncValidationResult {
			MarkersOk = result.MarkersOk,
			JsSyntaxOk = result.JsSyntaxOk,
			ContentOk = result.ContentOk && errors.Count == 0,
			Errors = mergedErrors.Count > 0 ? mergedErrors : null,
			Warnings = mergedWarnings.Count > 0 ? mergedWarnings : null
		};
	}

	private static ContentValidationResults RunContentValidations(
		string body,
		SchemaValidationResult contentResult,
		Dictionary<string, string>? explicitResources) =>
		new(
			Field: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateStandardFieldBindings(body, explicitResources)),
			InsertSelfConsistency: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateInsertedFieldSelfConsistency(body, explicitResources)),
			WidgetCaption: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateInsertedWidgetCaptionResources(body, explicitResources)),
			LocalizableText: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateLocalizableTextLiterals(body)),
			Binding: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateColumnBindings(body)),
			ConverterDecl: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateConverterDeclarations(body)),
			ConverterFunctionShape: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateConverterFunctionShape(body)),
			HandlerStructure: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateHandlerStructure(body)),
			ValidatorDecl: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateValidatorDeclarations(body)),
			ValidatorFactoryShape: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateCustomValidatorFactoryShape(body)),
			SchemaDeps: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateSchemaDepsCompleteness(body)),
			ContextAwait: RunContentValidation(contentResult,
				() => SchemaValidationService.ValidateContextAccessAwait(body)));

	private static PageSyncValidationResult BuildResult(
		SchemaValidationResult markerResult,
		SchemaValidationResult contentResult,
		ContentValidationResults content) {
		List<string> errors = CollectErrors(
			markerResult, contentResult,
			content.Field, content.InsertSelfConsistency, content.LocalizableText,
			content.ConverterDecl, content.ConverterFunctionShape,
			content.HandlerStructure, content.ValidatorDecl, content.ValidatorFactoryShape);
		var warnings = new List<string>();
		warnings.AddRange(content.Field.Warnings);
		if (!content.Binding.IsValid) {
			warnings.AddRange(content.Binding.Errors);
		}
		// Widget-caption resolvability is a body-only PRE-FLIGHT heuristic here (validate-page has no schema
		// context, so it cannot see keys a prior save already registered). Surface it as a warning; the
		// authoritative hard gate runs on the save path (PageUpdateCommand) against the final merged set.
		if (!content.WidgetCaption.IsValid) {
			warnings.AddRange(content.WidgetCaption.Errors);
		}
		warnings.AddRange(content.SchemaDeps.Warnings);
		warnings.AddRange(content.ContextAwait.Warnings);
		bool contentOk = contentResult.IsValid && content.Field.IsValid && content.InsertSelfConsistency.IsValid &&
			content.LocalizableText.IsValid &&
			content.ConverterDecl.IsValid &&
			content.ConverterFunctionShape.IsValid && content.HandlerStructure.IsValid &&
			content.ValidatorDecl.IsValid && content.ValidatorFactoryShape.IsValid;
		return new PageSyncValidationResult {
			MarkersOk = markerResult.IsValid,
			// Acornima already parsed the body successfully upstream, so JS
			// syntax is true unconditionally on this path. The dead
			// brace-counter ValidateJsSyntax call was removed.
			JsSyntaxOk = true,
			ContentOk = contentOk,
			Errors = errors.Count > 0 ? errors : null,
			Warnings = warnings.Count > 0 ? warnings : null
		};
	}

	private static Dictionary<string, string>? TryParseExplicitResources(
		string? resources, SchemaValidationResult contentResult) {
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

	private static List<string> CollectErrors(params SchemaValidationResult[] results) {
		var errors = new List<string>();
		foreach (SchemaValidationResult result in results) {
			if (!result.IsValid) {
				errors.AddRange(result.Errors);
			}
		}
		return errors;
	}

	private static SchemaValidationResult RunContentValidation(
		SchemaValidationResult contentResult,
		Func<SchemaValidationResult> validation) =>
		contentResult.IsValid ? validation() : new SchemaValidationResult { IsValid = true };

	private sealed record ContentValidationResults(
		SchemaValidationResult Field,
		SchemaValidationResult InsertSelfConsistency,
		SchemaValidationResult WidgetCaption,
		SchemaValidationResult LocalizableText,
		SchemaValidationResult Binding,
		SchemaValidationResult ConverterDecl,
		SchemaValidationResult ConverterFunctionShape,
		SchemaValidationResult HandlerStructure,
		SchemaValidationResult ValidatorDecl,
		SchemaValidationResult ValidatorFactoryShape,
		SchemaValidationResult SchemaDeps,
		SchemaValidationResult ContextAwait);
}

public sealed record PageValidateArgs(
	[property: JsonPropertyName("body")]
	[property: Description("Full JavaScript page body with markers. Pass `body` or `body-file`.")]
	string? Body,

	[property: JsonPropertyName("resources")]
	[property: Description(McpToolDescriptions.PageResources)]
	string? Resources = null,

	[property: JsonPropertyName("version")]
	[property: Description("Optional platform version (3-part semver, e.g. '8.3.3') scoping the registry-driven chart-widget (crt.ChartWidget) check. PREFER the resolvedTargetVersion from get-component-info for the same environment, so this pre-flight matches what update-page / sync-pages enforce on save. Omitted, or with no registry for that version, it falls back to the 'latest' catalog.")]
	string? Version = null,

	[property: JsonPropertyName("body-file")]
	[property: Description("Absolute path to a file holding the page body. Used when `body` is empty.")]
	string? BodyFile = null
);

public sealed class PageValidateResponse {

	[JsonPropertyName("valid")]
	public bool Valid { get; init; }

	[JsonPropertyName("validation")]
	public PageSyncValidationResult Validation { get; init; }
}
