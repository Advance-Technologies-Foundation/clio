namespace Clio.Workspace
{
	using System;
	using Newtonsoft.Json;
	using System.Collections.Generic;

	#region Class: WorkspaceSettings

	public class WorkspaceSettings
	{

		#region Properties: Public

		public IList<string> Packages { get; set; } = new List<string>();
		public Version ApplicationVersion { get; set; }

		#endregion

	}

	#endregion

}