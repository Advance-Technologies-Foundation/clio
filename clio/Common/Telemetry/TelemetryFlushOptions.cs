using System;
using Clio.UserEnvironment;

namespace Clio.Common.Telemetry;

/// <summary>
/// Resolved configuration for the telemetry flusher.
/// </summary>
/// <param name="Endpoint">Full OTLP/HTTP logs endpoint URL; <c>null</c> when uploading is disabled.</param>
/// <param name="IngestKey">Optional public ingest key sent as a request header.</param>
public sealed record TelemetryFlushOptions(string Endpoint, string IngestKey)
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
public interface ITelemetryFlushOptionsProvider
{
	/// <summary>
	/// Resolves the current flush options. Called once per flush run so long-running
	/// processes pick up configuration changes without a restart.
	/// </summary>
	TelemetryFlushOptions Resolve();
}

/// <inheritdoc />
public sealed class TelemetryFlushOptionsProvider : ITelemetryFlushOptionsProvider
{
	internal const string EndpointEnvironmentVariable = "CLIO_TELEMETRY_ENDPOINT";
	internal const string IngestKeyEnvironmentVariable = "CLIO_TELEMETRY_INGEST_KEY";

	private readonly ISettingsRepository _settingsRepository;

	/// <summary>
	/// Initializes a new instance of the <see cref="TelemetryFlushOptionsProvider"/> class.
	/// </summary>
	public TelemetryFlushOptionsProvider(ISettingsRepository settingsRepository)
	{
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	}

	/// <inheritdoc />
	public TelemetryFlushOptions Resolve()
	{
		TelemetrySettings settings = _settingsRepository.GetTelemetrySettings();
		string endpoint = FirstNonEmpty(
			Environment.GetEnvironmentVariable(EndpointEnvironmentVariable), settings?.Endpoint);
		string ingestKey = FirstNonEmpty(
			Environment.GetEnvironmentVariable(IngestKeyEnvironmentVariable), settings?.IngestKey);
		if (!IsValidEndpoint(endpoint)) {
			endpoint = null;
		}
		return new TelemetryFlushOptions(endpoint, ingestKey);
	}

	private static string FirstNonEmpty(string preferred, string fallback) =>
		string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

	// HTTPS is required so the ingest key and event payload never traverse the network in
	// cleartext; plaintext http is permitted only for a loopback host (local-collector testing).
	private static bool IsValidEndpoint(string endpoint) =>
		Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
		&& (uri.Scheme == Uri.UriSchemeHttps
			|| (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
