using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("clear-redis-db", Aliases = new[] { "flushdb" }, HelpText = "Clear redis database")]
public class ClearRedisOptions : RemoteCommandOptions
{
}

public class RedisCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
    : RemoteCommand<ClearRedisOptions>(applicationClient, settings)
{
    protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/ClearRedisDb";
}
