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

		#region Constructors: Public

		public GetCreatioInfoCommand(IApplicationClient applicationClient, 
			EnvironmentSettings environmentSettings, IClioGateway clioGateway)
			: base(applicationClient, environmentSettings){
			ClioGateWay = clioGateway;
		}

		#endregion

		#region Properties: Protected

		protected override string ServicePath => "/rest/CreatioApiGateway/GetSysInfo";

		protected override string ClioGateMinVersion { get; } = "2.0.0.32";

		/// <summary>
		/// Standard Creatio service used for the no-cliogate fallback. Requires only an
		/// authenticated session and exposes the core version plus locale/user/workspace
		/// metadata — but not the cliogate-only fields (Runtime, DbEngineType, LicenseInfo,
		/// ProductName).
		/// </summary>
		private const string GetApplicationInfoServicePath = "/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo";

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
			JObject jResponse = JObject.Parse(response);
			JToken sysInfo = jResponse["SysInfo"];
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
				string url = RootPath + GetApplicationInfoServicePath;
				string response = ApplicationClient.ExecutePostRequest(
					url, "{}", options.TimeOut, options.RetryCount, options.RetryDelay);
				JToken sysValues = JObject.Parse(response)["applicationInfo"]?["sysValues"];
				if (sysValues is null){
					Logger.WriteError(
						"cliogate is not installed and ApplicationInfoService returned an unexpected response.");
					return 1;
				}
				Logger.WriteWarning(
					$"cliogate {ClioGateMinVersion}+ is not installed - reporting limited info from ApplicationInfoService. "
					+ "Runtime, DbEngineType, LicenseInfo and ProductName require cliogate.");
				Logger.WriteLine(sysValues.ToString());
				return 0;
			} catch (Exception e){
				Logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
				return 1;
			}
		}

		#endregion

	}

	#endregion
}
