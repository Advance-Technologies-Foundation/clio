namespace Clio;

public interface IPackageInfoProvider
{

    #region Methods: Public

    PackageInfo GetPackageInfo(string packagePath);

    #endregion

}
