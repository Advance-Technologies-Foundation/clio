using System;
using System.Net.Http;
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

		#region Properties: Protected

		protected override string ServicePath => CreatioServicePaths.GetSysInfo;

		#endregion

		#region Properties: Public

		public override HttpMethod HttpMethod => HttpMethod.Get;

		#endregion

		#region Methods: Public

		/// <summary>
		/// Reports Creatio system information. The full report comes from cliogate
		/// <c>GetSysInfo</c>; when cliogate is absent or older than <see cref="ClioGateMinVersion"/>
		/// the command degrades gracefully to the standard <c>ApplicationInfoService</c> instead of
		/// failing — that path needs only authentication and still surfaces the core version.
		/// </summary>
		public override int Execute(GetCreatioInfoCommandOptions options){
			if (ClioGateWay is null || !ClioGateWay.IsCompatibleWith(ClioGateMinVersion)){
				return ExecuteApplicationInfoFallback(options);
			}
			return base.Execute(options);
		}

		#endregion

		#region Methods: Protected

		protected override void ProceedResponse(string response, GetCreatioInfoCommandOptions options){
			base.ProceedResponse(response, options);
			JToken sysInfo = JObject.Parse(response)["SysInfo"];
			if (sysInfo is null){
				Logger.WriteError("cliogate GetSysInfo returned an unexpected response (no SysInfo node).");
				return;
			}
			Logger.WriteLine(sysInfo.ToString());
		}

		#endregion

		#region Methods: Private

		/// <summary>
		/// No-cliogate fallback: reads core/system metadata from the standard
		/// <c>ApplicationInfoService</c> and prints its <c>sysValues</c> block. Cliogate-only
		/// fields are unavailable here and the user is told so.
		/// </summary>
		private int ExecuteApplicationInfoFallback(GetCreatioInfoCommandOptions options){
			try {
				string url = RootPath + CreatioServicePaths.GetApplicationInfo;
				string response = ApplicationClient.ExecutePostRequest(
					url, "{}", options.TimeOut, options.MaxAttempts, options.RetryDelay);
				if (JObject.Parse(response)["applicationInfo"]?["sysValues"] is not JObject sysValues){
					Logger.WriteError(
						"cliogate is not installed and ApplicationInfoService returned an unexpected response.");
					return 1;
				}
				bool enriched = TryEnrichWithSystemEnvironmentInfo(sysValues, options);
				Logger.WriteWarning(enriched
					? $"cliogate {ClioGateMinVersion}+ is not installed - core info from ApplicationInfoService, "
						+ "enriched with DbEngineType and Runtime from GetSystemEnvironmentInfo. "
						+ "LicenseInfo and ProductName still require cliogate."
					: $"cliogate {ClioGateMinVersion}+ is not installed - reporting limited info from ApplicationInfoService. "
						+ "Runtime, DbEngineType, LicenseInfo and ProductName require cliogate.");
				Logger.WriteLine(sysValues.ToString());
				return 0;
			} catch (Exception e){
				Logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
				return 1;
			}
		}

		/// <summary>
		/// Best-effort enrichment of the no-cliogate report: the admin-gated
		/// <c>ApplicationInfoService.GetSystemEnvironmentInfo</c> operation (ENG-92465) exposes the
		/// database engine and executing framework WITHOUT cliogate. Merges <c>dbEngineType</c>,
		/// <c>frameworkKind</c> and <c>frameworkDescription</c> into <paramref name="sysValues"/> when
		/// the call succeeds. The operation needs the <c>CanManageSolution</c> permission and exists only
		/// on newer Creatio, so any failure (access denied, endpoint absent, transport error) degrades
		/// silently — the command still reports the ApplicationInfoService data. Returns whether the
		/// enrichment was applied.
		/// </summary>
		private bool TryEnrichWithSystemEnvironmentInfo(JObject sysValues, GetCreatioInfoCommandOptions options){
			try {
				string url = RootPath + CreatioServicePaths.GetSystemEnvironmentInfo;
				string response = ApplicationClient.ExecutePostRequest(
					url, "{}", options.TimeOut, options.MaxAttempts, options.RetryDelay);
				JObject info = JObject.Parse(response);
				if (info["success"]?.Value<bool>() != true){
					return false;
				}
				bool applied = false;
				foreach (string field in new[] { "dbEngineType", "frameworkKind", "frameworkDescription" }){
					if (info[field] is { } value && value.Type != JTokenType.Null){
						sysValues[field] = value;
						applied = true;
					}
				}
				return applied;
			} catch (Exception){
				// Gated (no CanManageSolution), absent on older Creatio, or transport error — degrade
				// silently so describe still works; the ApplicationInfoService data is already reported.
				return false;
			}
		}

		#endregion

	}

	#endregion
}
