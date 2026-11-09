using System;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.Branding;

internal static class ODataSingleIdQuery
{
	internal static bool TryRead(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
		string relativeUrl, out Guid? id, out string error) {
		id = null;
		error = null;
		try {
			string response = applicationClient.ExecuteGetRequest(serviceUrlBuilder.Build(relativeUrl));
			if (string.IsNullOrWhiteSpace(response)) {
				error = "the environment returned an empty response.";
				return false;
			}
			using JsonDocument document = JsonDocument.Parse(response);
			//Every caller hands this an ODataKeyFormatter.CollectionPath URL and expects the standard OData
			//"value" collection below, so the response has to be classified as OData. Under Service the
			//BaseResponse heuristics run over entity data, and an entity carrying a business Success:false
			//column was rejected as a service failure.
			if (CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.ODataPayload,
				out string serverError)) {
				error = serverError;
				return false;
			}
			if (!document.RootElement.TryGetProperty("value", out JsonElement rows)
				|| rows.ValueKind != JsonValueKind.Array) {
				error = "the environment returned an unexpected response.";
				return false;
			}
			if (rows.GetArrayLength() > 0
				&& rows[0].TryGetProperty("Id", out JsonElement idElement)
				&& idElement.ValueKind == JsonValueKind.String
				&& Guid.TryParse(idElement.GetString(), out Guid parsed)
				&& parsed != Guid.Empty) {
				id = parsed;
			}
			return true;
		} catch (JsonException) {
			error = "the environment returned a non-JSON response.";
			return false;
		} catch (Exception ex) when (ex is not OperationCanceledException) {
			error = ex.Message;
			return false;
		}
	}
}
