using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{

	#region Class: NugetPackagesProvider

	public class NugetPackagesProvider : INugetPackagesProvider
	{

		#region Fields: Private

		private static readonly Regex _nugetPackageRegex = 
			new Regex("^\\(Id='(?<Id>.*)',Version='(?<Version>.*)'\\)", RegexOptions.Compiled);

		#endregion

		#region Methods: Private

		private async Task<string> GetAllVersionsNugetPackagesXml(string nugetSourceUrl, string packageName) {
			var findPackagesByIdUrl = $"{nugetSourceUrl.TrimEnd('/')}/FindPackagesById()?id='{packageName}'";
			using var httpClient = new HttpClient();
			var response = await httpClient.GetAsync(findPackagesByIdUrl);
			var allVersionsNugetPackagesXml = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode) {
				throw new ArgumentException($"Wrong NuGet server URL: '{nugetSourceUrl}'");
			}
			return allVersionsNugetPackagesXml;
		}

		private static NugetPackage ConvertToNugetPackage(string xmlBase, string nugetPackageDescription) {
			string nugetPackageInfo = nugetPackageDescription.Replace($"{xmlBase}/Packages", String.Empty);
			Match nugetPackageMatch = _nugetPackageRegex.Match(nugetPackageInfo);
			if (!nugetPackageMatch.Success) {
				throw new InvalidOperationException($"Wrong NuGet package id: '{nugetPackageInfo}'");
			}
			return new NugetPackage(nugetPackageMatch.Groups["Id"].Value,
				PackageVersion.ParseVersion(nugetPackageMatch.Groups["Version"].Value));
		}

		private static IEnumerable<NugetPackage> DeserializeNugetPackagesXml(string nugetPackagesXml) {
			XElement rootNode = XElement.Parse(nugetPackagesXml);
			String xmlBase =  rootNode
				.Attributes()
				.FirstOrDefault(att => att.Name.LocalName == "base")?.Value
				.Trim('/');
			return rootNode
				.Elements()
				.Where(el => el.Name.LocalName == "entry")
				.Select(el => el.Elements().FirstOrDefault(e => e.Name.LocalName == "id"))
				.Select(el => ConvertToNugetPackage(xmlBase,(el?.Value)));
		}

		private LastVersionNugetPackages FindLastVersionNugetPackages(AllVersionsNugetPackages packages) {
			return packages != null 
				? new LastVersionNugetPackages(packages.Name, packages.Last, packages.Stable)
				: null;
		}

		private async Task<AllVersionsNugetPackages> FindAllVersionsNugetPackages(string packageName, 
			string nugetSourceUrl) {
			string allVersionsNugetPackagesXml = await GetAllVersionsNugetPackagesXml(nugetSourceUrl, packageName);
			IEnumerable<NugetPackage> packages = DeserializeNugetPackagesXml(allVersionsNugetPackagesXml);
			return packages.Count() != 0
				? new AllVersionsNugetPackages(packageName, packages)
				: null;
		}

		#endregion

		#region Methods: Public

		public IEnumerable<LastVersionNugetPackages> GetLastVersionPackages(IEnumerable<string> packagesNames, 
				string nugetSourceUrl) {
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			packagesNames.CheckArgumentNull(nameof(packagesNames));
			Task<AllVersionsNugetPackages>[] tasks = packagesNames
				.Select(pkgName => FindAllVersionsNugetPackages(pkgName, nugetSourceUrl))
				.ToArray();
			Task.WaitAll(tasks, Timeout.Infinite);
			return tasks
				.Select(t => FindLastVersionNugetPackages(t.Result))
				.Where(pkg => pkg != null);
		}

		public LastVersionNugetPackages GetLastVersionPackages(string packageName, string nugetSourceUrl) {
			packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			return GetLastVersionPackages(new string[] { packageName }, nugetSourceUrl)
				.FirstOrDefault();
		}

		#endregion

	}

	#endregion

}
