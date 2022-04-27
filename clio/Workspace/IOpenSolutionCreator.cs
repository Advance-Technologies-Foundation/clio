namespace Clio.Workspace
{

	#region Interface: IOpenSolutionCreator

	public interface IOpenSolutionCreator
	{

		#region Methods: Public

		void Create(string rootPath, string solutionName, string nugetFolderName, string nugetCreatioSdkVersion);

		#endregion

	}

	#endregion

}