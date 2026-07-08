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
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
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
		// `success` is the authoritative flag; `value` is an additional explicit-failure signal, so a save is
		// rejected when success is false OR value is explicitly false. A missing `value` (null) is not treated as
		// a failure — it is governed by `success` (matching the verified BuildConfiguration shape
		// {"errorInfo":null,"success":true}, whose success flag likewise governs).
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
		string responseBody = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration"),
			string.Empty);
		// This static-content rebuild is a SHARED, fire-and-forget refresh that runs AFTER the schema has already
		// been durably saved — both callers (BusinessRuleAddonService.AppendRules and RelatedPageAddonService.Create)
		// invoke it post-save, and neither can undo the committed save. So a rebuild failure must NOT throw: a
		// non-committal response (empty body, a non-JSON auth-redirect page, or a payload with no `success` flag) is
		// tolerated silently, and even an EXPLICIT `success:false` is surfaced as a WARNING rather than an exception —
		// throwing would report an already-committed create-*-business-rules / create-related-page-addon operation as
		// failed (a false negative). The change is persisted; the client static-content rebuild may need a retry.
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return;
		}
		AddonBuildResponseDto response;
		try {
			response = JsonSerializer.Deserialize<AddonBuildResponseDto>(responseBody, JsonOptions);
		} catch (JsonException) {
			return; // non-JSON body (e.g. an HTML/redirect page) carries no explicit failure signal — tolerate it
		}
		if (response?.Success == false) {
			logger.WriteWarning(
				"WorkspaceExplorerService.svc/BuildConfiguration reported a failure AFTER the schema was already saved: "
				+ (response.ErrorInfo?.Message ?? "static content rebuild failed")
				+ ". The change is persisted; the client static-content rebuild may need a manual retry.");
		}
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
