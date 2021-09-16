﻿using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("ping-app", Aliases = new string[] { "ping" }, HelpText = "Check current credentional for selected environments")]
	public class PingAppOptions : EnvironmentNameOptions
	{
		[Option('x', "Endpoint", Required = false, HelpText = "Relative path for checked endpoint (by default ise Ping service)")]
		public string Endpoint { get; set; } = "/ping";
	}

	public class PingAppCommand : RemoteCommand<PingAppOptions>
	{
		public PingAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		public override int Execute(PingAppOptions options) {
			ServicePath = options.Endpoint;
			Console.WriteLine($"Ping {ServiceUri} ...");
			return base.Execute(options);
		}


	}
}
