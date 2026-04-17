using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using McpServerLib = ModelContextProtocol.Server;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageListColumnsTool(IToolCommandResolver commandResolver) {

	[McpServerTool(Name = "add-list-columns", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Add columns to an existing Freedom UI list page body. Reads the current body, inserts the new columns into the DataTable, and saves.")]
	public async Task<PageAddColumnsResponse> AddListColumns(
		[Description("Parameters: schema-name and columns (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] PageListColumnsArgs args,
		McpServerLib.McpServer server,
		CancellationToken cancellationToken = default) {
		string rawBody;
		lock (McpToolExecutionLock.SyncRoot) {
			var getOptions = new PageGetOptions {
				SchemaName = args.SchemaName,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			PageGetCommand getCommand;
			try {
				getCommand = commandResolver.Resolve<PageGetCommand>(getOptions);
			} catch (Exception ex) {
				return new PageAddColumnsResponse { Success = false, Error = ex.Message };
			}
			getCommand.TryGetPage(new PageGetOptions { SchemaName = args.SchemaName }, out PageGetResponse getResponse);
			if (!getResponse.Success || getResponse.Raw?.Body == null)
				return new PageAddColumnsResponse { Success = false, Error = getResponse.Error ?? "Failed to read page body" };
			rawBody = getResponse.Raw.Body;
		}
		string editedBody;
		try {
			editedBody = PageBodyEditor.AddListColumns(rawBody, args.Columns);
		} catch (Exception ex) {
			return new PageAddColumnsResponse { Success = false, Error = ex.Message };
		}
		PageSamplingReview samplingReview = null;
		if (args.SkipSampling != true) {
			samplingReview = await PageBodySamplingService.TrySamplingReviewAsync(server, args.SchemaName, editedBody, cancellationToken);
			if (samplingReview is { Ok: false, Skipped: false } && samplingReview.Issues?.Count > 0) {
				return new PageAddColumnsResponse {
					Success = false,
					Error = "Sampling review found issues: " + string.Join("; ", samplingReview.Issues),
					SamplingReview = samplingReview
				};
			}
		}
		PageUpdateResponse saveResponse;
		lock (McpToolExecutionLock.SyncRoot) {
			var updateOptions = new PageUpdateOptions {
				SchemaName = args.SchemaName,
				Body = editedBody,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			PageUpdateCommand updateCommand;
			try {
				updateCommand = commandResolver.Resolve<PageUpdateCommand>(updateOptions);
			} catch (Exception ex) {
				return new PageAddColumnsResponse { Success = false, Error = ex.Message };
			}
			updateCommand.TryUpdatePage(updateOptions, out saveResponse);
		}
		return new PageAddColumnsResponse {
			Success = saveResponse.Success,
			SchemaName = args.SchemaName,
			BodyLength = saveResponse.BodyLength,
			ColumnsAdded = saveResponse.Success ? args.Columns.Count : 0,
			Error = saveResponse.Error,
			SamplingReview = samplingReview
		};
	}
}

public sealed record PageListColumnsArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI list page schema name, e.g. 'UsrMyApp_ListPage'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("columns")]
	[property: Description("Array of column specs to add. Each requires 'code' (e.g. 'PDS_UsrName') and 'data-value-type' (Creatio DataValueType int).")]
	[property: Required]
	IReadOnlyList<ListColumnSpec> Columns,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name. Preferred for normal MCP work.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Emergency fallback only.")]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with uri. Emergency fallback only.")]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with uri. Emergency fallback only.")]
	string? Password = null,

	[property: JsonPropertyName("skip-sampling")]
	[property: Description("If true, skip AI semantic review before saving. Default: false")]
	bool? SkipSampling = null
);
