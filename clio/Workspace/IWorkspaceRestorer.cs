namespace Clio.Workspace
{
	using System;

	#region Interface: IWorkspaceRestorer

	public interface IWorkspaceRestorer
	{

		#region Methods: Public

		void Restore(Version nugetCreatioSdkVersion);

		#endregion

	}

	#endregion

}