using System;
using System.Net;
using System.Net.Http;
using Clio.Common;

namespace Clio.Command
{
	using System.Linq;
	using System.Reflection;
	using CommandLine;

	public abstract class RemoteCommand<TEnvironmentOptions> : Command<TEnvironmentOptions>
		where TEnvironmentOptions : EnvironmentOptions
	{
		public RemoteCommand() { } // for tests

		protected string RootPath => EnvironmentSettings.IsNetCore
			? EnvironmentSettings.Uri : EnvironmentSettings.Uri + @"/0";

		protected virtual string ServicePath { get; set; }

		protected string ServiceUri => RootPath + ServicePath;

		protected IApplicationClient ApplicationClient { get; }
		protected EnvironmentSettings EnvironmentSettings { get; }

		private ILogger _logger = new ConsoleLogger();
		public ILogger Logger
		{
			get => _logger;
			set
			{
				_logger = value;
			}
		}

		protected RemoteCommand(IApplicationClient applicationClient,
				EnvironmentSettings environmentSettings) {
			ApplicationClient = applicationClient;
			EnvironmentSettings = environmentSettings;
		}

		protected RemoteCommand(EnvironmentSettings environmentSettings) {
			EnvironmentSettings = environmentSettings;
		}

		public virtual HttpMethod HttpMethod => HttpMethod.Post;

		protected int Login() {
			try {
				Logger.WriteInfo($"Try login to {EnvironmentSettings.Uri} with {EnvironmentSettings.Login} credentials...");
				ApplicationClient.Login();
				Logger.WriteInfo("Login done");
				return 0;
			} catch (WebException we) {
				HttpWebResponse errorResponse = we.Response as HttpWebResponse;
				if (errorResponse.StatusCode == HttpStatusCode.NotFound) {
					Logger.WriteError($"Application {EnvironmentSettings.Uri} not found");
				}
				return 1;
			}
		}


		public override int Execute(TEnvironmentOptions options) {
			try {
				ExecuteRemoteCommand(options);
				string commandName = typeof(TEnvironmentOptions).GetCustomAttribute<VerbAttribute>()?.Name;
				Logger.WriteInfo($"Done {commandName}");
				return 0;
			} 
			catch (SilentException ex) {
				return 1;
			}
			catch (Exception e) {
				Logger.WriteError(e.Message);
				return 1;
			}
		}

		protected virtual void ExecuteRemoteCommand(TEnvironmentOptions options) {
			string response;
			if (HttpMethod == HttpMethod.Post) {
				response = ApplicationClient.ExecutePostRequest(ServiceUri, GetRequestData(options));
			} else {
				response = ApplicationClient.ExecuteGetRequest(ServiceUri);
			}
			ProceedResponse(response, options);
		}

		protected virtual void ProceedResponse(string response, TEnvironmentOptions options) {
		}

		protected virtual string GetRequestData(TEnvironmentOptions options) {
			return "{}";
		}

	}
}
