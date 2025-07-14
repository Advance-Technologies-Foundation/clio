using System;
using System.Net;
using System.Net.Http;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("telemetry", Aliases = new[] { "tele" }, HelpText = "Check application telemetry health")]
	public class TelemetryOptions : RemoteCommandOptions
	{
	}

	public class TelemetryCommand : RemoteCommand<TelemetryOptions>
	{
		public TelemetryCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		public override HttpMethod HttpMethod => HttpMethod.Get;

		private int ExecuteGetRequest() {
			try
			{
				string response = ApplicationClient.ExecuteGetRequest(ServiceUri, RequestTimeout, RetryCount, DelaySec);
				Logger.WriteInfo($"Telemetry check completed successfully");
				if (!string.IsNullOrEmpty(response)) {
					Logger.WriteInfo($"Response: {response}");
				}
				return 0;
			}
			catch (WebException ex)
			{
				Logger.WriteError($"Error checking telemetry: {ex.Message}");
				return 1;
			}
			catch (Exception ex)
			{
				Logger.WriteError($"Unknown error: {ex.Message}");
				return 1;
			}
		}

		public override int Execute(TelemetryOptions options) {
			ServicePath = "/rest/Telemetry";
			RequestTimeout = options.TimeOut;
			RetryCount = options.RetryCount;
			DelaySec = options.RetryDelay;
			Logger.WriteInfo($"Checking telemetry endpoint {ServiceUri} ...");
			return ExecuteGetRequest();
		}
	}
}