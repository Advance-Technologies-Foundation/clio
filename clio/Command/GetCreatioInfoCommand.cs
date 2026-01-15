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

		#endregion

		#region Properties: Public

		public override HttpMethod HttpMethod => HttpMethod.Get;

		#endregion

		#region Methods: Protected

		protected override void ProceedResponse(string response, GetCreatioInfoCommandOptions options){
			base.ProceedResponse(response, options);
			JObject jResponse = JObject.Parse(response);
			JToken sysInfo = jResponse["SysInfo"];
			Logger.WriteLine(sysInfo.ToString());
		}

		#endregion

	}

	#endregion
}
