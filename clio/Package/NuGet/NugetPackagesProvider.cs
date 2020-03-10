using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{

	#region Class: NugetPackagesProvider

	public class NugetPackagesProvider : INugetPackagesProvider
	{

		#region Fields: Private

		private readonly INugetExecutor _nugetExecutor;

		#endregion

		#region Constructors: Public

		public NugetPackagesProvider(INugetExecutor nugetExecutor)
		{
			nugetExecutor.CheckArgumentNull(nameof(nugetExecutor));
			_nugetExecutor = nugetExecutor;
		}

		#endregion

		#region Methods: Private

		private string CorrectNugetSourceUrlForLinux(string nugetSourceUrl)
		{
			if (nugetSourceUrl.EndsWith('/'))
			{
				return nugetSourceUrl;
			}

			return $"{nugetSourceUrl}/";
		}

		private NugetPackageVersion ParseVersion(string versionDescription) {
			string[] versionItems = versionDescription
				.Trim(' ')
				.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
			if (versionItems.Length == 0 || versionItems.Length > 2) {
				throw new ArgumentException(
					$"Wrong format the nuget version: '{versionDescription}'. " + 
					"The format the nuget version mast be: <Version>[-<Suffix>]");
			}
			Version version = new Version(versionItems[0].Trim(' '));
			string suffix = versionItems.Length == 2
				? versionItems[1].Trim(' ') 
				: string.Empty;
			return new NugetPackageVersion(version, suffix);
		}

		private NugetPackage ParsePackage(string packageWithVersionDescription) {
			string[] packageItems = packageWithVersionDescription
				.Trim(' ')
				.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (packageItems.Length != 2) {
				throw new ArgumentException(
					$"Wrong format the package with version: '{packageWithVersionDescription}'. " + 
					"The format the package with version mast be: <NamePackage> <VersionPackage>");
			}
			string packageName = packageItems[0].Trim(' ');
			NugetPackageVersion packageVersion = ParseVersion(packageItems[1].Trim(' '));
			return new NugetPackage(packageName, packageVersion);
		}

		private IEnumerable<NugetPackage> ParsePackages(string packagesDescription) {
			return packagesDescription
				.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
				.Select(ParsePackage);
		}

		#endregion

		#region Methods: Public

		public IEnumerable<NugetPackage> GetPackages(string nugetSourceUrl) {
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			nugetSourceUrl = CorrectNugetSourceUrlForLinux(nugetSourceUrl);
			string getlistCommand = $"list -AllVersions  -PreRelease  -Source {nugetSourceUrl}";
			string packagesDescription = _nugetExecutor.Execute(getlistCommand, true);
			return ParsePackages(packagesDescription);
		}

		public NugetPackage GetLastVersionPackage(string packageName, string nugetSourceUrl) {
			packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			nugetSourceUrl = CorrectNugetSourceUrlForLinux(nugetSourceUrl);
			return GetPackages(nugetSourceUrl)
				.Where(pkg => pkg.Name == packageName)
				.OrderByDescending(pkg => pkg.Version)
				.FirstOrDefault();
		}

		#endregion

	}

	#endregion

}