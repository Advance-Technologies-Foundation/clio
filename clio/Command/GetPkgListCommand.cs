using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("get-pkg-list", Aliases = new[] { "packages" }, HelpText = "Get environments packages")]
public class PkgListOptions : EnvironmentNameOptions
{
    [Option('f', "Filter", Required = false, HelpText = "Contains name filter",
        Default = null)]
    public string SearchPattern { get; set; } = string.Empty;

    [Option('j', "Json", Required = false, Default = false, HelpText = "Returns response in json format")]
    public bool? Json { get; set; }
}

public class GetPkgListCommand : Command<PkgListOptions>
{
    private readonly IApplicationPackageListProvider _applicationPackageListProvider;
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IJsonResponseFormater _jsonResponseFormater;
    private readonly ILogger _logger;

    public GetPkgListCommand(
        EnvironmentSettings environmentSettings,
        IApplicationPackageListProvider applicationPackageListProvider,
        IJsonResponseFormater jsonResponseFormater,
        ILogger logger)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
        jsonResponseFormater.CheckArgumentNull(nameof(jsonResponseFormater));
        _environmentSettings = environmentSettings;
        _applicationPackageListProvider = applicationPackageListProvider;
        _jsonResponseFormater = jsonResponseFormater;
        _logger = logger;
    }

    private static string[] CreateRow(string nameColumn, string versionColumn, string maintainerColumn) =>
        new[] { nameColumn, versionColumn, maintainerColumn };

    private static string[] CreateEmptyRow() => CreateRow(string.Empty, string.Empty, string.Empty);

    private void PrintPackageList(IEnumerable<PackageInfo> packages)
    {
        IList<string[]> table = new List<string[]> { CreateRow("Name", "Version", "Maintainer"), CreateEmptyRow() };
        foreach (PackageInfo pkg in packages)
        {
            table.Add(CreateRow(pkg.Descriptor.Name, pkg.Descriptor.PackageVersion, pkg.Descriptor.Maintainer));
        }

        _logger.WriteLine();
        _logger.WriteInfo(TextUtilities.ConvertTableToString(table));
        _logger.WriteLine();
    }

    private static IEnumerable<PackageInfo> FilterPackages(
        IEnumerable<PackageInfo> packages,
        string searchPattern) =>
        packages
            .Where(p => p.Descriptor.Name.ToLower().Contains(searchPattern.ToLower()))
            .OrderBy(p => p.Descriptor.Name);

    private void PrintPackageList(PkgListOptions options, IEnumerable<PackageInfo> filteredPackages)
    {
        if (options.Json.HasValue && options.Json.Value)
        {
            _logger.WriteLine(_jsonResponseFormater.Format(filteredPackages));
        }
        else
        {
            if (filteredPackages.Any())
            {
                PrintPackageList(filteredPackages);
            }

            _logger.WriteLine();
            _logger.WriteInfo($"Find {filteredPackages.Count()} packages in {_environmentSettings.Uri}");
        }
    }

    private void PrintError(PkgListOptions options, Exception e)
    {
        if (options.Json.HasValue && options.Json.Value)
        {
            _logger.WriteInfo(_jsonResponseFormater.Format(e));
        }
        else
        {
            _logger.WriteInfo(e.ToString());
        }
    }

    public override int Execute(PkgListOptions options)
    {
        try
        {
            IEnumerable<PackageInfo> packages = _applicationPackageListProvider.GetPackages();
            IEnumerable<PackageInfo> filteredPackages = FilterPackages(packages, options.SearchPattern);
            PrintPackageList(options, filteredPackages);
            return 0;
        }
        catch (Exception e)
        {
            PrintError(options, e);
            return 1;
        }
    }
}
