﻿using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet;

#region Class: AllVersionsNugetPackages

public class AllVersionsNugetPackages
{

    #region Fields: Private

    private readonly Lazy<IEnumerable<NugetPackage>> _orderingPackagesLazy;
    private readonly Lazy<NugetPackage> _lastVersionNugetPackageLazy;
    private readonly Lazy<NugetPackage> _lastStableVersionNugetPackageLazy;

    #endregion

    #region Constructors: Public

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

    #endregion

    #region Properties: Public

    public NugetPackage Last => _lastVersionNugetPackageLazy.Value;

    public string Name { get; }

    public IEnumerable<NugetPackage> OrderingPackages => _orderingPackagesLazy.Value;

    public IEnumerable<NugetPackage> Packages { get; }

    public NugetPackage Stable => _lastStableVersionNugetPackageLazy.Value;

    #endregion

    #region Methods: Private

    private NugetPackage GetLastStableVersionNugetPackage()
    {
        return OrderingPackages.FirstOrDefault(pkg => pkg.Version.IsStable);
    }

    private NugetPackage GetLastVersionNugetPackage()
    {
        return OrderingPackages.FirstOrDefault();
    }

    private IEnumerable<NugetPackage> GetOrderingPackages()
    {
        return Packages
               .Where(pkg => pkg.Name.Equals(Name, StringComparison.OrdinalIgnoreCase))
               .OrderByDescending(pkg => pkg.Version);
    }

    #endregion

}

#endregion
