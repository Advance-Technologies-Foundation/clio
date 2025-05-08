using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet;

public class AllVersionsNugetPackages
{
    private readonly Lazy<NugetPackage> _lastStableVersionNugetPackageLazy;
    private readonly Lazy<NugetPackage> _lastVersionNugetPackageLazy;
    private readonly Lazy<IEnumerable<NugetPackage>> _orderingPackagesLazy;

    public AllVersionsNugetPackages(string name, IEnumerable<NugetPackage> packages)
    {
        name.CheckArgumentNullOrWhiteSpace(nameof(name));
        packages.CheckArgumentNullOrEmptyCollection(nameof(packages));
        Name = name;
        Packages = packages;
        _orderingPackagesLazy = new Lazy<IEnumerable<NugetPackage>>(GetOrderingPackages);
        _lastVersionNugetPackageLazy = new Lazy<NugetPackage>(GetLastVersionNugetPackage);
        _lastStableVersionNugetPackageLazy = new Lazy<NugetPackage>(GetLastStableVersionNugetPackage);
    }

    public IEnumerable<NugetPackage> OrderingPackages => _orderingPackagesLazy.Value;

    public string Name { get; }

    public IEnumerable<NugetPackage> Packages { get; }

    public NugetPackage Last => _lastVersionNugetPackageLazy.Value;

    public NugetPackage Stable => _lastStableVersionNugetPackageLazy.Value;

    private IEnumerable<NugetPackage> GetOrderingPackages() =>
        Packages
            .Where(pkg => pkg.Name.Equals(Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pkg => pkg.Version);

    private NugetPackage GetLastVersionNugetPackage() => OrderingPackages.FirstOrDefault();

    private NugetPackage GetLastStableVersionNugetPackage() =>
        OrderingPackages.FirstOrDefault(pkg => pkg.Version.IsStable);
}
