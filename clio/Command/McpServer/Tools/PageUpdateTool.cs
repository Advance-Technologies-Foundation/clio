using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using McpServerLib = ModelContextProtocol.Server;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageUpdateTool(
	PageUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PageUpdateOptions>(command, logger, commandResolver) {

	private readonly IToolCommandResolver _commandResolver = commandResolver;

	internal const string ToolName = "update-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Update Freedom UI page schema body. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public async Task<PageUpdateResponse> UpdatePage(
		[Description("Parameters: schema-name, body (required); resources, dry-run, skip-sampling (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] PageUpdateArgs args,
		McpServerLib.McpServer server,
		CancellationToken cancellationToken = default) {
		PageUpdateOptions options = new() {
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
			Password = args.Password
		};
		if (!string.IsNullOrWhiteSpace(options.Body)) {
			var validationErrors = new List<string>();
			SchemaValidationResult validatorParamResult = SchemaValidationService.ValidateValidatorParamResourceBindings(options.Body);
			if (!validatorParamResult.IsValid) validationErrors.AddRange(validatorParamResult.Errors);
			SchemaValidationResult validatorBindingResult = SchemaValidationService.ValidateValidatorControlBindings(options.Body);
			if (!validatorBindingResult.IsValid) validationErrors.AddRange(validatorBindingResult.Errors);
			SchemaValidationResult standardValidatorResult = SchemaValidationService.ValidateStandardValidatorUsage(options.Body);
			if (!standardValidatorResult.IsValid) validationErrors.AddRange(standardValidatorResult.Errors);
			SchemaValidationResult validatorParamCompletenessResult = SchemaValidationService.ValidateCustomValidatorParamCompleteness(options.Body);
			if (!validatorParamCompletenessResult.IsValid) validationErrors.AddRange(validatorParamCompletenessResult.Errors);
			if (validationErrors.Count > 0) {
				return new PageUpdateResponse {
					Success = false,
					Error = "Validation failed: " + string.Join("; ", validationErrors)
				};
			}
		}
		PageSamplingReview samplingReview = null;
		if (!options.DryRun && args.SkipSampling != true) {
			samplingReview = await PageBodySamplingService.TrySamplingReviewAsync(server, args.SchemaName, args.Body, cancellationToken);
			if (samplingReview is { Ok: false, Skipped: false } && samplingReview.Issues?.Count > 0) {
				return new PageUpdateResponse {
					Success = false,
					Error = "Sampling review found issues: " + string.Join("; ", samplingReview.Issues),
					SamplingReview = samplingReview
				};
			}
		}
		PageUpdateResponse response;
		lock (CommandExecutionSyncRoot) {
			PageUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageUpdateCommand>(options);
			} catch (Exception ex) {
				return new PageUpdateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryUpdatePage(options, out response);
			if (response.Success && args.Verify == true) {
				try {
					PageGetOptions getOptions = new() {
						SchemaName = args.SchemaName,
						Environment = args.EnvironmentName,
						Uri = args.Uri,
						Login = args.Login,
						Password = args.Password
					};
					PageGetCommand getCommand = _commandResolver.Resolve<PageGetCommand>(getOptions);
					if (getCommand.TryGetPage(getOptions, out PageGetResponse getResponse) && getResponse.Success) {
						response.Page = getResponse.Page;
					}
				} catch {
					// verify is best-effort; failure does not fail the update
				}
			}
		}
		response.SamplingReview = samplingReview;
		return response;
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
	string? Password,
	[property: JsonPropertyName("skip-sampling")]
	[property: Description("If true, skip the AI semantic review before saving. Default: false")]
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
	string? TargetSchemaUId = null
);
