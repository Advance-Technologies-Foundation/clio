using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Clio.Common;
using Clio.Workspaces;

namespace Clio.Workspace;

public interface ISolutionCreator{
	#region Methods: Public
	void AddProjectToSolution(string solutionPath, IEnumerable<SolutionProject> solutionProjects);

	#endregion
}

#region Class: SolutionCreator

public class SolutionCreator : ISolutionCreator{
	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly ITemplateProvider _templateProvider;

	#endregion

	#region Constructors: Public

	public SolutionCreator(IFileSystem fileSystem, ILogger logger, ITemplateProvider templateProvider) {
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		_fileSystem = fileSystem;
		_logger = logger;
		_templateProvider = templateProvider;
	}

	#endregion

	#region Methods: Public
	
	public void AddProjectToSolution(string solutionPath, IEnumerable<SolutionProject> solutionProjects) {

		if (!_fileSystem.ExistsFile(solutionPath)) {
			CreateNewMainSolution(solutionPath);
		}
		
		string slnxContent = _fileSystem.ReadAllText(solutionPath);
		XmlDocument doc = new();
		doc.LoadXml(slnxContent);

		XmlNode solutionNode = doc.SelectSingleNode("Solution");
		if (solutionNode == null) {
			_logger.WriteWarning($"[WARNING] Solution file {solutionPath} does not contain a root <Solution> node.");
			return;
		}
		XmlNodeList existingProjects = solutionNode.SelectNodes("Project");
		List<string> paths = [];
		if (existingProjects != null) {
			foreach (XmlNode existingProject in existingProjects) {
				if (existingProject.Attributes != null) {
					string path = existingProject.Attributes["Path"].Value;
					paths.Add(path);
				}
			}
		}
		foreach (SolutionProject sp in solutionProjects) {
			XmlElement projectNode;
			if (!paths.Contains(sp.Path)) {
				projectNode = doc.CreateElement("Project");
				projectNode.SetAttribute("Path", sp.Path);
				solutionNode.AppendChild(projectNode);
				paths.Add(sp.Path);
			} else {
				projectNode = FindProjectNode(solutionNode, sp.Path);
			}
			if (sp.ForceBuild && projectNode != null && projectNode.SelectSingleNode("Build") == null) {
				projectNode.AppendChild(doc.CreateElement("Build"));
			}
		}

		doc.Save(solutionPath);
	}

	private static XmlElement FindProjectNode(XmlNode solutionNode, string path) {
		XmlNodeList existingProjects = solutionNode.SelectNodes("Project");
		if (existingProjects == null) {
			return null;
		}
		foreach (XmlNode existingProject in existingProjects) {
			if (existingProject is XmlElement element
				&& element.Attributes?["Path"]?.Value == path) {
				return element;
			}
		}
		return null;
	}

	private void CreateNewMainSolution(string solutionPath) {
		string solutionContent = _templateProvider.GetTemplate("workspace/MainSolution.slnx");
		_fileSystem.WriteAllTextToFile(solutionPath, solutionContent);
		_logger.WriteWarning($"[WARNING] Solution file {solutionPath} does not exist, created new MainSolution.slnx");
	}

	#endregion
}

#endregion
