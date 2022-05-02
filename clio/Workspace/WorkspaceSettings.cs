namespace Clio.Workspace
{
	using System;
	using Newtonsoft.Json;
	using System.Collections.Generic;

	#region Class: WorkspaceSettings

	public class WorkspaceSettings
	{

		#region Properties: Public

		public string Name { get; set; }
		public Dictionary<string, WorkspaceEnvironment> Environments { get; set; } =
			new Dictionary<string, WorkspaceEnvironment>();
		public string[] Packages { get; set; } = {};
		public Version ApplicationVersion { get; set; }
		[JsonIgnore]
		public string RootPath { get; set; }

		#endregion

	}

	#endregion

}