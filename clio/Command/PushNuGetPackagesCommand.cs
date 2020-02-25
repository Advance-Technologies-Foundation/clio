using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using Clio.Project.NuGet;

namespace Clio.Command
{
	[Verb("push-nuget-pkgs", Aliases = new string[] { "nuget" }, HelpText = "Push NuGet package on server")]
	public class PushNuGetPkgsOptions : EnvironmentOptions
	{

		[Value(0, MetaName = "Version", Required = true, HelpText = "Packages version")]
		public string Version { get; set; }

		[Option('r', "PackagesPath", Required = true, HelpText = "Packages path")]
		public string PackagesPath { get; set; }
		
		[Option('k', "ApiKey", Required = true, HelpText = "The API key for the server")]
		public string ApiKey { get; set; }

		[Option('s', "Source", Required = true, HelpText = "Specifies the server URL")]
		public string SourceUrl { get; set; }

	}

	public class PushNuGetPackagesCommand : Command<PushNuGetPkgsOptions>
	{
		private EnvironmentSettings _environmentSettings;
		private IPackageFinder _packageFinder; 
		private INuGetManager _nugetManager;

		public PushNuGetPackagesCommand(EnvironmentSettings environmentSettings, IPackageFinder packageFinder, 
			INuGetManager nugetManager) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			packageFinder.CheckArgumentNull(nameof(packageFinder));
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_environmentSettings = environmentSettings;
			_packageFinder = packageFinder;
			_nugetManager = nugetManager;
		}

		public override int Execute(PushNuGetPkgsOptions options) {
			try {
				IDictionary<string, PackageInfo> packagesInfo = _packageFinder.Find(options.PackagesPath);
				_nugetManager.CreateNuspecFiles(options.PackagesPath, packagesInfo, options.Version);
				IEnumerable<string> nuspecFilesPaths = _nugetManager.GetNuspecFilesPaths(options.PackagesPath);
				_nugetManager.Pack(nuspecFilesPaths, options.PackagesPath);
				IEnumerable<string> nupkgFilesPaths = _nugetManager.GetNupkgFilesPaths(options.PackagesPath);
				_nugetManager.Push(nupkgFilesPaths, options.ApiKey, options.SourceUrl);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
