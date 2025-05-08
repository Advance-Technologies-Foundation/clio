using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("clear-redis-db", Aliases = new[]
{
    "flushdb"
}, HelpText = "Clear redis database")]
public class ClearRedisOptions : RemoteCommandOptions
{ }

public class RedisCommand : RemoteCommand<ClearRedisOptions>
{

    #region Constructors: Public

    public RedisCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    { }

    #endregion

    #region Properties: Protected

    protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/ClearRedisDb";

    #endregion

}
