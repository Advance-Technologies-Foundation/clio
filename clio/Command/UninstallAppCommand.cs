namespace Clio.Command.PackageCommand
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Management.Automation;
	using ATF.Repository;
	using ATF.Repository.Providers;
	using Clio.Common;
	using CommandLine;
	using CreatioModel;
	using Markdig.Helpers;

	[Verb("uninstall-app-remote", Aliases = new string[] { "uninstall" }, HelpText = "Uninstall application")]
	public class UninstallAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Application name")]
		public string Name
		{
			get; set;
		}
	}

	public class UninstallAppCommand : RemoteCommand<UninstallAppOptions>
	{

		public UninstallAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings, ILogger logger)
			: base(applicationClient, settings) {
			Logger = logger;
		}

		public ILogger Logger {
			get;
		}

		protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/UninstallApp";
		protected string GetApplicationIdByNameUrl => RootPath + @"/rest/CreatioApiGateway/GetApplicationIdByName?appName=";

		protected override string GetRequestData(UninstallAppOptions options) {
			if (Guid.TryParse(options.Name, out Guid appid)) {
				return "\"" + appid + "\"";
			} else {
				return "\"" + GetAppIdFromAppName(options.Name) + "\"";
			}
		}

		protected override void ExecuteRemoteCommand(UninstallAppOptions options) {
			Logger.WriteInfo("Uninstalling application");
			base.ExecuteRemoteCommand(options);
		}

		private Guid GetAppIdFromAppName(string name) {
			string response = ApplicationClient.ExecuteGetRequest(GetApplicationIdByNameUrl + name).Trim('\"');
			if (Guid.TryParse(response, out Guid appId)) {
				Logger.WriteInfo($"Found Application Id by {name}: {appId}");
				return appId;
			} else {
				Logger.WriteError(response);
				throw new SilentException(response);
			}
		}
	}
}
