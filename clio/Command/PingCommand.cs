using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("ping-app", Aliases = new string[] { "ping" }, HelpText = "Check current credentional for selected environments")]
	public class PingAppOptions : EnvironmentNameOptions
	{
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
