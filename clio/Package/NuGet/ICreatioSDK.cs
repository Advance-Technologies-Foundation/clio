namespace Clio.Project.NuGet
{
	using System;

	#region Interface: IPackageDownloader

	public interface ICreatioSDK
	{

		#region Methods: Public

		Version FindSdkVersion(Version applicationVersion);

		#endregion

	}

	#endregion

}