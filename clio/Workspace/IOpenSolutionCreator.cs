namespace Clio.Workspace
{
	using System;

	#region Interface: IOpenSolutionCreator

	public interface IOpenSolutionCreator
	{

		#region Methods: Public

		void Create(string rootPath, string solutionName, string nugetFolderName, Version nugetCreatioSdkVersion);

		#endregion

	}

	#endregion

}