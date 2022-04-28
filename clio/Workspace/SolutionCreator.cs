namespace Clio.Workspace
{
	using System.Collections.Generic;
	using System.Text;
	using Clio.Common;

	#region Class: SolutionCreator

	public class SolutionCreator : ISolutionCreator
	{

		#region Fields: Private

		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public SolutionCreator(IFileSystem fileSystem) {
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		public string BuildSolutionContent(IEnumerable<SolutionProject> solutionProjects) {
			var sb = new StringBuilder();
			sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
			foreach (SolutionProject sp in solutionProjects) {
				sb.AppendLine($"Project(\"{{{sp.Id}}}\") = \"{sp.Name}\", \"{sp.Path}\", \"{{{sp.UId}}}\"");
				sb.AppendLine("EndProject");
			}
			sb.AppendLine("Global");
			sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
			sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
			sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
			sb.AppendLine("\tEndGlobalSection");
			sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
			foreach (SolutionProject sp in solutionProjects) {
				sb.AppendLine($"\t\t\t{{{sp.UId}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
				sb.AppendLine($"\t\t\t{{{sp.UId}}}.Debug|Any CPU.Build.0 = Debug|Any CPU");
				sb.AppendLine($"\t\t\t{{{sp.UId}}}.Release|Any CPU.ActiveCfg = Release|Any CPU");
				sb.AppendLine($"\t\t\t{{{sp.UId}}}.Release|Any CPU.Build.0 = Release|Any CPU");
			}
			sb.AppendLine("\tEndGlobalSection");
			sb.AppendLine("EndGlobal");
			return sb.ToString();
		}

		#endregion

		#region Methods: Public

		public void Create(string solutionPath, IEnumerable<SolutionProject> solutionProjects) {
			string solutionContent = BuildSolutionContent(solutionProjects);
			_fileSystem.WriteAllTextToFile(solutionPath, solutionContent);
		}

		#endregion

	}

	#endregion

}