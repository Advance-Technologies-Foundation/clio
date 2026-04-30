using System;
using System.Net;
using System.Net.Http;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("healthcheck", Aliases = new [] { "hc" }, HelpText = "Healthcheck monitoring")]
	public class HealthCheckOptions : RemoteCommandOptions
	{
		[Option('h', "WebHost", Required = false, HelpText = "Check web-host", Separator= ' ')]
		public string WebHost { get; set; }
		
		[Option('a', "WebApp", Required = false, HelpText = "Check web-app")]
		public string WebApp { get; set; }
	}

	public class HealthCheckCommand : RemoteCommand<HealthCheckOptions>
	{
	
		public HealthCheckCommand(IApplicationClient applicationClient, EnvironmentSettings settings): base(applicationClient, settings)
		{
			EnvironmentSettings = settings;
		}

		private static bool IsEnabled(string value) =>
			string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

		private string BuildUri(string path) {
			string baseUri = (EnvironmentSettings.Uri ?? string.Empty).TrimEnd('/');
			return $"{baseUri}{path}";
		}

		private int ExecuteGetRequest(string checkName, string requestUri) {
			try
			{
				Logger.WriteInfo($"Checking {checkName} {requestUri} ...");
				ApplicationClient.ExecuteGetRequest(requestUri, RequestTimeout, RetryCount, DelaySec);
				Logger.WriteInfo($"\t{checkName} - OK");
			}
			catch (WebException ex)
			{
				Logger.WriteError($"\tError: {ex.Message}");
				return 1;
			}
			catch (InvalidCastException)
			{
				// creatio.client <= 1.0.33 casts WebRequest.Create() to HttpWebRequest, which fails
				// on macOS/Linux when the runtime returns FileWebRequest for some localhost URLs.
				return ProbeWithHttpClient(checkName, requestUri);
			}
			catch(Exception ex)
			{
				Logger.WriteError($"\tUnknown Error: {ex.Message}");
				return 1;
			}
			return 0;
		}

		private int ProbeWithHttpClient(string checkName, string requestUri) {
			Logger.WriteWarning(
				$"\t{checkName} - creatio.client returned a non-HTTP request type; retrying via HttpClient.");
			try {
				using HttpClientHandler handler = new() {
					ServerCertificateCustomValidationCallback = (_, _, _, _) => true
				};
				using HttpClient client = new(handler) {
					Timeout = RequestTimeout > 0
						? TimeSpan.FromMilliseconds(RequestTimeout)
						: TimeSpan.FromSeconds(100)
				};
				using HttpResponseMessage response = client.GetAsync(requestUri).GetAwaiter().GetResult();
				if (!response.IsSuccessStatusCode) {
					Logger.WriteError($"\tError: {(int)response.StatusCode} {response.ReasonPhrase}");
					return 1;
				}
				Logger.WriteInfo($"\t{checkName} - OK");
				return 0;
			}
			catch (Exception ex) {
				Logger.WriteError($"\tError: {ex.Message}");
				return 1;
			}
		}

		public override int Execute(HealthCheckOptions options) {
			RequestTimeout = options.TimeOut;
			RetryCount = options.RetryCount;
			DelaySec = options.RetryDelay;
			bool checkWebApp = IsEnabled(options.WebApp);
			bool checkWebHost = IsEnabled(options.WebHost);
			if (!checkWebApp && !checkWebHost)
			{
				return EnvironmentSettings.IsNetCore
					? ExecuteGetRequest("WebAppLoader", BuildUri("/api/HealthCheck/Ping"))
					: ExecuteGetRequest("WebHost", BuildUri("/0/api/HealthCheck/Ping"));
			}
			int result = 0;
			if (checkWebApp)
			{
				result += ExecuteGetRequest("WebAppLoader", BuildUri("/api/HealthCheck/Ping"));
			}
			if (checkWebHost)
			{
				result += ExecuteGetRequest("WebHost", BuildUri("/0/api/HealthCheck/Ping"));
			}
			return result;
		}
	}
}
