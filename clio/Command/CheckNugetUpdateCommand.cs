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
		private readonly ILogger _logger;

		public CheckNugetUpdateCommand(INuGetManager nugetManager, ILogger logger) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_nugetManager = nugetManager;
			_logger = logger;
		}

		private static string GetNameAndVersion(string name, PackageVersion version) {
			return $"{name} ({version})";
		}

		private string GetPackageUpdateMessage(PackageForUpdate packageForUpdate) {
			LastVersionNugetPackages lastVersionNugetPackages = packageForUpdate.LastVersionNugetPackages;
			PackageInfo applPkg = packageForUpdate.ApplicationPackage;
			string pkgName = applPkg.Descriptor.Name;
			string message = $"   {GetNameAndVersion(pkgName, applPkg.Version)} --> " + 
				$"{GetNameAndVersion(pkgName, lastVersionNugetPackages.Last.Version)}";
			return lastVersionNugetPackages.LastIsStable || lastVersionNugetPackages.StableIsNotExists
				? message
				: $"{message}; Stable: {GetNameAndVersion(pkgName, lastVersionNugetPackages.Stable.Version)}";
		}

		private void PrintPackagesForUpdate(IEnumerable<PackageForUpdate> packagesForUpdate) {
			_logger.WriteInfo("Packages for update:");
			foreach (PackageForUpdate packageForUpdate in packagesForUpdate) {
				_logger.WriteLine(GetPackageUpdateMessage(packageForUpdate));
			}
		}

		private void PrintResult(IEnumerable<PackageForUpdate> packagesForUpdate) {
			if (packagesForUpdate.Any()) {
				PrintPackagesForUpdate(packagesForUpdate);
			} else {
				_logger.WriteInfo("No update packages.");
			}
		}

		public override int Execute(CheckNugetUpdateOptions options) {
			try {
				IEnumerable<PackageForUpdate> packagesForUpdate = _nugetManager.GetPackagesForUpdate(options.SourceUrl);
				PrintResult(packagesForUpdate);
				_logger.WriteInfo("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}

	}
	
}