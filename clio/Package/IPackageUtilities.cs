using System.Collections.Generic;

namespace Clio.Common
{

	#region Interface: IPackageUtilities

	public interface IPackageUtilities
	{

		#region Methods: Public

		void CopyPackageElements(string sourcePath, string destinationPath, bool overwrite);
		string GetPackageContentFolderPath(string repositoryPackageFolderPath);
		string GetPackageContentFolderPath(string repositoryFolderPath, string packageName);

		#endregion

	}

	#endregion

}
