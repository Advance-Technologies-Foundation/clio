using System.Collections.Generic;

namespace Clio.Common
{

	#region Interface: IPackageUtilities

	public interface IPackageUtilities
	{

		#region Methods: Public

		void CopyPackageElements(string sourcePath, string destinationPath, bool overwrite);

		#endregion

	}

	#endregion

}
