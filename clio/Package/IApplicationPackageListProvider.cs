using System.Collections.Generic;

namespace Clio.Package;

public interface IApplicationPackageListProvider
{
    IEnumerable<PackageInfo> GetPackages();

    IEnumerable<PackageInfo> GetPackages(string scriptData);
}
