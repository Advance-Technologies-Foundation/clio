using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
	IFileSystem fileSystem) {

	internal const string ToolName = "sync-pages";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Updates multiple Freedom UI page schemas in a single call. " +
		"For each page: validates body client-side (optional), runs AI semantic review (optional), saves to Creatio, " +
		"and verifies the update (optional). Continues processing remaining pages on failure. " +
		"Before authoring SCHEMA_VALIDATORS, call get-guidance with name `page-schema-validators` first.")]
	public async Task<PageSyncResponse> SyncPages(
		[Description("Parameters: environment-name (required); pages array (required); validate, verify, skip-sampling (optional)")]
		[Required] PageSyncArgs args,
		McpServerLib.McpServer server,
		CancellationToken cancellationToken = default) {
		var samplingResults = new Dictionary<string, PageSamplingReview>(StringComparer.Ordinal);
		if (args.SkipSampling != true) {
			foreach (PageSyncPageInput page in args.Pages) {
				samplingResults[page.SchemaName] = await PageBodySamplingService.TrySamplingReviewAsync(
					server, page.SchemaName, page.Body, cancellationToken);
			}
		}
		var results = new List<PageSyncPageResult>();
		bool validate = args.Validate ?? true;
		bool verify = args.Verify ?? false;
		lock (McpToolExecutionLock.SyncRoot) {
			try {
				PageUpdateOptions envOptions = new() { Environment = args.EnvironmentName };
				PageUpdateCommand updateCommand = commandResolver.Resolve<PageUpdateCommand>(envOptions);
				PageGetCommand getCommand = verify
					? commandResolver.Resolve<PageGetCommand>(
						new PageGetOptions { Environment = args.EnvironmentName })
					: null;
				foreach (PageSyncPageInput page in args.Pages) {
					samplingResults.TryGetValue(page.SchemaName, out PageSamplingReview samplingReview);
					PageSyncPageResult pageResult = SyncSinglePage(
						page, updateCommand, getCommand, validate, verify, samplingReview);
					results.Add(pageResult);
				}
				Thread.Sleep(500);
			} catch (Exception ex) {
				foreach (PageSyncPageInput page in args.Pages) {
					if (results.All(r => r.SchemaName != page.SchemaName)) {
						results.Add(new PageSyncPageResult {
							SchemaName = page.SchemaName,
							Success = false,
							Error = ex.Message
						});
					}
				}
			}
		}
		return new PageSyncResponse {
			Success = results.Count > 0 && results.All(r => r.Success),
			Pages = results
		};
	}

	private PageSyncPageResult SyncSinglePage(
		PageSyncPageInput page,
		PageUpdateCommand updateCommand,
		PageGetCommand getCommand,
		bool validate,
		bool verify,
		PageSamplingReview samplingReview) {
		try {
			PageSyncValidationResult validationResult = null;
			if (validate) {
				validationResult = ValidateBody(page.Body, page.Resources);
				if (!validationResult.MarkersOk || !validationResult.JsSyntaxOk || !validationResult.ContentOk) {
					return new PageSyncPageResult {
						SchemaName = page.SchemaName,
						Success = false,
						Validation = validationResult,
						SamplingReview = samplingReview,
						Error = "Client-side validation failed: " +
							string.Join("; ", validationResult.Errors ?? Array.Empty<string>())
					};
				}
			}
			if (samplingReview is { Ok: false, Skipped: false } && samplingReview.Issues?.Count > 0) {
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					SamplingReview = samplingReview,
					Error = "Sampling review found issues: " + string.Join("; ", samplingReview.Issues)
				};
			}
			PageUpdateOptions updateOptions = new() {
				SchemaName = page.SchemaName,
				Body = page.Body,
				DryRun = false,
				Resources = page.Resources,
				OptionalProperties = page.OptionalProperties
			};
			updateCommand.TryUpdatePage(updateOptions, out PageUpdateResponse updateResponse);
			if (!updateResponse.Success) {
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = false,
					Validation = validationResult,
					Error = updateResponse.Error
				};
			}
			if (verify && getCommand != null) {
				PageGetOptions getOptions = new() { SchemaName = page.SchemaName };
				getCommand.TryGetPage(getOptions, out PageGetResponse getResponse);
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
					string schemaDir = fileSystem.Path.Combine(
						fileSystem.Directory.GetCurrentDirectory(), ".clio-pages", page.SchemaName);
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
					SamplingReview = samplingReview,
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
				SamplingReview = samplingReview,
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

	private static PageSyncValidationResult ValidateBody(string body, string? resources) {
		SchemaValidationResult markerResult = SchemaValidationService.ValidateMarkerIntegrity(body);
		SchemaValidationResult syntaxResult = SchemaValidationService.ValidateJsSyntax(body);
		SchemaValidationResult contentResult = GetContentValidationResult(body, markerResult);
		Dictionary<string, string>? explicitResources = TryParseExplicitResources(resources, contentResult);
		SchemaValidationResult fieldResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateStandardFieldBindings(body, explicitResources));
		SchemaValidationResult validatorBindingResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateValidatorControlBindings(body));
		SchemaValidationResult validatorParamResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateValidatorParamResourceBindings(body));
		SchemaValidationResult standardValidatorResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateStandardValidatorUsage(body));
		SchemaValidationResult validatorParamCompletenessResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateCustomValidatorParamCompleteness(body));
		SchemaValidationResult bindingResult = RunContentValidation(
			contentResult, () => SchemaValidationService.ValidateColumnBindings(body));
		List<string> errors = CollectErrors(
			markerResult,
			syntaxResult,
			contentResult,
			fieldResult,
			validatorBindingResult,
			validatorParamResult,
			standardValidatorResult,
			validatorParamCompletenessResult);
		List<string> warnings = CollectWarnings(fieldResult, bindingResult);
		bool contentOk = IsContentValidationSuccessful(
			contentResult,
			fieldResult,
			validatorBindingResult,
			validatorParamResult,
			standardValidatorResult,
			validatorParamCompletenessResult);
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
		SchemaValidationResult bindingResult) {
		var warnings = new List<string>();
		if (fieldResult.Warnings.Count > 0) {
			warnings.AddRange(fieldResult.Warnings);
		}

		if (!bindingResult.IsValid) {
			warnings.AddRange(bindingResult.Errors);
		}

		return warnings;
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
	[property: Description("If true, skip AI semantic review before saving. Default: false")]
	bool? SkipSampling = null
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
	[property: Description("JSON object string of resource key-value pairs for #ResourceString(key)# macros")]
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
