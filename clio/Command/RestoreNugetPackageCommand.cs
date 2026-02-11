using System;
using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command
{
	[Verb("restore-nuget-pkg", Aliases = ["rn", "restore-nuget" ], HelpText = "Restore NuGet package to a folder")]
	public class RestoreNugetPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('d', "DestinationDirectory", Required = false, HelpText = "Destination restoring package directory")]
		public string DestinationDirectory { get; set; }

		[Option('s', "Source", Required = false, HelpText = "Specifies the server URL", 
			Default = "")]
		public string SourceUrl { get; set; }

	}

	public class RestoreNugetPackageCommand : Command<RestoreNugetPkgOptions>
	{
		private INuGetManager _nugetManager;
		private readonly ILogger _logger;

		public RestoreNugetPackageCommand(INuGetManager nugetManager, ILogger logger) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_nugetManager = nugetManager;
			_logger = logger;
		}

		public override int Execute(RestoreNugetPkgOptions options) {
			try {
				_nugetManager.RestoreToPackageStorage(new NugetPackageFullName(options.Name), options.SourceUrl, 
					options.DestinationDirectory, true);
				_logger.WriteInfo("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}
	}
}
