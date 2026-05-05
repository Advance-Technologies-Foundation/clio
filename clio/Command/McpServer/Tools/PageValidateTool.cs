using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageValidateTool {

	internal const string ToolName = "validate-page";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("Validates a Freedom UI page body client-side without saving to Creatio. " +
		"Checks marker integrity, JS syntax, JSON content, field bindings, column bindings, " +
		"and type key format (SCHEMA_CONVERTERS, SCHEMA_HANDLERS, SCHEMA_VALIDATORS keys must follow VendorPrefix.Name — " +
		"read get-guidance `page-schema-converters`, `page-schema-handlers`, or `page-schema-validators` before adding them).")]
	public PageValidateResponse ValidatePage(
		[Description("Parameters: body (required); resources (optional)")]
		[Required] PageValidateArgs args) {
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
		Dictionary<string, string>? explicitResources = null;
		if (contentResult.IsValid &&
		    !SchemaValidationService.TryParseResources(resources, out explicitResources, out _)) {
			contentResult.IsValid = false;
			contentResult.Errors.Add("resources must be a valid JSON object string");
		}
		SchemaValidationResult fieldResult = RunContentValidation(contentResult,
			() => SchemaValidationService.ValidateStandardFieldBindings(body, explicitResources));
		SchemaValidationResult bindingResult = RunContentValidation(contentResult,
			() => SchemaValidationService.ValidateColumnBindings(body));
		SchemaValidationResult converterDeclResult = RunContentValidation(contentResult,
			() => SchemaValidationService.ValidateConverterDeclarations(body));
		SchemaValidationResult handlerDeclResult = RunContentValidation(contentResult,
			() => SchemaValidationService.ValidateHandlerDeclarations(body));
		SchemaValidationResult validatorDeclResult = RunContentValidation(contentResult,
			() => SchemaValidationService.ValidateValidatorDeclarations(body));
		var errors = new List<string>();
		var warnings = new List<string>();
		if (!markerResult.IsValid) errors.AddRange(markerResult.Errors);
		if (!syntaxResult.IsValid) errors.AddRange(syntaxResult.Errors);
		if (!contentResult.IsValid) errors.AddRange(contentResult.Errors);
		if (!fieldResult.IsValid) errors.AddRange(fieldResult.Errors);
		if (fieldResult.Warnings.Count > 0) warnings.AddRange(fieldResult.Warnings);
		if (!bindingResult.IsValid) warnings.AddRange(bindingResult.Errors);
		if (!converterDeclResult.IsValid) errors.AddRange(converterDeclResult.Errors);
		if (!handlerDeclResult.IsValid) errors.AddRange(handlerDeclResult.Errors);
		if (!validatorDeclResult.IsValid) errors.AddRange(validatorDeclResult.Errors);
		return new PageSyncValidationResult {
			MarkersOk = markerResult.IsValid,
			JsSyntaxOk = syntaxResult.IsValid,
			ContentOk = contentResult.IsValid && fieldResult.IsValid && converterDeclResult.IsValid && 
				handlerDeclResult.IsValid && validatorDeclResult.IsValid,
			Errors = errors.Count > 0 ? errors : null,
			Warnings = warnings.Count > 0 ? warnings : null
		};
	}

	private static SchemaValidationResult RunContentValidation(
		SchemaValidationResult contentResult,
		Func<SchemaValidationResult> validation) =>
		contentResult.IsValid ? validation() : new SchemaValidationResult { IsValid = true };
}

public sealed record PageValidateArgs(
	[property: JsonPropertyName("body")]
	[property: Description("Full JavaScript page body with markers")]
	[property: Required]
	string Body,

	[property: JsonPropertyName("resources")]
	[property: Description("JSON object string of resource key-value pairs for #ResourceString(key)# macros")]
	string? Resources = null
);

public sealed class PageValidateResponse {

	[JsonPropertyName("valid")]
	public bool Valid { get; init; }

	[JsonPropertyName("validation")]
	public PageSyncValidationResult Validation { get; init; }
}
