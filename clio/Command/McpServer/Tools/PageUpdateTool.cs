using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageUpdateTool(
	PageUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PageUpdateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "update-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Update Freedom UI page schema body. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows. " +
		"Section authoring rules for the body payload: " +
		"if the body changes SCHEMA_HANDLERS call get-guidance with name `page-schema-handlers` first; " +
		"if the body changes SCHEMA_VALIDATORS call get-guidance with name `page-schema-validators` first; " +
		"if the body adds or edits `@creatio-devkit/common` usage call get-guidance with name `page-schema-sdk-common` before editing SCHEMA_DEPS or SDK calls.")]
	public PageUpdateResponse UpdatePage([Description("Parameters: schema-name, body (required); resources, dry-run (optional); environment-name preferred; uri/login/password emergency fallback only.")] [Required] PageUpdateArgs args) {
		PageUpdateOptions options = new() {
			SchemaName = args.SchemaName,
			Body = args.Body,
			DryRun = args.DryRun ?? false,
			Resources = args.Resources,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		var validationErrors = new List<string>();
		SchemaValidationResult handlerResult = SchemaValidationService.ValidateHandlerStructure(args.Body);
		if (!handlerResult.IsValid) {
			validationErrors.AddRange(handlerResult.Errors);
		}
		SchemaValidationResult validatorParamResult = SchemaValidationService.ValidateValidatorParamResourceBindings(args.Body);
		if (!validatorParamResult.IsValid) {
			validationErrors.AddRange(validatorParamResult.Errors);
		}
		SchemaValidationResult validatorBindingResult = SchemaValidationService.ValidateValidatorControlBindings(args.Body);
		if (!validatorBindingResult.IsValid) {
			validationErrors.AddRange(validatorBindingResult.Errors);
		}
		SchemaValidationResult validatorPlacementResult = SchemaValidationService.ValidateValidatorBindingPlacement(args.Body);
		if (!validatorPlacementResult.IsValid) {
			validationErrors.AddRange(validatorPlacementResult.Errors);
		}
		SchemaValidationResult standardValidatorResult = SchemaValidationService.ValidateStandardValidatorUsage(args.Body);
		if (!standardValidatorResult.IsValid) {
			validationErrors.AddRange(standardValidatorResult.Errors);
		}
		SchemaValidationResult validatorParamCompletenessResult = SchemaValidationService.ValidateCustomValidatorParamCompleteness(args.Body);
		if (!validatorParamCompletenessResult.IsValid) {
			validationErrors.AddRange(validatorParamCompletenessResult.Errors);
		}
		if (validationErrors.Count > 0) {
			return new PageUpdateResponse {
				Success = false,
				Error = "Validation failed: " + string.Join("; ", validationErrors)
			};
		}
		lock (CommandExecutionSyncRoot) {
			PageUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageUpdateCommand>(options);
			} catch (Exception ex) {
				return new PageUpdateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryUpdatePage(options, out PageUpdateResponse response);
			return response;
		}
	}
}

public sealed record PageUpdateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name, e.g. 'UsrMyApp_FormPage'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("body")]
	[property: Description("Full JavaScript page body with markers. Reuse get-page raw.body.")]
	[property: Required]
	string Body,

	[property: JsonPropertyName("resources")]
	[property: Description("JSON object string of resource key-value pairs for #ResourceString(key)# macros in the body, e.g. '{\"UsrDetailsTab_caption\": \"Details\"}'. Unresolved macros are auto-registered with captions derived from key names.")]
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
	string? Password
);
