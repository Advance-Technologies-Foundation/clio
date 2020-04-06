using System;
using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command
{
	[Verb("restore-nuget-pkg", Aliases = new string[] { "restore" }, HelpText = "Restore NuGet package to a folder")]
	public class RestoreNugetPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('n', "NupkgDirectory", Required = false, HelpText = "Nupkg package directory")]
		public string NupkgDirectory { get; set; }

		[Option('v', "Version", Required = false, HelpText = "Version NuGet package", 
			Default = PackageVersion.LastVersion)]
		public string Version { get; set; }

		[Option('s', "Source", Required = false, HelpText = "Specifies the server URL", 
			Default = "https://www.nuget.org")]
		public string SourceUrl { get; set; }

	}

	public class RestoreNugetPackageCommand : Command<RestoreNugetPkgOptions>
	{
		private INuGetManager _nugetManager;

		public RestoreNugetPackageCommand(INuGetManager nugetManager) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_nugetManager = nugetManager;
		}

		public override int Execute(RestoreNugetPkgOptions options) {
			try {
				_nugetManager.RestoreToPackageStorage(options.Name, options.Version, options.SourceUrl, 
					options.NupkgDirectory, true);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}