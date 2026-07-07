using System;
using CommandLine;
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
			try {
				string appInfoUrl = RootPath + CreatioServicePaths.GetApplicationInfo;
				string appInfoResponse = ApplicationClient.ExecutePostRequest(
					appInfoUrl, "{}", options.TimeOut, options.MaxAttempts, options.RetryDelay);
				if (JObject.Parse(appInfoResponse)["applicationInfo"]?["sysValues"] is not JObject report){
					Logger.WriteError("ApplicationInfoService returned an unexpected response.");
					return 1;
				}

				// DbEngineType + framework without cliogate (admin-gated GetSystemEnvironmentInfo).
				TryEnrichWithSystemEnvironmentInfo(report, options);

				// cliogate-only fields (productName, licenseInfo) + db/framework backfill for older Creatio.
				bool cliogateCompatible = ClioGateWay is not null
					&& ClioGateWay.IsCompatibleWith(ClioGateMinVersion);
				bool cliogateReported = cliogateCompatible && TryEnrichWithCliogateSysInfo(report, options);
				if (!cliogateReported){
					// Distinguish "not installed/incompatible" from "installed but GetSysInfo returned nothing"
					// (typically the caller lacks CanManageSolution) — claiming "not installed" when it is would
					// be misleading.
					string reason = cliogateCompatible
						? $"cliogate {ClioGateMinVersion}+ is installed but GetSysInfo returned no data "
							+ "(the caller may lack the CanManageSolution permission)"
						: $"cliogate {ClioGateMinVersion}+ is not installed";
					Logger.WriteWarning(
						$"{reason} - ProductName and LicenseInfo are unavailable. All other fields "
						+ "(incl. DbEngineType and framework when CanManageSolution is granted) are reported.");
				}

				Logger.WriteLine(report.ToString());
				return 0;
			} catch (Exception e){
				Logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
				return 1;
			}
		}

		#endregion

		#region Methods: Private

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
				if (info["success"]?.Value<bool>() != true){
					return;
				}
				foreach (string field in new[] { "dbEngineType", "frameworkKind", "frameworkDescription" }){
					if (info[field] is { } value && value.Type != JTokenType.Null){
						report[field] = value;
					}
				}
			} catch (Exception e){
				// Degrade silently — the ApplicationInfoService base is already reported. Surface the reason
				// only under --debug so an access/transport failure is diagnosable without polluting normal output.
				if (Program.IsDebugMode){
					Logger.WriteWarning($"GetSystemEnvironmentInfo skipped: {e.Message}");
				}
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
			} catch (Exception e){
				// Degrade silently — the base + GetSystemEnvironmentInfo data is already reported. Surface the
				// reason only under --debug so an access/transport failure is diagnosable.
				if (Program.IsDebugMode){
					Logger.WriteWarning($"cliogate GetSysInfo skipped: {e.Message}");
				}
				return false;
			}
		}

		#endregion

	}

	#endregion
}
