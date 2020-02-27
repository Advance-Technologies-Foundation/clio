namespace Clio
{
	public interface IPackageInfoProvider
	{
		PackageInfo GetPackageInfo(string packagePath);
	}
}