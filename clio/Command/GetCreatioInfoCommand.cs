using System.Net.Http;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace Clio.Command;

// clio get-info -e work
[Verb("get-info", Aliases = new[] { "describe", "describe-creatio", "instance-info" },
    HelpText = "Gets system information for Creatio instance.")]
public class GetCreatioInfoCommandOptions : RemoteCommandOptions
{
}

public class GetCreatioInfoCommand : RemoteCommand<GetCreatioInfoCommandOptions>
{
    public GetCreatioInfoCommand(
        IApplicationClient applicationClient,
        EnvironmentSettings environmentSettings, IClioGateway clioGateway)
        : base(applicationClient, environmentSettings) =>
        ClioGateWay = clioGateway;

    protected override string ServicePath => "/rest/CreatioApiGateway/GetSysInfo";

    protected override string ClioGateMinVersion { get; } = "2.0.0.32";

    public override HttpMethod HttpMethod => HttpMethod.Get;

    protected override void ProceedResponse(string response, GetCreatioInfoCommandOptions options)
    {
        base.ProceedResponse(response, options);
        JObject jResponse = JObject.Parse(response);
        JToken sysInfo = jResponse["SysInfo"];
        Logger.WriteLine(sysInfo.ToString());
    }
}
