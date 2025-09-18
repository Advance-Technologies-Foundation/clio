namespace Clio.Workspaces
{
	using System.Collections.Generic;
	using System.Text;
	using Clio.Common;

	#region Class: SolutionCreator

	public class SolutionCreator : ISolutionCreator
	{
		private readonly IFileSystem _fileSystem;
		private readonly ProjectGuidStore _guidStore;

		public SolutionCreator(IFileSystem fileSystem)
		{
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
			var guidStorePath = ".solution/project-guids.json";
			_guidStore = new ProjectGuidStore(_fileSystem, guidStorePath);
		}

		private string BuildSolutionContent(IEnumerable<SolutionProject> solutionProjects)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
			foreach (SolutionProject sp in solutionProjects)
			{
				var guid = _guidStore.GetOrCreateGuid(sp.Name);
				sb.AppendLine($"Project(\"{{{sp.Id}}}\") = \"{sp.Name}\", \"{sp.Path}\", \"{{{guid}}}\"");
				sb.AppendLine("EndProject");
			}
			sb.AppendLine("Global");
			sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
			sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
			sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
			sb.AppendLine("\tEndGlobalSection");
			sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
			foreach (SolutionProject sp in solutionProjects)
			{
				var guid = _guidStore.GetOrCreateGuid(sp.Name);
				sb.AppendLine($"\t\t\t{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
				sb.AppendLine($"\t\t\t{{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU");
				sb.AppendLine($"\t\t\t{{{guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU");
				sb.AppendLine($"\t\t\t{{{guid}}}.Release|Any CPU.Build.0 = Release|Any CPU");
			}
			sb.AppendLine("\tEndGlobalSection");
			sb.AppendLine("EndGlobal");
			return sb.ToString();
		}

		public void Create(string solutionPath, IEnumerable<SolutionProject> solutionProjects)
		{
			string solutionContent = BuildSolutionContent(solutionProjects);
			_fileSystem.WriteAllTextToFile(solutionPath, solutionContent);
		}
	}

	#endregion
}