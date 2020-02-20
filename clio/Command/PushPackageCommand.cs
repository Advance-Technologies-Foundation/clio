using System;
using System.IO;
using System.Text;
using System.Threading;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace Clio.Command
{
	[Verb("push-pkg", Aliases = new string[] { "install" }, HelpText = "Install package on a web application")]
	public class PushPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }
	}

	public class PushPackageCommand : RemoteCommand<PushPkgOptions>
	{
		private readonly IProjectUtilities _projectUtilities;
		private readonly ISettingsRepository _settingsRepository;
		private readonly ISqlScriptExecutor _scriptExecutor;

		private static string InstallUrl => @"/ServiceModel/PackageInstallerService.svc/InstallPackage";
		private static string LogUrl => @"/ServiceModel/PackageInstallerService.svc/GetLogFile";
		private static string UploadUrl => @"/ServiceModel/PackageInstallerService.svc/UploadPackage";
		private static string DefLogFileName => "cliolog.txt";

		public PushPackageCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
				IProjectUtilities projectUtilities, ISettingsRepository settingsRepository,
				ISqlScriptExecutor scriptExecutor)
			: base(applicationClient, settings) {
			_projectUtilities = projectUtilities;
			_settingsRepository = settingsRepository;
			_scriptExecutor = scriptExecutor;
		}

		private string GetCompleteUrl(string url) {
			return EnvironmentSettings.Uri + (EnvironmentSettings.IsNetCore ? string.Empty : "/0/") + url;
		}

		private void UnlockMaintainerPackageInternal(EnvironmentSettings settings) {
			var script = $"UPDATE SysPackage SET InstallType = 0 WHERE Maintainer = '{settings.Maintainer}'";
			_scriptExecutor.Execute(script, ApplicationClient, settings);
		}

		private (bool, string) InstallPackage(string filePath, EnvironmentOptions options) {
			string fileName;
			try {
				fileName = UploadPackage(filePath);
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return (false, e.Message);
			}
			Console.WriteLine($"Install {fileName} ...");
			string result = ApplicationClient.ExecutePostRequest(GetCompleteUrl(InstallUrl), "\"" + fileName + "\"", Timeout.Infinite);
			dynamic resultObj = JObject.Parse(result);
			var settings = _settingsRepository.GetEnvironment(options);
			if (settings.DeveloperModeEnabled.HasValue && settings.DeveloperModeEnabled.Value) {
				UnlockMaintainerPackageInternal(settings);
				new RestartCommand(ApplicationClient, EnvironmentSettings).Execute(new RestartOptions());
			}
			var logText = GetLog();
			Console.WriteLine("Installation log:");
			Console.WriteLine(logText);
			return (resultObj.success, logText);
		}

		private string GetLog() {
			return ApplicationClient.ExecuteGetRequest(GetCompleteUrl(LogUrl));
		}

		private string UploadPackage(string filePath) {
			Console.WriteLine("Uploading...");
			FileInfo fileInfo = new FileInfo(filePath);
			string fileName = fileInfo.Name;
			ApplicationClient.UploadFile(GetCompleteUrl(UploadUrl), filePath);
			Console.WriteLine("Uploaded");
			return fileName;
		}

		private void SaveLogFile(string logText, string reportPath) {
			if (File.Exists(reportPath)) {
				File.Delete(reportPath);
				File.WriteAllText(reportPath, logText, Encoding.UTF8);
			} else if (Directory.Exists(reportPath)) {
				reportPath = Path.Combine(reportPath, DefLogFileName);
			}
			File.WriteAllText(reportPath, logText, Encoding.UTF8);
		}

		public override int Execute(PushPkgOptions options) {
			var result = (success: false, logText: String.Empty);
			try {
				if (options.Name == null) {
					options.Name = Environment.CurrentDirectory;
				}
				string logText = string.Empty;
				if (File.Exists(options.Name)) {
					result = InstallPackage(options.Name, options);
				} else if (Directory.Exists(options.Name)) {
					var folderPath = options.Name;
					var filePath = options.Name + ".gz";
					_projectUtilities.CompressProject(folderPath, filePath, false);
					result = InstallPackage(filePath, options);
					File.Delete(filePath);
				} else {
					Console.WriteLine("Specified package not found.");
				}
				Console.WriteLine("Done");
			} catch (Exception e) {
				Console.WriteLine(e.Message);
			}
			if (options.ReportPath != null)
			{
				SaveLogFile(result.logText, options.ReportPath);
			}
			return (result.success) ? 0 : 1;
		}
	}
}
