using System;
using System.Linq;
using Clio.UserEnvironment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
/// Resolves the telemetry flush configuration. The endpoint is resolved by precedence — the
/// <c>CLIO_TELEMETRY_ENDPOINT</c> environment variable, then the <c>telemetry.endpoint</c> settings
/// value, then the production default shipped in the binary; the ingest key follows the same
/// env-over-settings precedence. The operator opt-out (<c>CLIO_TELEMETRY_ENABLED=false</c> or
/// <c>telemetry.enabled: false</c>) disables uploading regardless of the resolved endpoint and of
/// granted consent.
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
	internal const string EnabledEnvironmentVariable = "CLIO_TELEMETRY_ENABLED";

	/// <summary>
	/// TEST DISTRIBUTION BUILD ONLY (branch test/eng-89424-rnd-default): the built-in default is
	/// pointed at the R&D STAGING collector so a build handed to testers uploads with zero config
	/// (consent only). It is https with a valid *.creatio.com cert, so it satisfies the normal
	/// transport guard — no relaxation needed. The host resolves only on the corporate network
	/// (internal 10.48.x.x), which is fine for internal testers but useless as a production default.
	/// On master this is the production endpoint (<c>https://caadt-telemetry.creatio.com/v1/logs</c>).
	/// DO NOT MERGE this value to master.
	///
	/// It is the lowest-precedence endpoint source: <c>CLIO_TELEMETRY_ENDPOINT</c> and the settings
	/// <c>telemetry.endpoint</c> still override it, and the opt-out
	/// (<c>CLIO_TELEMETRY_ENABLED=false</c> or <c>telemetry.enabled: false</c>) suppresses uploading.
	/// </summary>
	internal const string DefaultEndpoint = "https://caadt-telemetry-rnd.creatio.com/v1/logs";

	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger<TelemetryFlushOptionsProvider> _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="TelemetryFlushOptionsProvider"/> class.
	/// </summary>
	public TelemetryFlushOptionsProvider(ISettingsRepository settingsRepository,
		ILogger<TelemetryFlushOptionsProvider> logger = null)
	{
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_logger = logger ?? NullLogger<TelemetryFlushOptionsProvider>.Instance;
	}

	/// <inheritdoc />
	public TelemetryFlushOptions Resolve()
	{
		TelemetrySettings settings = _settingsRepository.GetTelemetrySettings();
		if (IsUploadingDisabled(settings)) {
			// Operator opt-out hard-disables uploading and wins over the shipped default and any
			// configured endpoint, regardless of granted consent. Returning a null endpoint reuses
			// the flusher's existing "not configured" skip path.
			return new TelemetryFlushOptions(null, null);
		}
		string endpoint = FirstNonEmpty(
			Environment.GetEnvironmentVariable(EndpointEnvironmentVariable), settings?.Endpoint, DefaultEndpoint);
		string ingestKey = FirstNonEmpty(
			Environment.GetEnvironmentVariable(IngestKeyEnvironmentVariable), settings?.IngestKey);
		if (!string.IsNullOrWhiteSpace(endpoint) && !IsValidEndpoint(endpoint)) {
			// Distinct from the "no endpoint configured" case: a real endpoint was set but rejected,
			// so surface why uploading stays disabled instead of letting it look unconfigured. A
			// rejected explicit endpoint disables uploading; it does not silently fall back to the default.
			_logger.LogWarning(
				"telemetry endpoint rejected reason=requires-https-or-loopback-http; uploading disabled");
			endpoint = null;
		}
		return new TelemetryFlushOptions(endpoint, ingestKey);
	}

	// Operator opt-out, evaluated before endpoint resolution. The CLIO_TELEMETRY_ENABLED environment
	// variable wins over the settings flag (mirroring the endpoint precedence) so a managed fleet or
	// CI run can disable uploads without editing the settings file. An unset or unparsable env value
	// falls back to the settings flag; an absent flag leaves uploading enabled (the shipped default).
	private static bool IsUploadingDisabled(TelemetrySettings settings)
	{
		string environmentValue = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(environmentValue) && bool.TryParse(environmentValue, out bool enabled)) {
			return !enabled;
		}
		return settings?.Enabled == false;
	}

	private static string FirstNonEmpty(params string[] values) =>
		values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

	// HTTPS is required so the ingest key and event payload never traverse the network in
	// cleartext; plaintext http is permitted only for a loopback host (local-collector testing).
	private static bool IsValidEndpoint(string endpoint) =>
		Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
		&& (uri.Scheme == Uri.UriSchemeHttps
			|| (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
