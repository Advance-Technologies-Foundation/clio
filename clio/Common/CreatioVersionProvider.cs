using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

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

	// cliogate GetSysInfo SECONDARY probe (admin-gated, defense-in-depth). Only the transport/parse
	// exception families that legitimately mean "this environment did not answer with a usable version"
	// are soft-degraded to null (HttpRequestException, TaskCanceledException, TimeoutException,
	// WebException, SocketException, IOException, JsonException) — e.g. HTTP error, timeout, connection
	// refused, cliogate not installed → HTML/404, permission denied for a non-admin, malformed body.
	// Any OTHER exception (fatal CLR errors, unexpected programming errors) PROPAGATES by design: it must
	// not be silently swallowed into an undeterminable version.
	private string TryGetSysInfoCoreVersion(IServiceUrlBuilder serviceUrlBuilder) {
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetSysInfo);
			string response = _applicationClient.ExecuteGetRequest(url);
			return TryExtractSysInfoCoreVersion(response);
		}
		catch (Exception ex) when (IsSoftDegradable(ex)) {
			return null;
		}
	}

	// ApplicationInfoService PRIMARY probe — ungated, needs only an authenticated session, so it works for
	// any user (incl. non-admins and cliogate-less environments). Same soft-failure contract as the
	// secondary probe: only the transport/parse families are degraded to null; anything else propagates.
	private string TryGetApplicationInfoCoreVersion(IServiceUrlBuilder serviceUrlBuilder) {
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetApplicationInfo);
			string response = _applicationClient.ExecutePostRequest(url, "{}");
			return TryExtractApplicationInfoCoreVersion(response);
		}
		catch (Exception ex) when (IsSoftDegradable(ex)) {
			return null;
		}
	}

	// The transport/parse exception families that should soft-degrade a probe to null (undeterminable),
	// rather than crash the dispatch gate. Everything else is unexpected and must propagate.
	private static bool IsSoftDegradable(Exception ex) =>
		ex is HttpRequestException
			or TaskCanceledException
			or TimeoutException
			or WebException
			or SocketException
			or IOException
			or JsonException;

	// Extracts SysInfo.CoreVersion from the cliogate GetSysInfo response. Returns null on any missing
	// node or non-string value.
	private static string TryExtractSysInfoCoreVersion(string rawJson) =>
		TryExtractStringAtPath(rawJson, "SysInfo", "CoreVersion");

	// Extracts applicationInfo.sysValues.coreVersion from the ApplicationInfoService response. Returns
	// null on any missing node or non-string value.
	private static string TryExtractApplicationInfoCoreVersion(string rawJson) =>
		TryExtractStringAtPath(rawJson, "applicationInfo", "sysValues", "coreVersion");

	// Walks the given object path through the parsed JSON, requiring every intermediate segment to be a
	// JSON object and the leaf to be a JSON string. Returns the leaf string, or null on any missing
	// segment, wrong value kind, empty input, or malformed JSON (JsonException).
	private static string TryExtractStringAtPath(string rawJson, params string[] path) {
		if (string.IsNullOrWhiteSpace(rawJson)) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(rawJson);
			JsonElement current = document.RootElement;
			for (int i = 0; i < path.Length; i++) {
				if (current.ValueKind != JsonValueKind.Object
					|| !current.TryGetProperty(path[i], out JsonElement next)) {
					return null;
				}
				current = next;
			}
			return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
		}
		catch (JsonException) {
			return null;
		}
	}

	#endregion

}
