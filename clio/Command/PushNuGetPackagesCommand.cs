using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using System;
using System.Collections.Generic;

namespace Clio.Command
{
	[Verb("push-nuget-pkgs", Aliases = new string[] { "install" }, HelpText = "Push NuGet package on server")]
	public class PushNuGetPkgsOptions : EnvironmentOptions
	{

		[Value(0, MetaName = "Version", Required = true, HelpText = "Packages version")]
		public string Version { get; set; }

		[Option('r', "PackagesPath", Required = true, HelpText = "Packages path")]
		public string PackagesPath { get; set; }
	}

	public class PushNuGetPackagesCommand : Command<PushNuGetPkgsOptions>
	{
		private EnvironmentSettings _environmentSettings;
		private IPackageFinder _packageFinder; 
		private INuspecFilesGenerator _nuspecFilesGenerator;

		public PushNuGetPackagesCommand(EnvironmentSettings environmentSettings, IPackageFinder packageFinder, 
				INuspecFilesGenerator nuspecFilesGenerator) {
			_environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
			_packageFinder = packageFinder ?? throw new ArgumentNullException(nameof(packageFinder));
			_nuspecFilesGenerator = nuspecFilesGenerator 
				?? throw new ArgumentNullException(nameof(nuspecFilesGenerator));
		}

		public override int Execute(PushNuGetPkgsOptions options) {
			try {
				IDictionary<string, PackageInfo> packagesInfo = _packageFinder.Find(options.PackagesPath);
				_nuspecFilesGenerator.Create(options.PackagesPath, packagesInfo, options.Version);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
