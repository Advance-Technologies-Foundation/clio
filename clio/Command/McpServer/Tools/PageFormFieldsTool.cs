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
public sealed class PageFormFieldsTool(IToolCommandResolver commandResolver) {

	[McpServerTool(Name = "add-form-fields", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Add form fields to an existing Freedom UI form page body. Reads the current body, inserts the new fields, and saves. " +
		"Use when you need to add crt.Input, crt.ComboBox, crt.DateTimePicker, etc. to a form page without replacing the whole body.")]
	public async Task<PageAddFieldsResponse> AddFormFields(
		[Description("Parameters: schema-name and fields (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] PageFormFieldsArgs args,
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
				return new PageAddFieldsResponse { Success = false, Error = ex.Message };
			}
			getCommand.TryGetPage(new PageGetOptions { SchemaName = args.SchemaName }, out PageGetResponse getResponse);
			if (!getResponse.Success || getResponse.Raw?.Body == null)
				return new PageAddFieldsResponse { Success = false, Error = getResponse.Error ?? "Failed to read page body" };
			rawBody = getResponse.Raw.Body;
		}
		string editedBody;
		try {
			editedBody = PageBodyEditor.AddFormFields(rawBody, args.Fields);
		} catch (Exception ex) {
			return new PageAddFieldsResponse { Success = false, Error = ex.Message };
		}
		PageSamplingReview samplingReview = null;
		if (args.SkipSampling != true) {
			samplingReview = await PageBodySamplingService.TrySamplingReviewAsync(server, args.SchemaName, editedBody, cancellationToken);
			if (samplingReview is { Ok: false, Skipped: false } && samplingReview.Issues?.Count > 0)
				return new PageAddFieldsResponse {
					Success = false,
					Error = "Sampling review found issues: " + string.Join("; ", samplingReview.Issues),
					SamplingReview = samplingReview
				};
		}
		var (saveResponse, resolveError) = PageEditToolHelpers.TryExecuteSaveBody(
			commandResolver, args.SchemaName, editedBody,
			args.EnvironmentName, args.Uri, args.Login, args.Password, args.Resources);
		if (saveResponse is null)
			return new PageAddFieldsResponse { Success = false, Error = resolveError };
		return new PageAddFieldsResponse {
			Success = saveResponse.Success,
			SchemaName = args.SchemaName,
			BodyLength = saveResponse.BodyLength,
			FieldsAdded = saveResponse.Success ? args.Fields.Count : 0,
			ResourcesRegistered = saveResponse.Success ? saveResponse.ResourcesRegistered : 0,
			Error = saveResponse.Error,
			SamplingReview = samplingReview
		};
	}
}

public sealed record PageFormFieldsArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI form page schema name, e.g. 'UsrMyApp_FormPage'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("fields")]
	[property: Description("Array of field specs to add. Each requires 'path' (e.g. 'PDS.UsrName') and 'type' (e.g. 'crt.Input').")]
	[property: Required]
	IReadOnlyList<FormFieldSpec> Fields
) : PageEditToolArgs;
