using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("ping", Aliases = new string[] { "ping-app" }, HelpText = "Check current credentional for selected environments")]
	public class PingAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Environment name")]
		public string Name { get => Environment; set { Environment = value; } }
	}

	public class PingAppCommand: BaseRemoteCommand
	{
		public static int Ping(EnvironmentOptions options) {
			try {
				Configure(options);
				Console.WriteLine($"Try login to {Settings.Uri} with {Settings.Login} credentials...");
				CreatioClient.Login();
				Console.WriteLine("Login done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
