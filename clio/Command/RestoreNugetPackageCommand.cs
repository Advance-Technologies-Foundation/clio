using System;
using System.IO;
using System.Text;
using System.Threading;
using Clio.Common;
using Clio.Project.NuGet;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("restore-nuget-pkg", Aliases = new string[] { "restore" }, HelpText = "Restore NuGet package on a web application")]
	public class RestoreNugetPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }
		
		[Option('v', "Version", Required = false, HelpText = "Version NuGet package")]
		public string Version { get; set; }

		[Option('s', "Source", Required = true, HelpText = "Specifies the server URL")]
		public string SourceUrl { get; set; }

		[Option('n', "NupkgDirectory", Required = true, HelpText = "Nupkg package directory")]
		public string NupkgDirectory { get; set; }

		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }

	}

	public class RestoreNugetPackageCommand : Command<RestoreNugetPkgOptions>
	{
		private INuGetManager _nugetManager;
		private PushPackageCommand _pushPackageCommand; 

		public RestoreNugetPackageCommand(INuGetManager nugetManager, 
				PushPackageCommand pushPackageCommand) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			pushPackageCommand.CheckArgumentNull(nameof(pushPackageCommand));
			_nugetManager = nugetManager;
			_pushPackageCommand = pushPackageCommand;
		}

		public override int Execute(RestoreNugetPkgOptions options) {
			try {
				string result = _nugetManager.Restore(options.Name, options.Version, options.SourceUrl, 
					options.NupkgDirectory);
				Console.WriteLine(result);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}