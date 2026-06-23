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
	/// Production OTLP/HTTP logs collector — the intended built-in default once it is live. Kept as
	/// a named constant so the value is documented and compile-pinned ahead of launch; flipping
	/// <see cref="DefaultEndpoint"/> to it is the one-line change that turns telemetry on.
	/// </summary>
	internal const string ProductionEndpoint = "https://caadt-telemetry.creatio.com/v1/logs";

	/// <summary>
	/// The lowest-precedence endpoint source, shipped in the binary. <c>CLIO_TELEMETRY_ENDPOINT</c>
	/// and the settings <c>telemetry.endpoint</c> override it; a configured-but-invalid endpoint
	/// disables uploading rather than falling back here. It currently ships <b>empty</b> because the
	/// production collector (<see cref="ProductionEndpoint"/>) is not live yet, so a freshly
	/// installed or in-place-updated clio sends nothing anywhere until an endpoint is explicitly
	/// configured. When the collector is provisioned, set this to <see cref="ProductionEndpoint"/>
	/// (and update the pin test) to turn every install on without per-machine configuration — the
	/// binary is the only delivery vehicle that reaches existing installs on update, since clio
	/// neither ships <c>appsettings.json</c> nor creates it with a telemetry default.
	/// </summary>
	internal const string DefaultEndpoint = "";

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
