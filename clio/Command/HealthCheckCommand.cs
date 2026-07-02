using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Clio.Common;
using Clio.Common.Responses;
using CommandLine;
using Newtonsoft.Json;

namespace Clio.Command
{
	[Verb("healthcheck", Aliases = new [] { "hc" }, HelpText = "Healthcheck monitoring")]
	public class HealthCheckOptions : RemoteCommandOptions
	{
		[Option('h', "web-host", Required = false, HelpText = "Check web-host", Separator= ' ')]
		public string WebHost { get; set; }

		[Option("WebHost", Required = false, Hidden = true, HelpText = "Alias for --web-host")]
		public string WebHostAlias {
			get => WebHost;
			set { if (!string.IsNullOrEmpty(value)) WebHost = value; }
		}

		[Option('a', "web-app", Required = false, HelpText = "Check web-app")]
		public string WebApp { get; set; }

		[Option("WebApp", Required = false, Hidden = true, HelpText = "Alias for --web-app")]
		public string WebAppAlias {
			get => WebApp;
			set { if (!string.IsNullOrEmpty(value)) WebApp = value; }
		}

		[Option("json", Required = false, HelpText = "Returns response in json format")]
		public bool? Json { get; set; }
	}

	#region Class: HealthCheckResult

	/// <summary>Result of a single healthcheck probe, carried in the unified <c>--json</c> envelope.</summary>
	public sealed record HealthCheckEntry(
		[property: JsonProperty("name")] string Name,
		[property: JsonProperty("uri")] string Uri,
		[property: JsonProperty("ok")] bool Ok,
		[property: JsonProperty("error")] string Error);

	/// <summary>Aggregate healthcheck payload carried in the unified <c>--json</c> envelope's <c>data</c>.</summary>
	public sealed record HealthCheckResult(
		[property: JsonProperty("healthy")] bool Healthy,
		[property: JsonProperty("checks")] IReadOnlyList<HealthCheckEntry> Checks);

	#endregion

	public class HealthCheckCommand : RemoteCommand<HealthCheckOptions>
	{

		#region Constants: Private

		/// <summary>Canonical kebab-case command name, emitted in the unified <c>--json</c> envelope.</summary>
		private const string HealthCheckCommandName = "healthcheck";

		#endregion

		#region Fields: Private

		private readonly IJsonResponseFormater _jsonResponseFormater;

		#endregion

		public HealthCheckCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
				IJsonResponseFormater jsonResponseFormater) : base(applicationClient, settings)
		{
			EnvironmentSettings = settings;
			_jsonResponseFormater = jsonResponseFormater;
		}

		private static bool IsEnabled(string value) =>
			string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

		private string BuildUri(string path) {
			string baseUri = (EnvironmentSettings.Uri ?? string.Empty).TrimEnd('/');
			return $"{baseUri}{path}";
		}

		// Runs a single probe and returns its structured result. Human-readable progress/outcome lines are
		// written only in non-JSON mode, so in --json mode stdout carries exactly one JSON object.
		private HealthCheckEntry Probe(string checkName, string requestUri, bool jsonMode) {
			try
			{
				if (!jsonMode) Logger.WriteInfo($"Checking {checkName} {requestUri} ...");
				ApplicationClient.ExecuteGetRequest(requestUri, RequestTimeout, MaxAttempts, DelaySec);
				if (!jsonMode) Logger.WriteInfo($"\t{checkName} - OK");
				return new HealthCheckEntry(checkName, requestUri, true, null);
			}
			catch (WebException ex)
			{
				if (!jsonMode) Logger.WriteError($"\tError: {ex.Message}");
				return new HealthCheckEntry(checkName, requestUri, false, ex.Message);
			}
			catch (InvalidCastException)
			{
				// creatio.client <= 1.0.33 casts WebRequest.Create() to HttpWebRequest, which fails
				// on macOS/Linux when the runtime returns FileWebRequest for some localhost URLs.
				return ProbeWithHttpClient(checkName, requestUri, jsonMode);
			}
			catch(Exception ex)
			{
				if (!jsonMode) Logger.WriteError($"\tUnknown Error: {ex.Message}");
				return new HealthCheckEntry(checkName, requestUri, false, ex.Message);
			}
		}

		private HealthCheckEntry ProbeWithHttpClient(string checkName, string requestUri, bool jsonMode) {
			if (!jsonMode) {
				Logger.WriteWarning(
					$"\t{checkName} - creatio.client returned a non-HTTP request type; retrying via HttpClient.");
			}
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
					string message = $"{(int)response.StatusCode} {response.ReasonPhrase}";
					if (!jsonMode) Logger.WriteError($"\tError: {message}");
					return new HealthCheckEntry(checkName, requestUri, false, message);
				}
				if (!jsonMode) Logger.WriteInfo($"\t{checkName} - OK");
				return new HealthCheckEntry(checkName, requestUri, true, null);
			}
			catch (Exception ex) {
				if (!jsonMode) Logger.WriteError($"\tError: {ex.Message}");
				return new HealthCheckEntry(checkName, requestUri, false, ex.Message);
			}
		}

		public override int Execute(HealthCheckOptions options) {
			if (!string.IsNullOrEmpty(options.Uri))
				EnvironmentSettings.Uri = options.Uri;
			if (options.IsNetCore.HasValue)
				EnvironmentSettings.IsNetCore = options.IsNetCore.Value;
			RequestTimeout = options.TimeOut;
			MaxAttempts = options.MaxAttempts;
			DelaySec = options.RetryDelay;
			bool jsonMode = options.Json == true;
			bool checkWebApp = IsEnabled(options.WebApp);
			bool checkWebHost = IsEnabled(options.WebHost);

			var checks = new List<(string Name, string Path)>();
			if (!checkWebApp && !checkWebHost)
			{
				checks.Add(EnvironmentSettings.IsNetCore
					? ("WebAppLoader", "/api/HealthCheck/Ping")
					: ("WebHost", "/0/api/HealthCheck/Ping"));
			}
			else
			{
				if (checkWebApp) checks.Add(("WebAppLoader", "/api/HealthCheck/Ping"));
				if (checkWebHost) checks.Add(("WebHost", "/0/api/HealthCheck/Ping"));
			}

			var entries = new List<HealthCheckEntry>(checks.Count);
			foreach ((string name, string path) in checks) {
				entries.Add(Probe(name, BuildUri(path), jsonMode));
			}
			bool healthy = entries.All(entry => entry.Ok);

			if (jsonMode) {
				string json;
				if (healthy) {
					json = _jsonResponseFormater.FormatEnvelope(HealthCheckCommandName,
						new HealthCheckResult(true, entries));
				} else {
					// Contract: on failure data is null and error carries the detail (mutual exclusivity).
					// Summarize the failed probes into the human-readable error message.
					string failed = string.Join("; ",
						entries.Where(entry => !entry.Ok).Select(entry => $"{entry.Name}: {entry.Error}"));
					json = _jsonResponseFormater.FormatEnvelope(HealthCheckCommandName,
						CommandErrorCodes.HealthCheckFailed, $"One or more health checks failed. {failed}");
				}
				Logger.WriteLine(json);
				return healthy ? 0 : CommandErrorCodes.ToExitCode(CommandErrorCodes.HealthCheckFailed);
			}

			// Non-JSON: preserve the historical exit-code contract (sum of failed checks).
			return entries.Count(entry => !entry.Ok);
		}
	}
}
