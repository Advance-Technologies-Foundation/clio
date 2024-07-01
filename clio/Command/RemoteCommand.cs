﻿using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

public abstract class RemoteCommand<TEnvironmentOptions> : Command<TEnvironmentOptions>
	where TEnvironmentOptions : EnvironmentOptions
{

	#region Constructors: Protected

	protected RemoteCommand(IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings)
		: this(environmentSettings){
		ApplicationClient = applicationClient;
	}

	protected RemoteCommand(EnvironmentSettings environmentSettings){
		EnvironmentSettings = environmentSettings;
	}

	#endregion

	#region Constructors: Public

	public RemoteCommand(){ } // for tests

	#endregion

	#region Properties: Protected

	protected IApplicationClient ApplicationClient { get; }

	protected EnvironmentSettings EnvironmentSettings { get; }

	protected string RootPath =>
		EnvironmentSettings.IsNetCore
			? EnvironmentSettings.Uri : EnvironmentSettings.Uri + @"/0";

	protected virtual string ServicePath { get; set; }

	protected string ServiceUri => RootPath + ServicePath;

	#endregion

	#region Properties: Public

	public virtual HttpMethod HttpMethod => HttpMethod.Post;

	public ILogger Logger { get; set; } = ConsoleLogger.Instance;

	#endregion

	#region Methods: Protected

	protected virtual void ExecuteRemoteCommand(TEnvironmentOptions options){
		string response = HttpMethod == HttpMethod.Post
			? ApplicationClient.ExecutePostRequest(ServiceUri, GetRequestData(options))
			: ApplicationClient.ExecuteGetRequest(ServiceUri);
		ProceedResponse(response, options);
	}

	protected virtual string GetRequestData(TEnvironmentOptions options){
		return "{}";
	}

	protected int Login(){
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

	protected virtual void ProceedResponse(string response, TEnvironmentOptions options){ }

	#endregion

	#region Methods: Public

	public override int Execute(TEnvironmentOptions options){
		try {
			ExecuteRemoteCommand(options);
			string commandName = typeof(TEnvironmentOptions).GetCustomAttribute<VerbAttribute>()?.Name;
			Logger.WriteInfo($"Done {commandName}");
			return 0;
		} catch (SilentException ex) {
			return 1;
		} catch (Exception e) {
			Logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion

}