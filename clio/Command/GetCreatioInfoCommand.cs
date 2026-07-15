using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Clio.Common;

namespace Clio.Command
{
	#region Class: GetCreatioInfoCommandCommandOptions

	//clio get-info -e work

	[Verb("get-info", Aliases = ["describe", "describe-creatio", "instance-info"],
		HelpText = "Gets system information for Creatio instance.")]
	public sealed class GetCreatioInfoCommandOptions : RemoteCommandOptions
	{ }

	#endregion

	#region Class: GetCreatioInfoCommandCommand

	public sealed class GetCreatioInfoCommand : RemoteCommand<GetCreatioInfoCommandOptions>
	{

		#region Constants: Private

		/// <summary>
		/// Minimum cliogate version that exposes the full <c>GetSysInfo</c> report. When the installed
		/// cliogate is absent or older than this, the command degrades gracefully to
		/// <c>ApplicationInfoService</c> instead of failing.
		/// </summary>
		private const string ClioGateMinVersion = "2.0.0.32";
		private const string InvalidApplicationUriMessage =
			"The application URL is invalid. Use an absolute HTTP or HTTPS URL.";
		private const string UnexpectedApplicationInfoResponseMessage =
			"The Creatio ApplicationInfoService returned an unexpected response.";

		private enum BaseProbeFailure {
			Authentication,
			Connection,
			NonCreatio,
			UnexpectedResponse
		}

		private enum CliogateEnrichmentState {
			UnavailableOrIncompatible,
			CompatibilityUnknown,
			CompatibleWithoutData,
			Reported
		}

		#endregion

		#region Constructors: Public

		public GetCreatioInfoCommand(IApplicationClient applicationClient, 
			EnvironmentSettings environmentSettings, IClioGateway clioGateway)
			: base(applicationClient, environmentSettings){
			ClioGateWay = clioGateway;
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Reports Creatio system information in a single, SOURCE-INDEPENDENT contract so the output shape
		/// is identical with or without cliogate. The base report (core version plus locale / user /
		/// workspace metadata) always comes from the standard <c>ApplicationInfoService</c> and is enriched
		/// with the database engine and executing framework from the admin-gated
		/// <c>GetSystemEnvironmentInfo</c> operation — both WITHOUT cliogate. When a compatible cliogate is
		/// installed, the cliogate-only <c>productName</c> and <c>licenseInfo</c> are merged into the same
		/// object (and cliogate backfills the db/framework fields on Creatio versions that lack
		/// <c>GetSystemEnvironmentInfo</c>). Every source beyond the base is best-effort: a failure degrades
		/// silently and the command still reports what it has.
		/// </summary>
		public override int Execute(GetCreatioInfoCommandOptions options){
			if (!TryGetSafeTargetUri(out Uri targetUri)) {
				Logger.WriteError(InvalidApplicationUriMessage);
				return 1;
			}
			if (!TryGetBaseReport(targetUri, options, out JObject report)) {
				return 1;
			}

			// DbEngineType + framework without cliogate (admin-gated GetSystemEnvironmentInfo).
			TryEnrichWithSystemEnvironmentInfo(report, options);

			// cliogate-only fields (productName, licenseInfo) + db/framework backfill for older Creatio.
			CliogateEnrichmentState cliogateState = TryEnrichFromCliogate(report, options);
			if (cliogateState != CliogateEnrichmentState.Reported){
				string reason = cliogateState switch {
					CliogateEnrichmentState.CompatibleWithoutData =>
						$"cliogate {ClioGateMinVersion}+ is installed but GetSysInfo returned no data "
						+ "(the caller may lack the CanManageSolution permission)",
					CliogateEnrichmentState.CompatibilityUnknown =>
						"cliogate compatibility could not be determined",
					_ => $"cliogate {ClioGateMinVersion}+ is not installed or is incompatible"
				};
				Logger.WriteWarning(
					$"{reason} - ProductName and LicenseInfo are unavailable. All other fields "
					+ "(incl. DbEngineType and framework when CanManageSolution is granted) are reported.");
			}

			Logger.WriteLine(report.ToString());
			return 0;
		}

		#endregion

		#region Methods: Private

		private static IEnumerable<Exception> EnumerateExceptionChain(Exception exception) {
			Stack<Exception> pending = new();
			pending.Push(exception);
			while (pending.Count > 0) {
				Exception current = pending.Pop();
				yield return current;
				if (current is AggregateException aggregateException) {
					foreach (Exception inner in aggregateException.InnerExceptions.Reverse()) {
						pending.Push(inner);
					}
				} else if (current.InnerException is not null) {
					pending.Push(current.InnerException);
				}
			}
		}

		private BaseProbeFailure ClassifyBaseProbeException(Exception exception) {
			IReadOnlyList<Exception> chain = EnumerateExceptionChain(exception).ToArray();
			if (chain.Any(item => item is UnauthorizedAccessException)
					|| TryGetHttpStatus(chain, out HttpStatusCode authStatus)
					&& authStatus is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
				return BaseProbeFailure.Authentication;
			}
			if (TryGetHttpStatus(chain, out HttpStatusCode status)) {
				if (status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) {
					return BaseProbeFailure.NonCreatio;
				}
				if (status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
						or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
						or HttpStatusCode.GatewayTimeout) {
					return BaseProbeFailure.Connection;
				}
				return BaseProbeFailure.UnexpectedResponse;
			}
			if (chain.Any(item => item is TaskCanceledException or TimeoutException or HttpRequestException)) {
				return BaseProbeFailure.Connection;
			}
			if (chain.OfType<WebException>().Any(IsConnectionFailure)) {
				return BaseProbeFailure.Connection;
			}
			return BaseProbeFailure.UnexpectedResponse;
		}

		private static string GetDisplayUri(Uri uri) {
			UriBuilder builder = new(uri) {
				UserName = string.Empty,
				Password = string.Empty,
				Path = string.Empty,
				Query = string.Empty,
				Fragment = string.Empty
			};
			return builder.Uri.GetLeftPart(UriPartial.Authority);
		}

		private static bool IsClearlyNonCreatioContent(string response) {
			if (string.IsNullOrWhiteSpace(response)) {
				return false;
			}
			string trimmed = response.TrimStart();
			// ApplicationInfoService must return an object. Preserve malformed-JSON classification for
			// every unmistakable JSON token prefix, but treat digit-prefixed text such as "404 Not Found"
			// as non-Creatio. A valid numeric JSON scalar parses successfully and is rejected by shape.
			char first = trimmed[0];
			bool hasJsonStart = first is '{' or '[' or '"' or '-'
				|| trimmed.StartsWith("true", StringComparison.Ordinal)
				|| trimmed.StartsWith("false", StringComparison.Ordinal)
				|| trimmed.StartsWith("null", StringComparison.Ordinal);
			return !hasJsonStart;
		}

		private static bool IsRecoverable(Exception exception) =>
			!EnumerateExceptionChain(exception).Any(item => item is
				OutOfMemoryException or StackOverflowException or AccessViolationException or
				NullReferenceException or IndexOutOfRangeException);

		private static bool IsConnectionFailure(WebException exception) => exception.Status is
			WebExceptionStatus.ConnectFailure or
			WebExceptionStatus.ConnectionClosed or
			WebExceptionStatus.KeepAliveFailure or
			WebExceptionStatus.NameResolutionFailure or
			WebExceptionStatus.ProxyNameResolutionFailure or
			WebExceptionStatus.ReceiveFailure or
			WebExceptionStatus.RequestCanceled or
			WebExceptionStatus.SecureChannelFailure or
			WebExceptionStatus.SendFailure or
			WebExceptionStatus.Timeout or
			WebExceptionStatus.TrustFailure;

		private void ReportBaseProbeFailure(BaseProbeFailure failure, Uri targetUri, Exception exception = null,
			int? responseLength = null) {
			string displayUri = GetDisplayUri(targetUri);
			string message = failure switch {
				BaseProbeFailure.Authentication =>
					$"Authentication failed for the Creatio application at '{displayUri}'. "
					+ "Verify the credentials and authentication settings.",
				BaseProbeFailure.Connection =>
					$"Could not connect to the Creatio application at '{displayUri}'. "
					+ "Verify the URL and make sure the application is running.",
				BaseProbeFailure.NonCreatio =>
					$"The URL '{displayUri}' does not appear to be a Creatio application. "
					+ "Verify the application URL and try again.",
				_ => UnexpectedApplicationInfoResponseMessage
			};
			Logger.WriteError(message);
			WriteSafeDebug("base-probe", failure, exception, responseLength);
		}

		private bool TryGetBaseReport(Uri targetUri, GetCreatioInfoCommandOptions options, out JObject report) {
			report = null;
			string response;
			try {
				string appInfoUrl = RootPath + CreatioServicePaths.GetApplicationInfo;
				response = ApplicationClient.ExecutePostRequest(
					appInfoUrl, "{}", options.TimeOut, options.MaxAttempts, options.RetryDelay);
			} catch (Exception exception) when (IsRecoverable(exception)) {
				ReportBaseProbeFailure(ClassifyBaseProbeException(exception), targetUri, exception);
				return false;
			}

			if (ReauthExecutor.IsSessionExpiredResponse(response)) {
				ReportBaseProbeFailure(BaseProbeFailure.Authentication, targetUri, responseLength: response?.Length);
				return false;
			}

			JToken root;
			try {
				root = JToken.Parse(response ?? string.Empty);
			} catch (JsonException exception) {
				BaseProbeFailure failure = IsClearlyNonCreatioContent(response)
					? BaseProbeFailure.NonCreatio
					: BaseProbeFailure.UnexpectedResponse;
				ReportBaseProbeFailure(failure, targetUri, exception, response?.Length);
				return false;
			}

			if (root is not JObject rootObject
					|| rootObject["applicationInfo"]?["sysValues"] is not JObject baseReport
					|| baseReport["coreVersion"]?.Type != JTokenType.String
					|| string.IsNullOrWhiteSpace(baseReport["coreVersion"]?.Value<string>())) {
				ReportBaseProbeFailure(BaseProbeFailure.UnexpectedResponse, targetUri,
					responseLength: response?.Length);
				return false;
			}
			report = baseReport;
			return true;
		}

		private bool TryGetSafeTargetUri(out Uri targetUri) {
			return Uri.TryCreate(EnvironmentSettings?.Uri, UriKind.Absolute, out targetUri)
				&& (string.Equals(targetUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
		}

		private static bool TryGetHttpStatus(IEnumerable<Exception> exceptions, out HttpStatusCode status) {
			foreach (Exception exception in exceptions) {
				if (exception is HttpRequestException { StatusCode: { } httpStatus }) {
					status = httpStatus;
					return true;
				}
				if (exception is WebException { Response: HttpWebResponse response }) {
					status = response.StatusCode;
					return true;
				}
			}
			status = default;
			return false;
		}

		private void WriteSafeDebug(string stage, BaseProbeFailure failure, Exception exception = null,
			int? responseLength = null) {
			if (!Program.IsDebugMode) {
				return;
			}
			string exceptionTypes = exception is null
				? "none"
				: string.Join(" -> ", EnumerateExceptionChain(exception)
					.Select(item => item.GetType().FullName)
					.Distinct(StringComparer.Ordinal));
			IReadOnlyList<Exception> exceptionChain = exception is null
				? []
				: EnumerateExceptionChain(exception).ToArray();
			string status = TryGetHttpStatus(exceptionChain, out HttpStatusCode httpStatus)
				? $"{(int)httpStatus} ({httpStatus})"
				: "none";
			Logger.WriteDebug(
				$"get-info {stage}: classification={failure}; exception-types={exceptionTypes}; "
				+ $"http-status={status}; response-length={responseLength?.ToString() ?? "unknown"}.");
		}

		private CliogateEnrichmentState TryEnrichFromCliogate(
			JObject report, GetCreatioInfoCommandOptions options) {
			if (ClioGateWay is null) {
				return CliogateEnrichmentState.UnavailableOrIncompatible;
			}
			bool compatible;
			try {
				compatible = ClioGateWay.IsCompatibleWith(ClioGateMinVersion);
			} catch (Exception exception) when (IsRecoverable(exception)) {
				WriteSafeDebug("cliogate-compatibility", BaseProbeFailure.UnexpectedResponse, exception);
				return CliogateEnrichmentState.CompatibilityUnknown;
			}
			if (!compatible) {
				return CliogateEnrichmentState.UnavailableOrIncompatible;
			}
			return TryEnrichWithCliogateSysInfo(report, options)
				? CliogateEnrichmentState.Reported
				: CliogateEnrichmentState.CompatibleWithoutData;
		}

		/// <summary>
		/// Best-effort: the admin-gated <c>ApplicationInfoService.GetSystemEnvironmentInfo</c> operation
		/// (ENG-92465) exposes the database engine and executing framework WITHOUT cliogate. Merges
		/// <c>dbEngineType</c>, <c>frameworkKind</c> and <c>frameworkDescription</c> into
		/// <paramref name="report"/> when the call succeeds. Needs the <c>CanManageSolution</c> permission
		/// and a newer Creatio, so any failure (access denied, endpoint absent, transport error) degrades
		/// silently — the <c>ApplicationInfoService</c> base is already reported.
		/// </summary>
		private void TryEnrichWithSystemEnvironmentInfo(JObject report, GetCreatioInfoCommandOptions options){
			try {
				string url = RootPath + CreatioServicePaths.GetSystemEnvironmentInfo;
				// Best-effort probe: single attempt, no retry delay. The retry budget exists to ride out
				// transient blips on a REQUIRED call; here a 404 (operation absent on older Creatio) or 403
				// (no CanManageSolution) is expected and swallowed, so retrying 3x would only add ~2s of dead
				// latency to every describe for a result that will not change on retry.
				string response = ApplicationClient.ExecutePostRequest(
					url, "{}", options.TimeOut, maxAttempts: 1, delaySec: 0);
				JObject info = JObject.Parse(response);
				if (info["success"]?.Type != JTokenType.Boolean || info["success"]?.Value<bool>() != true){
					return;
				}
				foreach (string field in new[] { "dbEngineType", "frameworkKind", "frameworkDescription" }){
					if (info[field] is { } value && value.Type != JTokenType.Null){
						report[field] = value;
					}
				}
			} catch (Exception e) when (IsRecoverable(e)){
				// Degrade silently — the ApplicationInfoService base is already reported. Surface the reason
				// only under --debug so an access/transport failure is diagnosable without polluting normal output.
				WriteSafeDebug("system-environment-enrichment", BaseProbeFailure.UnexpectedResponse, e);
			}
		}

		/// <summary>
		/// Best-effort: when a compatible cliogate is installed, reads <c>GetSysInfo</c> and merges the
		/// cliogate-only <c>productName</c> and <c>licenseInfo</c> into <paramref name="report"/>. Also
		/// backfills <c>dbEngineType</c> / <c>frameworkKind</c> / <c>frameworkDescription</c> from the
		/// cliogate report when <see cref="TryEnrichWithSystemEnvironmentInfo"/> did not set them (Creatio
		/// without the <c>GetSystemEnvironmentInfo</c> operation), keeping the contract consistent. Returns
		/// whether cliogate data was merged.
		/// </summary>
		private bool TryEnrichWithCliogateSysInfo(JObject report, GetCreatioInfoCommandOptions options){
			try {
				string url = RootPath + CreatioServicePaths.GetSysInfo;
				// Best-effort probe: single attempt, no retry delay (see TryEnrichWithSystemEnvironmentInfo).
				string response = ApplicationClient.ExecuteGetRequest(
					url, options.TimeOut, maxAttempts: 1, delaySec: 0);
				if (JObject.Parse(response)["SysInfo"] is not JObject sysInfo){
					return false;
				}
				if (sysInfo["ProductName"] is { } productName && productName.Type != JTokenType.Null){
					report["productName"] = productName;
				}
				if (sysInfo["LicenseInfo"] is { } licenseInfo && licenseInfo.Type != JTokenType.Null){
					report["licenseInfo"] = licenseInfo;
				}
				if (report["dbEngineType"] is null && sysInfo["DbEngineType"] is { } dbEngine && dbEngine.Type != JTokenType.Null){
					report["dbEngineType"] = dbEngine;
				}
				if (report["frameworkDescription"] is null && sysInfo["Runtime"] is { } runtime && runtime.Type != JTokenType.Null){
					report["frameworkDescription"] = runtime;
				}
				if (report["frameworkKind"] is null && sysInfo["IsNetCore"]?.Type == JTokenType.Boolean){
					report["frameworkKind"] = sysInfo["IsNetCore"].Value<bool>() ? "Net" : "NetFramework";
				}
				return true;
			} catch (Exception e) when (IsRecoverable(e)){
				// Degrade silently — the base + GetSystemEnvironmentInfo data is already reported. Surface the
				// reason only under --debug so an access/transport failure is diagnosable.
				WriteSafeDebug("cliogate-sysinfo-enrichment", BaseProbeFailure.UnexpectedResponse, e);
				return false;
			}
		}

		#endregion

	}

	#endregion
}
