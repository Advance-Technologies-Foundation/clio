using System.Text.Json;

namespace Clio.Common.DataForge;

public interface IDataForgeMaintenanceClient {
	DataForgeMaintenanceStatusResult GetStatus();
	DataForgeMaintenanceStatusResult Initialize();
	DataForgeMaintenanceStatusResult Update();
}

public sealed class DataForgeMaintenanceClient(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IDataForgeMaintenanceClient {
	private const string ServiceBasePath = "rest/DataForgeMaintenanceService";
	private readonly JsonSerializerOptions _jsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	public DataForgeMaintenanceStatusResult GetStatus() {
		string response = applicationClient.ExecutePostRequest(BuildMethodUrl("GetServiceStatus"), "{}");
		ServiceStatusEnvelope? envelope = Deserialize<ServiceStatusEnvelope>(response);
		ServiceStatusPayload? payload = envelope?.GetServiceStatusResult ?? Deserialize<ServiceStatusPayload>(response);
		if (payload is null) {
			return new DataForgeMaintenanceStatusResult(false, "Unavailable", "Empty maintenance status response.");
		}

		if (!payload.IsOnline) {
			return new DataForgeMaintenanceStatusResult(false, "Offline", payload.Liveness?.Message);
		}

		if (payload.Readiness?.HttpStatusCode == 200) {
			return new DataForgeMaintenanceStatusResult(true, "Ready", null);
		}

		return new DataForgeMaintenanceStatusResult(false, "NotReady", payload.Readiness?.Message);
	}

	public DataForgeMaintenanceStatusResult Initialize() {
		applicationClient.ExecutePostRequest(BuildMethodUrl("InitializeDataStructuresAndLookups"), "{}");
		return new DataForgeMaintenanceStatusResult(true, "Scheduled", null);
	}

	public DataForgeMaintenanceStatusResult Update() {
		applicationClient.ExecutePostRequest(BuildMethodUrl("UpdateDataStructuresAndLookups"), "{}");
		return new DataForgeMaintenanceStatusResult(true, "Scheduled", null);
	}

	private string BuildMethodUrl(string methodName) => serviceUrlBuilder.Build($"{ServiceBasePath}/{methodName}");

	private T? Deserialize<T>(string response) where T : class {
		if (string.IsNullOrWhiteSpace(response)) {
			return null;
		}

		return JsonSerializer.Deserialize<T>(response, _jsonOptions);
	}

	private sealed record ProbePayload(int HttpStatusCode, string? Message);

	private sealed record ServiceStatusPayload(
		bool IsOnline,
		ProbePayload? Liveness,
		ProbePayload? Readiness,
		string? DataStructureReadiness,
		string? LookupsReadinessInfo);

	private sealed record ServiceStatusEnvelope(ServiceStatusPayload? GetServiceStatusResult);
}
