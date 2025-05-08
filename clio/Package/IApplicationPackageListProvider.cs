using System.Collections.Generic;

namespace Clio.Package;

#region Interface: IApplicationPackageListProvider

public interface IApplicationPackageListProvider
{

    #region Methods: Public

    IEnumerable<PackageInfo> GetPackages();

    IEnumerable<PackageInfo> GetPackages(string scriptData);

    #endregion

}

#endregion
