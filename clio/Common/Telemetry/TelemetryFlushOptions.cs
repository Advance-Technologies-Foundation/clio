using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
	/// Production OTLP/HTTP logs endpoint shipped as the built-in default so any clio installation —
	/// fresh or updated in place — uploads product telemetry once consent is granted, without
	/// per-machine configuration. It is the lowest-precedence endpoint source:
	/// <c>CLIO_TELEMETRY_ENDPOINT</c> and the settings <c>telemetry.endpoint</c> still override it,
	/// and the opt-out (<c>CLIO_TELEMETRY_ENABLED=false</c> or <c>telemetry.enabled: false</c>)
	/// suppresses uploading entirely. Shipping the endpoint in the binary — rather than seeding the
	/// persisted settings file, which clio neither ships nor creates with a default — keeps it a
	/// single source of truth that a normal clio release can relocate.
	/// </summary>
	internal const string DefaultEndpoint = "https://caadt-telemetry.creatio.com/v1/logs";

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
				"telemetry endpoint rejected reason=requires-https-or-private-http; uploading disabled");
			endpoint = null;
		} else if (!string.IsNullOrWhiteSpace(endpoint) && IsInsecurePrivateEndpoint(endpoint)) {
			// THROWAWAY TEST BUILD ONLY (branch throwaway/eng-89424-insecure-private-ip). Every cleartext
			// upload accepted via the private-IP relaxation is logged so the weakened transport is never
			// silent. Do NOT merge to master.
			_logger.LogWarning(
				"telemetry using INSECURE cleartext http to a private address endpoint={Endpoint}; " +
				"throwaway test build only, not for production", endpoint);
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
	//
	// THROWAWAY TEST BUILD ONLY (branch throwaway/eng-89424-insecure-private-ip): this build also
	// permits cleartext http to an RFC1918 private IPv4 address, so an internal tester can point clio
	// straight at the stage collector NodePort (e.g. http://10.48.x.x:31419/v1/logs) without a tunnel.
	// Public hosts and hostnames remain https-only — RFC1918 ranges are not internet-routable, so this
	// can never enable cleartext across the public internet. Do NOT merge this relaxation to master.
	private static bool IsValidEndpoint(string endpoint) =>
		Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
		&& (uri.Scheme == Uri.UriSchemeHttps
			|| (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || IsPrivateHost(uri))));

	// True only for an endpoint accepted via the throwaway private-IP relaxation (cleartext http to a
	// non-loopback private address), so the caller can log every such upload — it is never silent.
	private static bool IsInsecurePrivateEndpoint(string endpoint) =>
		Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
		&& uri.Scheme == Uri.UriSchemeHttp
		&& !uri.IsLoopback
		&& IsPrivateHost(uri);

	// RFC1918 private IPv4 ranges (10/8, 172.16/12, 192.168/16). Hostnames return false: only IP
	// literals qualify, so a private *name* (which could resolve anywhere) is never treated as trusted.
	private static bool IsPrivateHost(Uri uri)
	{
		if (!IPAddress.TryParse(uri.Host, out IPAddress address)
			|| address.AddressFamily != AddressFamily.InterNetwork) {
			return false;
		}
		byte[] octets = address.GetAddressBytes();
		return octets[0] == 10
			|| (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
			|| (octets[0] == 192 && octets[1] == 168);
	}
}
