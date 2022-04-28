namespace Clio.Workspace
{
	#region Interface: IWorkspaceRestorer

	public interface IWorkspaceRestorer
	{

		#region Methods: Public

		void Restore(string nugetCreatioSdkVersion);

		#endregion

	}

	#endregion

}