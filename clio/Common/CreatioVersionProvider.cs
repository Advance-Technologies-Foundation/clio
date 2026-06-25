using System;
using System.Text.Json;

namespace Clio.Common;

/// <inheritdoc cref="ICreatioVersionProvider"/>
public sealed class CreatioVersionProvider : ICreatioVersionProvider
{

	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly IServiceUrlBuilderFactory _serviceUrlBuilderFactory;

	// Memoised for the lifetime of the invocation: the version is resolved at most once even when a
	// command declares several requirements (mirrors RequiredPackageChecker's package-list cache).
	// _resolved guards against re-probing when the genuine answer is null (undeterminable) — a null
	// _coreVersion alone could not distinguish "not probed yet" from "probed, no version".
	private bool _resolved;
	private Version _coreVersion;

	#endregion

	#region Constructors: Public

	public CreatioVersionProvider(IApplicationClient applicationClient, EnvironmentSettings environmentSettings,
		IServiceUrlBuilderFactory serviceUrlBuilderFactory) {
		_applicationClient = applicationClient;
		_environmentSettings = environmentSettings;
		_serviceUrlBuilderFactory = serviceUrlBuilderFactory;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public Version GetCoreVersion() {
		if (_resolved) {
			return _coreVersion;
		}
		_coreVersion = Probe();
		_resolved = true;
		return _coreVersion;
	}

	#endregion

	#region Methods: Private

	// Resolves the target environment's core version. PRIMARY source is the UNGATED ApplicationInfoService
	// (applicationInfo.sysValues.coreVersion), which needs only an authenticated session — so the version
	// gate, which fires for EVERY user, never fails closed on a compatible stand merely because the caller
	// lacks admin rights. The cliogate GetSysInfo endpoint (SysInfo.CoreVersion) is an admin-gated
	// SECONDARY probe, tried only when ApplicationInfo yields nothing (defense-in-depth for a stand whose
	// ApplicationInfo lacks the field). Both expose the same 4-part core version, so the secondary is
	// byte-equivalent when it runs. Returns null only when NEITHER source yields a parseable version —
	// null is reserved for "no version at all".
	private Version Probe() {
		IServiceUrlBuilder serviceUrlBuilder = _serviceUrlBuilderFactory.Create(_environmentSettings);

		string rawVersion = TryGetApplicationInfoCoreVersion(serviceUrlBuilder);
		if (string.IsNullOrWhiteSpace(rawVersion)) {
			rawVersion = TryGetSysInfoCoreVersion(serviceUrlBuilder);
		}

		if (string.IsNullOrWhiteSpace(rawVersion)) {
			return null;
		}

		// Parse the RAW 4-part version verbatim — a version gate must not discard Build/Revision. An
		// unparseable non-empty string is undeterminable (null), never a silently-clamped value.
		return Version.TryParse(rawVersion.Trim(), out Version parsed) ? parsed : null;
	}

	// cliogate GetSysInfo SECONDARY probe (admin-gated, defense-in-depth). Any failure class (HTTP error,
	// timeout, connection refused, cliogate not installed → HTML/404, permission denied for a non-admin,
	// unexpected shape) must yield null so the resolver ultimately reports an undeterminable version
	// rather than letting a raw stack trace escape the dispatch gate.
	private string TryGetSysInfoCoreVersion(IServiceUrlBuilder serviceUrlBuilder) {
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetSysInfo);
			string response = _applicationClient.ExecuteGetRequest(url);
			return TryExtractSysInfoCoreVersion(response);
		}
		// Broad catch is intentional: this is a soft probe. Every transport/parse failure must degrade
		// to the fallback (and ultimately null), never surface as an exception at the dispatch gate.
		catch (Exception) {
			return null;
		}
	}

	// ApplicationInfoService PRIMARY probe — ungated, needs only an authenticated session, so it works for
	// any user (incl. non-admins and cliogate-less environments). Same soft-failure contract as the
	// secondary probe.
	private string TryGetApplicationInfoCoreVersion(IServiceUrlBuilder serviceUrlBuilder) {
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetApplicationInfo);
			string response = _applicationClient.ExecutePostRequest(url, "{}");
			return TryExtractApplicationInfoCoreVersion(response);
		}
		// Broad catch is intentional: see TryGetSysInfoCoreVersion.
		catch (Exception) {
			return null;
		}
	}

	// Extracts SysInfo.CoreVersion from the cliogate GetSysInfo response. Returns null on any missing
	// node or non-string value.
	private static string TryExtractSysInfoCoreVersion(string rawJson) {
		if (string.IsNullOrWhiteSpace(rawJson)) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(rawJson);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("SysInfo", out JsonElement sysInfo)
				|| sysInfo.ValueKind != JsonValueKind.Object
				|| !sysInfo.TryGetProperty("CoreVersion", out JsonElement coreVersion)
				|| coreVersion.ValueKind != JsonValueKind.String) {
				return null;
			}
			return coreVersion.GetString();
		}
		catch (JsonException) {
			return null;
		}
	}

	// Extracts applicationInfo.sysValues.coreVersion from the ApplicationInfoService response. Returns
	// null on any missing node or non-string value.
	private static string TryExtractApplicationInfoCoreVersion(string rawJson) {
		if (string.IsNullOrWhiteSpace(rawJson)) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(rawJson);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("applicationInfo", out JsonElement appInfo)
				|| appInfo.ValueKind != JsonValueKind.Object
				|| !appInfo.TryGetProperty("sysValues", out JsonElement sysValues)
				|| sysValues.ValueKind != JsonValueKind.Object
				|| !sysValues.TryGetProperty("coreVersion", out JsonElement coreVersion)
				|| coreVersion.ValueKind != JsonValueKind.String) {
				return null;
			}
			return coreVersion.GetString();
		}
		catch (JsonException) {
			return null;
		}
	}

	#endregion

}
