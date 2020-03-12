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
			PackageVersion packageVersion = PackageVersion.ParseVersion(packageItems[1].Trim(' '));
			return new NugetPackage(packageName, packageVersion);
		}

		private IEnumerable<NugetPackage> ParsePackages(string packagesDescription) {
			return packagesDescription
				.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
				.Select(ParsePackage);
		}

		private NugetPackage GetLastVersionNugetPackage(string packageName, IEnumerable<NugetPackage> nugetPackages) {
			return nugetPackages
				.Where(pkg => pkg.Name == packageName)
				.OrderByDescending(pkg => pkg.Version)
				.FirstOrDefault();
		}

		private NugetPackage GetLastStableVersionNugetPackage(string packageName, 
				IEnumerable<NugetPackage> nugetPackages) {
			return nugetPackages
				.Where(pkg => pkg.Name == packageName)
				.OrderByDescending(pkg => pkg.Version)
				.FirstOrDefault(pkg => pkg.Version.IsStable);
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

		public LastVersionNugetPackages GetLastVersionPackages(string packageName, IEnumerable<NugetPackage> nugetPackages) {
			packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
			nugetPackages.CheckArgumentNull(nameof(nugetPackages));
			NugetPackage last = GetLastVersionNugetPackage(packageName,  nugetPackages);
			NugetPackage stable = GetLastStableVersionNugetPackage(packageName,  nugetPackages);
			return last == null ? null : new LastVersionNugetPackages(last, stable);
		}

		public LastVersionNugetPackages GetLastVersionPackages(string packageName, string nugetSourceUrl) {
			packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			nugetSourceUrl = CorrectNugetSourceUrlForLinux(nugetSourceUrl);
			IEnumerable<NugetPackage> nugetPackages = GetPackages(nugetSourceUrl);
			return GetLastVersionPackages(packageName, nugetPackages);
		}

		#endregion

	}

	#endregion

}