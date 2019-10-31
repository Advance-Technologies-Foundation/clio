using System;
using CommandLine;

namespace clio.Command.RedisCommand
{
	[Verb("clear-redis-db", Aliases = new string[] { "flushdb" }, HelpText = "Clear redis database")]
	internal class ClearRedisOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }
	}

	class RedisCommand: BaseRemoteCommand
	{
		private static string ClearRedisDbUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/ClearRedisDb";

		private static void ClearRedisDbInternal() {
			BpmonlineClient.ExecutePostRequest(ClearRedisDbUrl, @"{}");
		}

		public static int ClearRedisDb(ClearRedisOptions options) {
			try {
				Configure(options);
				ClearRedisDbInternal();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
