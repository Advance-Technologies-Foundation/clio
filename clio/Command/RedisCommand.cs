using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("clear-redis-db", Aliases = ["flushdb"], HelpText = "Clear redis database")]
	public class ClearRedisOptions : RemoteCommandOptions
	{
	}

	public class RedisCommand : RemoteCommand<ClearRedisOptions>
	{
		private readonly IServiceUrlBuilder _urlBuilder;

		public RedisCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder urlBuilder)
			: base(applicationClient, settings) {
			_urlBuilder = urlBuilder;
		}

		protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearRedisDb);
	}
}
