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
		private readonly IDataProvider dataProvider;

		public UninstallAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IDataProvider dataProvider)
			: base(applicationClient, settings) {
			this.dataProvider = dataProvider;
		}

		protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/UninstallApp";

		protected override string GetRequestData(UninstallAppOptions options) {
			if (Guid.TryParse(options.Name, out Guid appid)) {
				return "\"" + appid + "\"";
			} else {
				return "\"" + GetAppIdFromAppName(options.Name) + "\"";
			}
		}

		private Guid GetAppIdFromAppName(string name) {
			var context = AppDataContextFactory.GetAppDataContext(dataProvider);
			var apps = context.Models<SysInstalledApp>().Where(x => x.Name == name).ToList();
			if (apps.Count() == 0) {
				throw new ItemNotFoundException($"Application \"{name}\" not found.");
			}
			if (apps.Count() > 1) {
				throw new ArgumentOutOfRangeException($"Found more the one application: {PrintArrayInOneLine(apps)}");
			}
			return apps[0].Id;
		}

		private string PrintArrayInOneLine(IEnumerable<object> array) {
			string result = string.Join(", ", array.Select(x => x.ToString()));
			return result;
		}

	}
}
