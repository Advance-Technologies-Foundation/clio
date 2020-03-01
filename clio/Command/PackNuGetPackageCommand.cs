using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Project.NuGet;

namespace Clio.Command
{
	[Verb("pack-nuget-pkg", Aliases = new string[] { "pack" }, HelpText = "Pack NuGet package")]
	public class PackNuGetPkgOptions : EnvironmentOptions
	{

		[Value(0, MetaName = "PackagePath", Required = true, HelpText = "Path of package folder")]
		public string PackagePath { get; set; }


		[Option('s', "SkipPdb", Required = false, HelpText = "Exclude pdb files from nuget package", Default = false)]
		public bool SkipPdb { get; set; }

		[Option('d', "Dependencies", Required = false, HelpText = "Package dependencies", Default = null)]
		public string Dependencies { get; set; }

		[Option('n', "NupkgDirectory", Required = false, HelpText = "Nupkg package directory", Default = null)]
		public string NupkgDirectory { get; set; }

	}

	public class PackNuGetPackageCommand : Command<PackNuGetPkgOptions>
	{
		private INuGetManager _nugetManager;

		private PackageDependency ParseDependency(string dependencyDescription) {
			string[] dependencyItems = dependencyDescription
				.Split(new [] {':'}, StringSplitOptions.RemoveEmptyEntries);
			if (dependencyItems.Length != 2) {
				throw new ArgumentException($"Wrong format the dependency: '{dependencyDescription}'. " 
					+ "The format the dependency mast be: <NamePackage>:<VersionPackage>");
			}
			return new PackageDependency(dependencyItems[0], dependencyItems[1]);
		}

		private PackageDependency[] ParseDependencies(string dependenciesDescription) {
			if (string.IsNullOrWhiteSpace(dependenciesDescription)) {
				return null;
			}
			IEnumerable<string> dependencies = StringParser.ParseArray(dependenciesDescription);
			if (!dependencies.Any()) {
				return null;
			}
			return dependencies.Select(ParseDependency).ToArray();
		}

		public PackNuGetPackageCommand(IPackageInfoProvider packageInfoProvider, INuGetManager nugetManager) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_nugetManager = nugetManager;
		}

		public override int Execute(PackNuGetPkgOptions options) {
			try {
				IEnumerable<PackageDependency> dependencies = options.Dependencies == null 
					? Enumerable.Empty<PackageDependency>() 
					: ParseDependencies(options.Dependencies);
				string result = _nugetManager.Pack(options.PackagePath, dependencies, options.SkipPdb, 
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