using System.Linq;

namespace Clio.Workspaces
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
			// Build .slnx as XML
			var sortedProjects = solutionProjects.OrderBy(p => p.Path).ToList();
			var sb = new StringBuilder();
			sb.AppendLine("<Solution>");
			foreach (var sp in sortedProjects) {
				sb.AppendLine($"    <Project Path=\"{System.Security.SecurityElement.Escape(sp.Path)}\" />");
			}
			sb.AppendLine("</Solution>");
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