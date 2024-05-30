namespace Clio.Project.NuGet
{
	using System;

	#region Interface: ICreatioSdk

	public interface ICreatioSdk
	{

		#region Properties: Public

		public Version LastVersion { get; }

		#endregion

		#region Methods: Public

		Version FindLatestSdkVersion(Version applicationVersion);

		#endregion

	}

	#endregion

}