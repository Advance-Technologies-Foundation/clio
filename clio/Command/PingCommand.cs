﻿using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("ping-app", Aliases = new string[] { "ping" }, HelpText = "Check current credentional for selected environments")]
	public class PingAppOptions : RemoteCommandOptions
	{
		[Option('x', "Endpoint", Required = false, HelpText = "Relative path for checked endpoint (by default ise Ping service)")]
		public string Endpoint { get; set; } = "/ping";
	}

	public class PingAppCommand : RemoteCommand<PingAppOptions>
	{
		public PingAppCommand() { } // for tests

		public PingAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
			EnvironmentSettings = settings;
		}

		private int ExecuteGetRequest() {
			ApplicationClient.ExecuteGetRequest(RootPath, RequestTimeout, RetryCount, DelaySec);
			Logger.WriteInfo("Done");
			return 0;
		}

		protected override void ProceedResponse(string response, PingAppOptions options){
			base.ProceedResponse(response, options);
			if(response.Trim() != "Pong") {
				throw new Exception("Ping failed, expected to receive 'Pong' instead saw: " + response);
			}
		}

		public override int Execute(PingAppOptions options) {
			ServicePath = options.Endpoint;
			RequestTimeout = options.TimeOut;
			RetryCount = options.RetryCount;
			DelaySec = options.RetryDelay;
			Logger.WriteInfo($"Ping {ServiceUri} ...");
			return EnvironmentSettings.IsNetCore ? ExecuteGetRequest() : base.Execute(options);
		}

	}
}
