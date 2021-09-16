using Clio.Common;
using CommandLine;
using System;

namespace Clio.Command
{
	[Verb("download-pkg-to-fs", Aliases = new string[] { "dptf" }, HelpText = "Download packages to file system")]
	public class DownloadPkgToFsOptions : EnvironmentOptions
	{
	}

	public class DownloadPkgToFsCommand : RemoteCommand<DownloadPkgToFsOptions>
	{
		public DownloadPkgToFsCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings){
		}
		protected override string ServicePath => EnvironmentSettings.IsNetCore ? @"/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem" : @"/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem";

		public override int Execute(DownloadPkgToFsOptions options)
		{
			Console.WriteLine($"Loading Packages ToFile System: ...");
			string json = ApplicationClient.ExecutePostRequest(ServiceUri, "");
			var jDoc = System.Text.Json.JsonDocument.Parse(json);

			if (!string.IsNullOrEmpty(jDoc.RootElement.GetProperty("errorInfo").ToString() ))
			{
				var originalColor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine(jDoc.RootElement.GetProperty("errorInfo").ToString());
				Console.ForegroundColor = originalColor;
			}
			else
			{
				Console.WriteLine("DONE");
			}
			return 0;
		}
	}
}
