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

		[Value(0, MetaName = "PackagePath", Required = true, HelpText = "Package path")]
		public string PackagePath { get; set; }

		[Option('d', "Dependencies", Required = false, HelpText = "Package dependencies", Default = null)]
		public string Dependencies { get; set; }

		[Option('n', "NupkgDirectory", Required = true, HelpText = "Nupkg package directory")]
		public string NupkgDirectory { get; set; }

	}

	public class PackNuGetPackageCommand : Command<PackNuGetPkgOptions>
	{
		private IPackageInfoProvider _packageInfoProvider; 
		private INuGetManager _nugetManager;

		private DependencyInfo ParseDependency(string dependencyDescription) {
			string[] dependencyItems = dependencyDescription
				.Split(new [] {':'}, StringSplitOptions.RemoveEmptyEntries);
			if (dependencyItems.Length != 2) {
				throw new ArgumentException($"Wrong format the dependency: '{dependencyDescription}'. " 
					+ "The format the dependency mast be: <NamePackage>:<VersionPackage>");
			}
			return new DependencyInfo(dependencyItems[0], dependencyItems[1]);
		}

		private DependencyInfo[] ParseDependencies(string dependenciesDescription) {
			if (string.IsNullOrWhiteSpace(dependenciesDescription)) {
				return null;
			}
			string[] dependencies = dependenciesDescription
				.Split(new [] {';'}, StringSplitOptions.RemoveEmptyEntries);
			if (dependencies.Length == 0) {
				return null;
			}
			return dependencies.Select(ParseDependency).ToArray();
		}

		private void CheckDependencies(IEnumerable<DependencyInfo> dependencies, PackageInfo packageInfo) {
			StringBuilder sb = null;
			foreach (DependencyInfo dependencyInfo in dependencies) {
				if (!packageInfo.Depends.Contains(dependencyInfo)) {
					if (sb == null) {
						sb = new StringBuilder();
						sb.Append("The following dependencies do not exist in the package descriptor:");
					}
					sb.Append($" {dependencyInfo.Name}:{dependencyInfo.PackageVersion};");
				}
			}
			if (sb != null) {
				throw new ArgumentException(sb.ToString());
			}
		}

		public PackNuGetPackageCommand(IPackageInfoProvider packageInfoProvider, INuGetManager nugetManager) {
			packageInfoProvider.CheckArgumentNull(nameof(packageInfoProvider));
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_packageInfoProvider = packageInfoProvider;
			_nugetManager = nugetManager;
		}

		public override int Execute(PackNuGetPkgOptions options) {
			try {
				IEnumerable<DependencyInfo> dependencies = options.Dependencies == null 
					? Enumerable.Empty<DependencyInfo>() 
					: ParseDependencies(options.Dependencies);
				PackageInfo packageInfo = _packageInfoProvider.GetPackageInfo(options.PackagePath);
				CheckDependencies(dependencies, packageInfo);
				string nuspecFileName = _nugetManager.GetNuspecFileName(packageInfo);
				string nuspecFilePath = Path.Combine(options.NupkgDirectory, nuspecFileName);
				_nugetManager.CreateNuspecFile(packageInfo, dependencies, nuspecFilePath);
				string nupkgFileName = _nugetManager.GetNupkgFileName(packageInfo);
				string nupkgFilePath = Path.Combine(options.NupkgDirectory, nupkgFileName); 
				_nugetManager.Pack(nuspecFilePath, nupkgFilePath);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

	}

}