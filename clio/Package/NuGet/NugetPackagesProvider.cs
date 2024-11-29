using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Clio.Common;
using Newtonsoft.Json.Linq;

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
			var allVersionsNugetPackage = await GetPackageVersionsAsync(packageName, nugetSourceUrl);
			IEnumerable<NugetPackage> packages = allVersionsNugetPackage
				.Select(version => new NugetPackage(packageName, PackageVersion.ParseVersion(version)));
			return packages.Count() != 0
				? new AllVersionsNugetPackages(packageName, packages)
				: null;
		}

		public static async Task<List<string>> GetPackageVersionsAsync(string packageName, string nugetServer ) {
			nugetServer = String.IsNullOrEmpty(nugetServer) ? "https://api.nuget.org" : nugetServer;
			string nugetApiUrl = $"{nugetServer}/v3-flatcontainer/{packageName.ToLower()}/index.json";
			List<string> versions = new List<string>();

			using (HttpClient client = new HttpClient()) {
				try {
					// Send GET request to NuGet API
					HttpResponseMessage response = await client.GetAsync(nugetApiUrl);
					response.EnsureSuccessStatusCode();

					// Parse the response
					string jsonResponse = await response.Content.ReadAsStringAsync();
					JObject packageData = JObject.Parse(jsonResponse);

					// Extract versions
					var versionArray = packageData["versions"];
					if (versionArray != null) {
						foreach (var version in versionArray) {
							versions.Add(version.ToString());
						}
					} else {
						Console.WriteLine($"No versions found for package: {packageName}");
					}
				} catch (Exception ex) {
					Console.WriteLine($"Error fetching package versions: {ex.Message}");
				}
			}

			return versions;
		}

		#endregion

		#region Methods: Public

		public IEnumerable<LastVersionNugetPackages> GetLastVersionPackages(IEnumerable<string> packagesNames, 
				string nugetSourceUrl) {
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
			return GetLastVersionPackages(new string[] { packageName }, nugetSourceUrl)
				.FirstOrDefault();
		}

		#endregion

	}

	#endregion

}
