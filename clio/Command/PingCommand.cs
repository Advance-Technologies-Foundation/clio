using System;
using System.Net;
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

	public class PingAppCommand : RemoteCommand<PingAppOptions>
	{
		public PingAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		public override int Execute(PingAppOptions options) {
			try {
				return Login();
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
