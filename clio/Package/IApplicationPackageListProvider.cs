using System.Collections.Generic;
using Creatio.Client;

namespace Clio.Package
{
	public interface IApplicationPackageListProvider
	{
		IEnumerable<PackageInfo> GetPackages(string scriptData = "{}");

	}
}