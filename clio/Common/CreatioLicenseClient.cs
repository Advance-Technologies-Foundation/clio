using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Clio.Common;

/// <summary>
/// Client for the native Creatio <c>LicenseService.svc</c> that reports the granted state of named license
/// operations. Mirrors the platform <c>LicenseService.getLicenseOperationStatuses</c> contract.
/// </summary>
public interface ICreatioLicenseClient
{
	/// <summary>
	/// Returns the granted state of each requested license operation via
	/// <c>LicenseService.svc/GetLicOperationStatuses</c>. Operations the server does not report (for example
	/// when the caller is unlicensed) are absent from the map.
	/// </summary>
	/// <param name="operationCodes">The license operation codes (for example <c>CanCustomizeBranding</c>).</param>
	/// <param name="requestOptions">The request timeout and retry settings.</param>
	/// <returns>A map of operation code to its granted state; empty when nothing was requested or granted.</returns>
	/// <exception cref="InvalidOperationException">The service returned an empty or non-JSON response.</exception>
	IReadOnlyDictionary<string, bool> GetLicenseOperationStatuses(IReadOnlyCollection<string> operationCodes,
		CreatioRequestOptions requestOptions);
}

/// <inheritdoc />
public class CreatioLicenseClient : CreatioServiceClient, ICreatioLicenseClient
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioLicenseClient"/> class.
	/// </summary>
	public CreatioLicenseClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<string, bool> GetLicenseOperationStatuses(IReadOnlyCollection<string> operationCodes,
		CreatioRequestOptions requestOptions) {
		Dictionary<string, bool> statuses = new(StringComparer.OrdinalIgnoreCase);
		if (operationCodes is null || operationCodes.Count == 0) {
			return statuses;
		}
		LicOperationStatusesResponse response = PostAndDeserialize<LicOperationStatusesResponse>(
			ServiceUrlBuilder.KnownRoute.LicenseGetLicOperationStatuses,
			new LicOperationStatusesRequest { LicOperationCodes = operationCodes },
			requestOptions);
		LicOperationStatusesResult result = response?.Result;
		if (result?.Success != true || result.LicOperationStatuses is null) {
			return statuses;
		}
		foreach (LicOperationStatus status in result.LicOperationStatuses.Where(status => !string.IsNullOrEmpty(status?.Key))) {
			statuses[status.Key] = status.Value;
		}
		return statuses;
	}

	private sealed record LicOperationStatusesRequest
	{
		[JsonPropertyName("licOperationCodes")]
		public IReadOnlyCollection<string> LicOperationCodes { get; init; }
	}

	private sealed record LicOperationStatusesResponse
	{
		[JsonPropertyName("GetLicOperationStatusesResult")]
		public LicOperationStatusesResult Result { get; init; }
	}

	private sealed record LicOperationStatusesResult
	{
		[JsonPropertyName("success")]
		public bool? Success { get; init; }

		[JsonPropertyName("licOperationStatuses")]
		public List<LicOperationStatus> LicOperationStatuses { get; init; }
	}

	private sealed record LicOperationStatus
	{
		[JsonPropertyName("Key")]
		public string Key { get; init; }

		[JsonPropertyName("Value")]
		public bool Value { get; init; }
	}
}
