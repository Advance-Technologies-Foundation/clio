using System.Net.Http;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace Clio.Command;

#region Class: GetCreatioInfoCommandCommandOptions

//clio get-info -e work

[Verb("get-info", Aliases = new[] {"describe", "describe-creatio", "instance-info"},
	HelpText = "Gets system information for Creatio instance.")]
public class GetCreatioInfoCommandOptions : EnvironmentOptions
{ }

#endregion

#region Class: GetCreatioInfoCommandCommand

public class GetCreatioInfoCommand : RemoteCommand<GetCreatioInfoCommandOptions>
{

	#region Constructors: Public

	public GetCreatioInfoCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
		: base(applicationClient, environmentSettings){ }

	#endregion

	#region Properties: Protected

	protected override string ServicePath => "/rest/CreatioApiGateway/GetSysInfo";

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