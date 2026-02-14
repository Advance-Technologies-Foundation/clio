using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using Clio.Common;

namespace Clio.Workspaces;

#region Class: SolutionCreator

public class SolutionCreator : ISolutionCreator{
	#region Fields: Private

	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public SolutionCreator(IFileSystem fileSystem) {
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Public

	public string BuildSolutionContent(IEnumerable<SolutionProject> solutionProjects) {
		// Build .slnx as XML
		List<SolutionProject> sortedProjects = solutionProjects.OrderBy(p => p.Path).ToList();
		StringBuilder sb = new();
		sb.AppendLine("<Solution>");
		sb.AppendLine("    <Configurations>")
		  .AppendLine("        <BuildType Name=\"Debug\" />")
		  .AppendLine("        <BuildType Name=\"Release\" />")
		  .AppendLine("        <BuildType Name=\"dev-n8\" />")
		  .AppendLine("        <BuildType Name=\"dev-nf\" />")
		  .AppendLine("    </Configurations>");
			
		foreach (SolutionProject sp in sortedProjects) {
			sb.AppendLine($"    <Project Path=\"{SecurityElement.Escape(sp.Path)}\" />");
		}

		sb.AppendLine("</Solution>");
		return sb.ToString();
	}

	public void Create(string solutionPath, IEnumerable<SolutionProject> solutionProjects) {
		string solutionContent = BuildSolutionContent(solutionProjects);
		_fileSystem.WriteAllTextToFile(solutionPath, solutionContent);
	}

	#endregion
}

#endregion
