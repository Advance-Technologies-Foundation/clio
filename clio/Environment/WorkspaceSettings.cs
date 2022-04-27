using System.Collections.Generic;

namespace Clio.Environment
{
	
	public class WorkspaceSettings
	{
		public string Name { get; set; }
		public string RootPath { get; set; }
		public Dictionary<string, EnvironmentOptions> Environments { get; set; }
		public string[] Packages { get; set; }
	}

}