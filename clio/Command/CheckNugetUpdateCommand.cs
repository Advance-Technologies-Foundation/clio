using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command
{

	[Verb("check-nuget-update", Aliases = new string[] { "check" }, HelpText = "Check for Creatio packages updates in NuGet")]
	public class CheckNugetUpdateOptions : EnvironmentOptions
	{

		[Option('s', "Source", Required = false, HelpText = "Specifies the server URL", 
			Default = "https://www.nuget.org/api/v2")]
		public string SourceUrl { get; set; }

	}

	public class CheckNugetUpdateCommand : Command<CheckNugetUpdateOptions>
	{
		private INuGetManager _nugetManager;

		public CheckNugetUpdateCommand(INuGetManager nugetManager) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_nugetManager = nugetManager;
		}

		private static string GetNameAndVersion(string name, PackageVersion version) {
			return $"{name} ({version})";
		}

		private static string GetPackageUpdateMessage(PackageForUpdate packageForUpdate) {
			LastVersionNugetPackages lastVersionNugetPackages = packageForUpdate.LastVersionNugetPackages;
			PackageInfo applPkg = packageForUpdate.ApplicationPackage;
			string pkgName = applPkg.Descriptor.Name;
			string message = $"   {GetNameAndVersion(pkgName, applPkg.Version)} --> " + 
				$"{GetNameAndVersion(pkgName, lastVersionNugetPackages.Last.Version)}";
			return lastVersionNugetPackages.LastIsStable || lastVersionNugetPackages.StableIsNotExists
				? message
				: $"{message}; Stable: {GetNameAndVersion(pkgName, lastVersionNugetPackages.Stable.Version)}";
		}

		private static void PrintPackagesForUpdate(IEnumerable<PackageForUpdate> packagesForUpdate) {
			Console.WriteLine("Packages for update:");
			foreach (PackageForUpdate packageForUpdate in packagesForUpdate) {
				Console.WriteLine(GetPackageUpdateMessage(packageForUpdate));
			}
		}

		private static void PrintResult(IEnumerable<PackageForUpdate> packagesForUpdate) {
			if (packagesForUpdate.Any()) {
				PrintPackagesForUpdate(packagesForUpdate);
			} else {
				Console.WriteLine("No update packages.");
			}
		}

		public override int Execute(CheckNugetUpdateOptions options) {
			try {
				IEnumerable<PackageForUpdate> packagesForUpdate = _nugetManager.GetPackagesForUpdate(options.SourceUrl);
				PrintResult(packagesForUpdate);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

	}
	
}