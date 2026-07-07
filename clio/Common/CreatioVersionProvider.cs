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
	// A null _resolution distinguishes "not probed yet" from "probed" — the probed answer is always
	// a non-null CreatioVersionResolution carrying its tri-state status.
	private CreatioVersionResolution _resolution;

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
	public CreatioVersionResolution Resolve() {
		_resolution ??= Probe();
		return _resolution;
	}

	#endregion

	#region Methods: Private

	// The tri-state outcome of a SINGLE source probe: either the HTTP call returned (Responded) — even
	// with an empty / garbage / no-field / unparseable-version body — or it threw a soft-degradable
	// exception (Failed). When Responded, RawVersion holds the extracted-and-trimmed version string (or
	// null when the body carried no usable version field). This is the seam the aggregation needs to
	// tell "reachable but no version" apart from "did not respond at all".
	private readonly record struct ProbeOutcome(bool Responded, string RawVersion)
	{
		public static ProbeOutcome FromFailure() => new(false, null);
		public static ProbeOutcome FromResponse(string rawVersion) => new(true, rawVersion);
	}

	// Resolves the target environment's core version. PRIMARY source is the UNGATED ApplicationInfoService
	// (applicationInfo.sysValues.coreVersion), which needs only an authenticated session — so the version
	// gate, which fires for EVERY user, never fails closed on a compatible stand merely because the caller
	// lacks admin rights. The cliogate GetSysInfo endpoint (SysInfo.CoreVersion) is an admin-gated
	// SECONDARY probe, tried only when ApplicationInfo yields no parseable version (defense-in-depth for a
	// stand whose ApplicationInfo lacks the field). Both expose the same 4-part core version, so the
	// secondary is byte-equivalent when it runs. Aggregates the per-source outcomes into three classes:
	//   - first source with a PARSEABLE version  -> Resolved(version)  (secondary short-circuited away);
	//   - else if ANY attempted source RESPONDED  -> ReachableWithoutVersion (reachable, no usable version);
	//   - else (every attempted source FAILED)    -> ProbeFailed (no source responded at all).
	private CreatioVersionResolution Probe() {
		IServiceUrlBuilder serviceUrlBuilder = _serviceUrlBuilderFactory.Create(_environmentSettings);

		ProbeOutcome primary = TryGetApplicationInfoCoreVersion(serviceUrlBuilder);
		if (TryParseVersion(primary.RawVersion, out Version primaryVersion)) {
			return CreatioVersionResolution.Resolved(primaryVersion);
		}

		// Only fall through to the SECONDARY probe when the primary produced no parseable version
		// (mirrors the prior short-circuit, which also ran the secondary when the primary failed or
		// responded without a usable version).
		ProbeOutcome secondary = TryGetSysInfoCoreVersion(serviceUrlBuilder);
		if (TryParseVersion(secondary.RawVersion, out Version secondaryVersion)) {
			return CreatioVersionResolution.Resolved(secondaryVersion);
		}

		// No parseable version from either source. If at least one source RESPONDED (regardless of how
		// useless its body was) the environment is reachable-but-undeterminable; otherwise no source
		// responded at all and the version check could not be performed.
		return primary.Responded || secondary.Responded
			? CreatioVersionResolution.ReachableWithoutVersion()
			: CreatioVersionResolution.ProbeFailed();
	}

	// Parses the RAW 4-part version verbatim — a version gate must not discard Build/Revision. An empty
	// or unparseable non-empty string is not a usable version (false), never a silently-clamped value.
	private static bool TryParseVersion(string rawVersion, out Version parsed) {
		parsed = null;
		return !string.IsNullOrWhiteSpace(rawVersion) && Version.TryParse(rawVersion.Trim(), out parsed);
	}

	// cliogate GetSysInfo SECONDARY probe (admin-gated, defense-in-depth). Only the transport/parse
	// exception families that legitimately mean "this environment did not answer" are soft-degraded to a
	// Failed outcome (HttpRequestException, TaskCanceledException, TimeoutException, WebException,
	// SocketException, IOException, JsonException) — e.g. HTTP error, timeout, connection refused,
	// cliogate not installed → HTML/404, permission denied for a non-admin, malformed body. Any OTHER
	// exception (fatal CLR errors, unexpected programming errors) PROPAGATES by design: it must not be
	// silently swallowed. A returned call (even one carrying no usable version) is a Responded outcome.
	private ProbeOutcome TryGetSysInfoCoreVersion(IServiceUrlBuilder serviceUrlBuilder) {
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetSysInfo);
			string response = _applicationClient.ExecuteGetRequest(url);
			return ProbeOutcome.FromResponse(TryExtractSysInfoCoreVersion(response));
		}
		catch (Exception ex) when (IsSoftDegradable(ex)) {
			return ProbeOutcome.FromFailure();
		}
	}

	// ApplicationInfoService PRIMARY probe — ungated, needs only an authenticated session, so it works for
	// any user (incl. non-admins and cliogate-less environments). Same soft-failure contract as the
	// secondary probe: only the transport/parse families are degraded to a Failed outcome; anything else
	// propagates. A returned call (even one carrying no usable version) is a Responded outcome.
	private ProbeOutcome TryGetApplicationInfoCoreVersion(IServiceUrlBuilder serviceUrlBuilder) {
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetApplicationInfo);
			string response = _applicationClient.ExecutePostRequest(url, "{}");
			return ProbeOutcome.FromResponse(TryExtractApplicationInfoCoreVersion(response));
		}
		catch (Exception ex) when (IsSoftDegradable(ex)) {
			return ProbeOutcome.FromFailure();
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
