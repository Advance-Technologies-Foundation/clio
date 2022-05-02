namespace Clio.Project.NuGet
{
	using System;

	#region Interface: ICreatioSdk

	public interface ICreatioSdk
	{

		#region Methods: Public

		Version FindSdkVersion(Version applicationVersion);

		#endregion

	}

	#endregion

}