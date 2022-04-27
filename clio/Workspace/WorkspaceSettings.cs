namespace Clio.Workspace
{
	using System.Collections.Generic;

	#region Class: WorkspaceSettings

	public class WorkspaceSettings
	{

		#region Properties: Public

		public string Name { get; set; }
		public string RootPath { get; set; }
		public Dictionary<string, EnvironmentOptions> Environments { get; set; }
		public string[] Packages { get; set; }

		#endregion

	}

	#endregion

}