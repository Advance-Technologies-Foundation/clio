using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("clear-redis-db", Aliases = new string[] { "flushdb" }, HelpText = "Clear redis database")]
	public class ClearRedisOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get => Environment; set { Environment = value; } }
	}

	public class RedisCommand: RemoteCommand<ClearRedisOptions>
	{
		public RedisCommand(IApplicationClient applicationClient, EnvironmentSettings settings) 
			: base(applicationClient, settings) {
		}

		private string ClearRedisDbUrl => EnvironmentSettings.IsNetCore 
			? EnvironmentSettings.Uri + @"/ServiceModel/AppInstallerService.svc/ClearRedisDb"
			: EnvironmentSettings.Uri + @"/0/ServiceModel/AppInstallerService.svc/ClearRedisDb";

		private void ClearRedisDbInternal() {
			ApplicationClient.ExecutePostRequest(ClearRedisDbUrl, @"{}");
		}

		public override int Execute(ClearRedisOptions options) {
			try {
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
