using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command.RedisCommand
{
	[Verb("clear-redis-db", Aliases = new string[] { "flushdb" }, HelpText = "Clear redis database")]
	public class ClearRedisOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }
	}

	public class RedisCommand: BaseRemoteCommand
	{
		public RedisCommand(IApplicationClient applicationClient) 
			: base(applicationClient) {
		}

		private static string ClearRedisDbUrl => _appUrl + @"/ServiceModel/AppInstallerService.svc/ClearRedisDb";

		private void ClearRedisDbInternal() {
			ApplicationClient.ExecutePostRequest(ClearRedisDbUrl, @"{}");
		}

		public int ClearRedisDb(ClearRedisOptions options) {
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
