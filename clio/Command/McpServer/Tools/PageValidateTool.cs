using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageValidateTool(
	IMobileComponentInfoCatalog mobileComponentCatalog,
	IComponentInfoCatalog webComponentCatalog) {

	internal const string ToolName = "validate-page";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("Validates a Freedom UI page body client-side without saving to Creatio. " +
		"For web pages: checks marker integrity, JS syntax, JSON content, field bindings, column bindings, " +
		"handler structure (SCHEMA_HANDLERS must be an array of {request, handler} entries), " +
		"and VendorPrefix.Name format for SCHEMA_CONVERTERS / SCHEMA_VALIDATORS keys and SCHEMA_HANDLERS entry `request` values — " +
		"read get-guidance `page-schema-converters`, `page-schema-handlers`, or `page-schema-validators` before adding them. " +
		"For mobile pages (plain JSON body starting with '{'): validates that disallowed constructs " +
		"(validators, handlers, custom converters sections) are absent.")]
	public PageValidateResponse ValidatePage(
		[Description("Parameters: body (required); resources (optional)")]
		[Required] PageValidateArgs args) {
		if (PageSchemaTypeExtensions.FromBody(args.Body) == PageSchemaType.Mobile) {
			SchemaValidationService.TryParseResources(args.Resources, out Dictionary<string, string>? mobileResources, out _);
			PageSyncValidationResult mobileResult = MobilePageValidation.Run(
				args.Body, mobileComponentCatalog, webComponentCatalog, mobileResources);
			return new PageValidateResponse {
				Valid = mobileResult.ContentOk,
				Validation = mobileResult
			};
		}
		PageSyncValidationResult result = Validate(args.Body, args.Resources);
		return new PageValidateResponse {
			Valid = result.MarkersOk && result.JsSyntaxOk && result.ContentOk,
			Validation = result
		};
	}

	private static PageSyncValidationResult Validate(string body, string? resources) {
		SchemaValidationResult markerResult = SchemaValidationService.ValidateMarkerIntegrity(body);
		SchemaValidationResult syntaxResult = SchemaValidationService.ValidateJsSyntax(body);
		SchemaValidationResult contentResult = markerResult.IsValid
			? SchemaValidationService.ValidateMarkerContent(body)
			: new SchemaValidationResult { IsValid = true };
		Dictionary<string, string>? explicitResources = TryParseExplicitResources(resources, contentResult);
		ContentValidationResults content = RunContentValidations(body, contentResult, explicitResources);
		return BuildResult(markerResult, syntaxResult, contentResult, content);
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
				() => SchemaValidationService.ValidateSchemaDepsCompleteness(body)));

	private static PageSyncValidationResult BuildResult(
		SchemaValidationResult markerResult,
		SchemaValidationResult syntaxResult,
		SchemaValidationResult contentResult,
		ContentValidationResults content) {
		List<string> errors = CollectErrors(
			markerResult, syntaxResult, contentResult,
			content.Field, content.InsertSelfConsistency, content.ConverterDecl, content.ConverterFunctionShape,
			content.HandlerStructure, content.ValidatorDecl, content.ValidatorFactoryShape);
		var warnings = new List<string>();
		warnings.AddRange(content.Field.Warnings);
		if (!content.Binding.IsValid) {
			warnings.AddRange(content.Binding.Errors);
		}
		warnings.AddRange(content.SchemaDeps.Warnings);
		bool contentOk = contentResult.IsValid && content.Field.IsValid && content.InsertSelfConsistency.IsValid &&
			content.ConverterDecl.IsValid &&
			content.ConverterFunctionShape.IsValid && content.HandlerStructure.IsValid &&
			content.ValidatorDecl.IsValid && content.ValidatorFactoryShape.IsValid;
		return new PageSyncValidationResult {
			MarkersOk = markerResult.IsValid,
			JsSyntaxOk = syntaxResult.IsValid,
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
		SchemaValidationResult Binding,
		SchemaValidationResult ConverterDecl,
		SchemaValidationResult ConverterFunctionShape,
		SchemaValidationResult HandlerStructure,
		SchemaValidationResult ValidatorDecl,
		SchemaValidationResult ValidatorFactoryShape,
		SchemaValidationResult SchemaDeps);
}

public sealed record PageValidateArgs(
	[property: JsonPropertyName("body")]
	[property: Description("Full JavaScript page body with markers")]
	[property: Required]
	string Body,

	[property: JsonPropertyName("resources")]
	[property: Description("JSON object string of localizable string key-value pairs the platform does NOT auto-provide (custom tab/group titles, button captions, validator messages, explicit caption overrides). IMPORTANT: only pass keys that have NO matching DS-bound view model attribute on the target page (or that intentionally override the inherited caption). Keys matching an existing DS-bound attribute are auto-provided by the platform and MUST be omitted. See `page-schema-resources` guidance for the full check.")]
	string? Resources = null
);

public sealed class PageValidateResponse {

	[JsonPropertyName("valid")]
	public bool Valid { get; init; }

	[JsonPropertyName("validation")]
	public PageSyncValidationResult Validation { get; init; }
}
