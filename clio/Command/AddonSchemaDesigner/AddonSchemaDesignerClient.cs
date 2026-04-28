using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.AddonSchemaDesigner;

internal interface IAddonSchemaDesignerClient {
	AddonSchemaDto GetSchema(AddonGetRequestDto request);
	void SaveSchema(AddonSchemaDto schema);
	void ResetClientScriptCache();
	void BuildConfiguration();
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

	/// <summary>
	/// Clears the server-side RequireJS module script cache for the current session,
	/// making saved schema changes available immediately without a full reload.
	/// </summary>
	public void ResetClientScriptCache() {
		applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache"),
			string.Empty);
	}

	/// <summary>
	/// Triggers an incremental static content rebuild on the server (no C# compilation).
	/// Regenerates client JS files for changed schemas, broadcasts a
	/// <c>ConfigurationStructureChanged</c> event to online users, and writes a new
	/// <c>ConfigurationHash</c> to disk so offline users get cache invalidation on
	/// their next startup via <c>/api/ClientCache/Hashes</c>.
	/// </summary>
	public void BuildConfiguration() {
		applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration"),
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
