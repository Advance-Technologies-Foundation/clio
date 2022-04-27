namespace Clio.Project
{

	#region Interface: IOpenSolutionCreator

	public interface IOpenSolutionCreator
	{

		#region Methods: Public

		void Create(string solutionName, string nugetFolderName, string nugetCreatioSdkVersion);

		#endregion

	}

	#endregion

}