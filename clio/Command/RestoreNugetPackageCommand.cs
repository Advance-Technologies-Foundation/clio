using System;
using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command
{
	[Verb("restore-nuget-pkg", Aliases = new string[] { "restore-nuget", "rn" }, HelpText = "Restore NuGet package to a folder")]
	public class RestoreNugetPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('d', "DestinationDirectory", Required = false, HelpText = "Destination restoring package directory")]
		public string DestinationDirectory { get; set; }

		[Option('s', "Source", Required = false, HelpText = "Specifies the server URL", 
			Default = "https://www.nuget.org/api/v2")]
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
				_nugetManager.RestoreToPackageStorage(new NugetPackageFullName(options.Name), options.SourceUrl, 
					options.DestinationDirectory, true);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}