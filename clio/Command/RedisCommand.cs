using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("clear-redis-db", Aliases = new string[] { "flushdb" }, HelpText = "Clear redis database")]
	public class ClearRedisOptions : EnvironmentNameOptions
	{
	}

	public class RedisCommand : RemoteCommand<ClearRedisOptions>
	{
		public RedisCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath =>  @"/ServiceModel/AppInstallerService.svc/ClearRedisDb";

	}
}
