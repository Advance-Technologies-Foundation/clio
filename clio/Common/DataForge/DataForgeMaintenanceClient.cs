using System;
using System.Text.Json;

namespace Clio.Common.DataForge;

public interface IDataForgeMaintenanceClient {
	DataForgeMaintenanceStatusResult GetStatus();

	/// <summary>
	/// Returns full health details (liveness, readiness, data-structure readiness, lookups readiness)
	/// by calling the Creatio <c>DataForgeMaintenanceService/GetServiceStatus</c> endpoint.
	/// </summary>
	DataForgeHealthResult GetHealthDetails();

	/// <summary>
	/// Returns both health details and maintenance status from a single
	/// <c>DataForgeMaintenanceService/GetServiceStatus</c> call, avoiding a redundant HTTP round-trip.
	/// </summary>
	(DataForgeHealthResult Health, DataForgeMaintenanceStatusResult Status) GetFullStatus();

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

	public DataForgeHealthResult GetHealthDetails() {
		string response = applicationClient.ExecutePostRequest(BuildMethodUrl("GetServiceStatus"), "{}");
		ServiceStatusEnvelope? envelope = Deserialize<ServiceStatusEnvelope>(response);
		ServiceStatusPayload? payload = envelope?.GetServiceStatusResult ?? Deserialize<ServiceStatusPayload>(response);
		if (payload is null) {
			return new DataForgeHealthResult(false, false, false, false, string.Empty);
		}

		bool liveness = payload.IsOnline;
		bool readiness = liveness && payload.Readiness?.HttpStatusCode == 200;
		bool dataStructuresReady = readiness && !string.IsNullOrWhiteSpace(payload.DataStructureReadiness)
			&& !payload.DataStructureReadiness.Contains("error", StringComparison.OrdinalIgnoreCase);
		bool lookupsReady = readiness && !string.IsNullOrWhiteSpace(payload.LookupsReadinessInfo)
			&& !payload.LookupsReadinessInfo.Contains("error", StringComparison.OrdinalIgnoreCase);
		return new DataForgeHealthResult(liveness, readiness, dataStructuresReady, lookupsReady, string.Empty);
	}

	public (DataForgeHealthResult Health, DataForgeMaintenanceStatusResult Status) GetFullStatus() {
		string response = applicationClient.ExecutePostRequest(BuildMethodUrl("GetServiceStatus"), "{}");
		ServiceStatusEnvelope? envelope = Deserialize<ServiceStatusEnvelope>(response);
		ServiceStatusPayload? payload = envelope?.GetServiceStatusResult ?? Deserialize<ServiceStatusPayload>(response);

		DataForgeHealthResult health;
		DataForgeMaintenanceStatusResult status;

		if (payload is null) {
			health = new DataForgeHealthResult(false, false, false, false, string.Empty);
			status = new DataForgeMaintenanceStatusResult(false, "Unavailable", "Empty maintenance status response.");
			return (health, status);
		}

		bool liveness = payload.IsOnline;
		bool readiness = liveness && payload.Readiness?.HttpStatusCode == 200;
		bool dataStructuresReady = readiness && !string.IsNullOrWhiteSpace(payload.DataStructureReadiness)
			&& !payload.DataStructureReadiness.Contains("error", StringComparison.OrdinalIgnoreCase);
		bool lookupsReady = readiness && !string.IsNullOrWhiteSpace(payload.LookupsReadinessInfo)
			&& !payload.LookupsReadinessInfo.Contains("error", StringComparison.OrdinalIgnoreCase);
		health = new DataForgeHealthResult(liveness, readiness, dataStructuresReady, lookupsReady, string.Empty);

		if (!payload.IsOnline) {
			status = new DataForgeMaintenanceStatusResult(false, "Offline", payload.Liveness?.Message);
		} else if (payload.Readiness?.HttpStatusCode == 200) {
			status = new DataForgeMaintenanceStatusResult(true, "Ready", null);
		} else {
			status = new DataForgeMaintenanceStatusResult(false, "NotReady", payload.Readiness?.Message);
		}

		return (health, status);
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
