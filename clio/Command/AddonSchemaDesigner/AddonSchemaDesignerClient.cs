using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.AddonSchemaDesigner;

internal interface IAddonSchemaDesignerClient {
	AddonSchemaDto GetSchema(AddonGetRequestDto request);
	void SaveSchema(AddonSchemaDto schema);
	/// <summary>Best-effort, post-save reset of the server-side client script cache.</summary>
	/// <returns><c>null</c> on success; a non-fatal warning message when the reset failed after the schema was
	/// already saved. Never throws — the caller should surface the warning, not fail the already-committed write.</returns>
	string ResetClientScriptCache();

	/// <summary>Best-effort, post-save static-content rebuild.</summary>
	/// <returns><c>null</c> on success or a tolerated non-committal response; a non-fatal warning message when the
	/// rebuild could not be confirmed (an explicit <c>success:false</c> or a thrown POST) after the schema was
	/// already saved. Never throws.</returns>
	string BuildConfiguration();
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
	/// making saved schema changes available immediately without a full reload. Returns a non-fatal warning
	/// message when the reset failed after the schema was already saved (so a caller can surface it to a user
	/// who cannot see server logs), or <c>null</c> on success.
	/// </summary>
	public string ResetClientScriptCache() {
		// Best-effort, like BuildConfiguration: this runs AFTER the schema is already saved (both the related-page
		// and business-rule Create/Append paths call it post-save), so a transient failure — e.g. an expired-session
		// redirect between the save and the reset — must NOT fail an already-committed operation. Log it AND return it
		// (so the caller can fold it into its result) and move on; the save stands and the cache reset can be retried.
		try {
			applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache"),
				string.Empty);
			return null;
		} catch (Exception exception) {
			string warning =
				"Client script-cache reset failed after the schema was already saved: " + exception.Message
				+ ". The change is persisted; the cache reset can be retried.";
			logger.WriteWarning(warning);
			return warning;
		}
	}

	/// <summary>
	/// Triggers an incremental static content rebuild on the server (no C# compilation).
	/// Regenerates client JS files for changed schemas, broadcasts a
	/// <c>ConfigurationStructureChanged</c> event to online users, and writes a new
	/// <c>ConfigurationHash</c> to disk so offline users get cache invalidation on
	/// their next startup via <c>/api/ClientCache/Hashes</c>. Returns a non-fatal warning message when the
	/// rebuild could not be confirmed after the schema was already saved (so a caller can surface it), or
	/// <c>null</c> when it succeeded or returned a tolerated non-committal response.
	/// </summary>
	public string BuildConfiguration() {
		// This static-content rebuild is a SHARED, fire-and-forget refresh that runs AFTER the schema has already
		// been durably saved — both callers (BusinessRuleAddonService.AppendRules and RelatedPageAddonService.Create)
		// invoke it post-save, and neither can undo the committed save. So a rebuild failure must NOT throw: the POST
		// itself is wrapped (an expired-session redirect between the save and the rebuild can make ExecutePostRequest
		// THROW, symmetric with ResetClientScriptCache); a non-committal response (empty body, a non-JSON auth-redirect
		// page, or a payload with no `success` flag) is tolerated silently; and even an EXPLICIT `success:false` is
		// RETURNED as a warning rather than thrown — throwing would report an already-committed create-*-business-rules
		// / create-related-page-addon operation as failed (a false negative). The change is persisted; the client
		// static-content rebuild may need a retry.
		string responseBody;
		try {
			responseBody = applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration"),
				string.Empty);
		} catch (Exception exception) {
			string warning =
				"Static-content rebuild (BuildConfiguration) failed after the schema was already saved: "
				+ exception.Message
				+ ". The change is persisted; the client static-content rebuild may need a manual retry.";
			logger.WriteWarning(warning);
			return warning;
		}
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return null;
		}
		AddonBuildResponseDto response;
		try {
			response = JsonSerializer.Deserialize<AddonBuildResponseDto>(responseBody, JsonOptions);
		} catch (JsonException) {
			return null; // non-JSON body (e.g. an HTML/redirect page) carries no explicit failure signal — tolerate it
		}
		if (response?.Success == false) {
			string warning =
				"WorkspaceExplorerService.svc/BuildConfiguration reported a failure AFTER the schema was already saved: "
				+ (response.ErrorInfo?.Message ?? "static content rebuild failed")
				+ ". The change is persisted; the client static-content rebuild may need a manual retry.";
			logger.WriteWarning(warning);
			return warning;
		}
		return null;
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
