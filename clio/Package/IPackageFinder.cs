namespace Clio
{
	using System.Collections.Generic;

	public interface IPackageFinder
	{
		IDictionary<string, PackageInfo> Find(string packagesPath);
	}
}