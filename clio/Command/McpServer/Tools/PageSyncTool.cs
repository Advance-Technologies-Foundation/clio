using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that batches multiple Freedom UI page updates into a single call,
/// reducing MCP round-trips, lock acquisitions, and sleep overhead.
/// </summary>
[McpServerToolType]
public sealed class PageSyncTool(
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "sync-pages";

	/// <summary>
	/// Updates multiple Freedom UI pages in a single MCP call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Updates multiple Freedom UI page schemas in a single call. " +
		"For each page: validates body client-side (optional), saves to Creatio, " +
		"and verifies the update (optional). Continues processing remaining pages on failure.")]
	public PageSyncResponse SyncPages(
		[Description("Parameters: environment-name (required); pages array (required); validate, verify (optional)")]
		[Required] PageSyncArgs args) {
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
					PageSyncPageResult pageResult = SyncSinglePage(
						page, updateCommand, getCommand, validate, verify);
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

	private static PageSyncPageResult SyncSinglePage(
		PageSyncPageInput page,
		PageUpdateCommand updateCommand,
		PageGetCommand getCommand,
		bool validate,
		bool verify) {
		try {
			PageSyncValidationResult validationResult = null;
			if (validate) {
				validationResult = ValidateBody(page.Body, page.Resources);
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
			PageUpdateOptions updateOptions = new() {
				SchemaName = page.SchemaName,
				Body = page.Body,
				DryRun = false,
				Resources = page.Resources
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
				return new PageSyncPageResult {
					SchemaName = page.SchemaName,
					Success = true,
					BodyLength = updateResponse.BodyLength,
					Validation = validationResult,
					ResourcesRegistered = updateResponse.ResourcesRegistered,
					Page = getResponse.Page,
					VerifiedBody = getResponse.Raw?.Body
				};
			}
			return new PageSyncPageResult {
				SchemaName = page.SchemaName,
				Success = true,
				BodyLength = updateResponse.BodyLength,
				Validation = validationResult,
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
		SchemaValidationResult contentResult = markerResult.IsValid
			? SchemaValidationService.ValidateMarkerContent(body)
			: new SchemaValidationResult { IsValid = true };
		Dictionary<string, string>? explicitResources = null;
		if (contentResult.IsValid &&
		    !SchemaValidationService.TryParseResources(resources, out explicitResources, out _)) {
			contentResult.IsValid = false;
			contentResult.Errors.Add("resources must be a valid JSON object string");
		}
		SchemaValidationResult fieldResult = contentResult.IsValid
			? SchemaValidationService.ValidateStandardFieldBindings(body, explicitResources)
			: new SchemaValidationResult { IsValid = true };
		SchemaValidationResult bindingResult = contentResult.IsValid
			? SchemaValidationService.ValidateColumnBindings(body)
			: new SchemaValidationResult { IsValid = true };
		var errors = new List<string>();
		var warnings = new List<string>();
		if (!markerResult.IsValid) {
			errors.AddRange(markerResult.Errors);
		}
		if (!syntaxResult.IsValid) {
			errors.AddRange(syntaxResult.Errors);
		}
		if (!contentResult.IsValid) {
			errors.AddRange(contentResult.Errors);
		}
		if (!fieldResult.IsValid) {
			errors.AddRange(fieldResult.Errors);
		}
		if (fieldResult.Warnings.Count > 0) {
			warnings.AddRange(fieldResult.Warnings);
		}
		if (!bindingResult.IsValid) {
			warnings.AddRange(bindingResult.Errors);
		}
		return new PageSyncValidationResult {
			MarkersOk = markerResult.IsValid,
			JsSyntaxOk = syntaxResult.IsValid,
			ContentOk = contentResult.IsValid && fieldResult.IsValid,
			Errors = errors.Count > 0 ? errors : null,
			Warnings = warnings.Count > 0 ? warnings : null
		};
	}
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
	bool? Verify = null
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
	[property: Description("Full JavaScript page body copied from get-page raw.body")]
	[property: Required]
	string Body,

	[property: JsonPropertyName("resources")]
	[property: Description("JSON object string of resource key-value pairs for #ResourceString(key)# macros")]
	string? Resources = null
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

	[JsonPropertyName("verified-body")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string VerifiedBody { get; init; }
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
