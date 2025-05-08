using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Newtonsoft.Json.Linq;

namespace Clio.Project.NuGet;

public partial class NugetPackagesProvider : INugetPackagesProvider
{
    private static readonly Regex _nugetPackageRegex =
        MyRegex();

    public IEnumerable<LastVersionNugetPackages> GetLastVersionPackages(
        IEnumerable<string> packagesNames,
        string nugetSourceUrl)
    {
        packagesNames.CheckArgumentNull(nameof(packagesNames));
        Task<AllVersionsNugetPackages>[] tasks = packagesNames
            .Select(pkgName => FindAllVersionsNugetPackages(pkgName, nugetSourceUrl))
            .ToArray();
        Task.WaitAll(tasks, Timeout.Infinite);
        return tasks
            .Select(t => FindLastVersionNugetPackages(t.Result))
            .Where(pkg => pkg != null);
    }

    public LastVersionNugetPackages GetLastVersionPackages(string packageName, string nugetSourceUrl)
    {
        packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
        return GetLastVersionPackages(new[] { packageName }, nugetSourceUrl)
            .FirstOrDefault();
    }

    private static NugetPackage ConvertToNugetPackage(string xmlBase, string nugetPackageDescription)
    {
        string nugetPackageInfo = nugetPackageDescription.Replace($"{xmlBase}/Packages", string.Empty);
        Match nugetPackageMatch = _nugetPackageRegex.Match(nugetPackageInfo);
        if (!nugetPackageMatch.Success)
        {
            throw new InvalidOperationException($"Wrong NuGet package id: '{nugetPackageInfo}'");
        }

        return new NugetPackage(
            nugetPackageMatch.Groups["Id"].Value,
            PackageVersion.ParseVersion(nugetPackageMatch.Groups["Version"].Value));
    }

    private LastVersionNugetPackages FindLastVersionNugetPackages(AllVersionsNugetPackages packages) =>
        packages != null
            ? new LastVersionNugetPackages(packages.Name, packages.Last, packages.Stable)
            : null;

    private async Task<AllVersionsNugetPackages> FindAllVersionsNugetPackages(
        string packageName,
        string nugetSourceUrl)
    {
        List<string> allVersionsNugetPackage = await GetPackageVersionsAsync(packageName, nugetSourceUrl);
        IEnumerable<NugetPackage> packages = allVersionsNugetPackage
            .Select(version => new NugetPackage(packageName, PackageVersion.ParseVersion(version)));
        return packages.Count() != 0
            ? new AllVersionsNugetPackages(packageName, packages)
            : null;
    }

    public static async Task<List<string>> GetPackageVersionsAsync(string packageName, string nugetServer)
    {
        nugetServer = string.IsNullOrEmpty(nugetServer) ? "https://api.nuget.org" : nugetServer;
        string nugetApiUrl = $"{nugetServer}/v3-flatcontainer/{packageName.ToLower()}/index.json";
        List<string> versions = [];

        using (HttpClient client = new())
        {
            try
            {
                // Send GET request to NuGet API
                HttpResponseMessage response = await client.GetAsync(nugetApiUrl);
                response.EnsureSuccessStatusCode();

                // Parse the response
                string jsonResponse = await response.Content.ReadAsStringAsync();
                JObject packageData = JObject.Parse(jsonResponse);

                // Extract versions
                JToken? versionArray = packageData["versions"];
                if (versionArray != null)
                {
                    foreach (JToken version in versionArray)
                    {
                        versions.Add(version.ToString());
                    }
                }
                else
                {
                    Console.WriteLine($"No versions found for package: {packageName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching package versions: {ex.Message}");
            }
        }

        return versions;
    }

    [GeneratedRegex("^\\(Id='(?<Id>.*)',Version='(?<Version>.*)'\\)", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
