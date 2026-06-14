using System;
using Clio.UserEnvironment;

namespace Clio.Common.Telemetry;

/// <summary>
/// Resolved configuration for the telemetry flusher.
/// </summary>
/// <param name="Endpoint">Full OTLP/HTTP logs endpoint URL; <c>null</c> when uploading is disabled.</param>
/// <param name="IngestKey">Optional public ingest key sent as a request header.</param>
public sealed record MeasurementFlushOptions(string Endpoint, string IngestKey)
{
	/// <summary>
	/// Indicates whether uploading is enabled. Uploads happen only when a valid endpoint is configured.
	/// </summary>
	public bool IsSendingEnabled => !string.IsNullOrWhiteSpace(Endpoint);
}

/// <summary>
/// Resolves the telemetry flush configuration. Environment variables
/// <c>CLIO_TELEMETRY_ENDPOINT</c> and <c>CLIO_TELEMETRY_INGEST_KEY</c> win over the
/// <c>telemetry</c> section of the clio settings file.
/// </summary>
public interface IMeasurementFlushOptionsProvider
{
	/// <summary>
	/// Resolves the current flush options. Called once per flush run so long-running
	/// processes pick up configuration changes without a restart.
	/// </summary>
	MeasurementFlushOptions Resolve();
}

/// <inheritdoc />
public sealed class MeasurementFlushOptionsProvider : IMeasurementFlushOptionsProvider
{
	internal const string EndpointEnvironmentVariable = "CLIO_TELEMETRY_ENDPOINT";
	internal const string IngestKeyEnvironmentVariable = "CLIO_TELEMETRY_INGEST_KEY";

	private readonly ISettingsRepository _settingsRepository;

	/// <summary>
	/// Initializes a new instance of the <see cref="MeasurementFlushOptionsProvider"/> class.
	/// </summary>
	public MeasurementFlushOptionsProvider(ISettingsRepository settingsRepository)
	{
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	}

	/// <inheritdoc />
	public MeasurementFlushOptions Resolve()
	{
		TelemetrySettings settings = _settingsRepository.GetTelemetrySettings();
		string endpoint = FirstNonEmpty(
			Environment.GetEnvironmentVariable(EndpointEnvironmentVariable), settings?.Endpoint);
		string ingestKey = FirstNonEmpty(
			Environment.GetEnvironmentVariable(IngestKeyEnvironmentVariable), settings?.IngestKey);
		if (!IsValidEndpoint(endpoint)) {
			endpoint = null;
		}
		return new MeasurementFlushOptions(endpoint, ingestKey);
	}

	private static string FirstNonEmpty(string preferred, string fallback) =>
		string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

	private static bool IsValidEndpoint(string endpoint) =>
		Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
		&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
