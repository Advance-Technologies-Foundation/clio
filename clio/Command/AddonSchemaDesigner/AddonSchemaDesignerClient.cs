using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.AddonSchemaDesigner;

internal interface IAddonSchemaDesignerClient {
	AddonSchemaDto GetSchema(AddonGetRequestDto request);
	void SaveSchema(AddonSchemaDto schema);
	void ResetClientScriptCache();
}

internal sealed class AddonSchemaDesignerClient(
	IApplicationClient applicationClient,
	IJsonConverter jsonConverter,
	IServiceUrlBuilder serviceUrlBuilder)
	: IAddonSchemaDesignerClient {

	private const string DesignerServicePath = "ServiceModel/AddonSchemaDesignerService.svc";
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	public AddonSchemaDto GetSchema(AddonGetRequestDto request) {
		ArgumentNullException.ThrowIfNull(request);

		string responseBody = applicationClient.ExecutePostRequest(
			BuildDesignerMethodUrl("GetSchema"),
			JsonSerializer.Serialize(request, JsonOptions));
		AddonSchemaResponseDto response = Deserialize<AddonSchemaResponseDto>(
			responseBody,
			"AddonSchemaDesignerService returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "AddonSchemaDesignerService.GetSchema failed.");
		}

		return response.Schema
			?? throw new InvalidOperationException("AddonSchemaDesignerService did not return a schema payload.");
	}

	public void SaveSchema(AddonSchemaDto schema) {
		ArgumentNullException.ThrowIfNull(schema);

		string responseBody = applicationClient.ExecutePostRequest(
			BuildDesignerMethodUrl("SaveSchema"),
			JsonSerializer.Serialize(schema, JsonOptions));
		AddonSaveResponseDto response = Deserialize<AddonSaveResponseDto>(
			responseBody,
			"AddonSchemaDesignerService.SaveSchema returned an empty response.");
		if (!response.Success || response.Value == false) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "AddonSchemaDesignerService.SaveSchema failed.");
		}
	}

	public void ResetClientScriptCache() {
		applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache"),
			string.Empty);
	}

	private string BuildDesignerMethodUrl(string methodName) {
		string baseUrl = serviceUrlBuilder.Build(DesignerServicePath);
		return $"{baseUrl}/{methodName}";
	}

	private T Deserialize<T>(string responseBody, string emptyMessage) {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			throw new InvalidOperationException(emptyMessage);
		}

		try {
			return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
				?? throw new InvalidOperationException(emptyMessage);
		} catch (JsonException) {
			string correctedJson = jsonConverter.CorrectJson(responseBody);
			return JsonSerializer.Deserialize<T>(correctedJson, JsonOptions)
				?? throw new InvalidOperationException(emptyMessage);
		}
	}
}
