namespace Clio.Workspace
{
	using System;
	using Newtonsoft.Json;
	using System.Collections.Generic;

	#region Class: WorkspaceSettings

	public class WorkspaceSettings
	{

		#region Properties: Public

		public string[] Packages { get; set; } = {};
		public Version ApplicationVersion { get; set; }

		#endregion

	}

	#endregion

}