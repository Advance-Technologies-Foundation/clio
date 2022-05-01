namespace Clio.Workspace
{
	using System;

	#region Interface: IOpenSolutionCreator

	public interface IOpenSolutionCreator
	{

		#region Methods: Public

		void Create(string rootPath, Version nugetCreatioSdkVersion);

		#endregion

	}

	#endregion

}