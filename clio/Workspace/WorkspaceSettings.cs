using System;
using System.IO;
using Newtonsoft.Json;

namespace Clio.Workspace
{
	using System.Collections.Generic;

	#region Class: WorkspaceSettings

	public class WorkspaceSettings
	{

		#region Properties: Public

		public string Name { get; set; }
		public Dictionary<string, WorkspaceEnvironment> Environments { get; set; } =
			new Dictionary<string, WorkspaceEnvironment>();
		public string[] Packages { get; set; } = {};
		public string ApplicationVersion { get; set; }
		[JsonIgnore]
		public string RootPath { get; set; }

		#endregion

	}

	#endregion

}