using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("ping-app", Aliases = new[]
{
    "ping"
}, HelpText = "Check current credentional for selected environments")]
public class PingAppOptions : RemoteCommandOptions
{

    #region Properties: Public

    [Option('x', "Endpoint", Required = false,
        HelpText = "Relative path for checked endpoint (by default ise Ping service)")]
    public string Endpoint { get; set; } = "/ping";

    #endregion

}

public class PingAppCommand : RemoteCommand<PingAppOptions>
{

    #region Constructors: Public

    public PingAppCommand()
    { } // for tests

    public PingAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    {
        EnvironmentSettings = settings;
    }

    #endregion

    #region Methods: Private

    private int ExecuteGetRequest()
    {
        ApplicationClient.ExecuteGetRequest(RootPath, RequestTimeout, RetryCount, DelaySec);
        Logger.WriteInfo("Done");
        return 0;
    }

    #endregion

    #region Methods: Public

    public override int Execute(PingAppOptions options)
    {
        ServicePath = options.Endpoint;
        RequestTimeout = options.TimeOut;
        RetryCount = options.RetryCount;
        DelaySec = options.RetryDelay;
        Logger.WriteInfo($"Ping {ServiceUri} ...");
        return EnvironmentSettings.IsNetCore ? ExecuteGetRequest() : base.Execute(options);
    }

    #endregion

}
