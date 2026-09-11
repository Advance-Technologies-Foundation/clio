using System;
using System.Collections.Generic;
using System.Xml;
using Clio.Common;
using Clio.Workspaces;

namespace Clio.Workspace;

/// <summary>Creates and updates XML solution project registrations.</summary>
public interface ISolutionCreator{
	#region Methods: Public
	/// <summary>Adds missing projects, preserving existing registrations and solution folders.</summary>
	/// <param name="solutionPath">Path to the XML solution to create or update.</param>
	/// <param name="solutionProjects">Projects with paths relative to the solution directory.</param>
	/// <exception cref="XmlException">The existing file is not a valid XML solution.</exception>
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
	
	/// <inheritdoc />
	public void AddProjectToSolution(string solutionPath, IEnumerable<SolutionProject> solutionProjects) {

		if (!_fileSystem.ExistsFile(solutionPath)) {
			CreateNewMainSolution(solutionPath);
		}
		
		string slnxContent = _fileSystem.ReadAllText(solutionPath);
		XmlDocument doc = new();
		doc.LoadXml(slnxContent);

		XmlNode solutionNode = doc.SelectSingleNode("Solution");
		if (solutionNode == null) {
			throw new XmlException($"Solution file {solutionPath} does not contain a root <Solution> node. Repair the solution and rerun the command.");
		}
		foreach (SolutionProject sp in solutionProjects) {
			XmlElement projectNode = FindProjectNode(solutionNode, sp.Path);
			if (projectNode == null) {
				projectNode = doc.CreateElement("Project");
				projectNode.SetAttribute("Path", sp.Path);
				solutionNode.AppendChild(projectNode);
			}
			if (sp.ForceBuild && projectNode != null && projectNode.SelectSingleNode("Build") == null) {
				projectNode.AppendChild(doc.CreateElement("Build"));
			}
		}

		doc.Save(solutionPath);
	}

	private static XmlElement FindProjectNode(XmlNode solutionNode, string path) {
		XmlNodeList existingProjects = solutionNode.SelectNodes(".//Project");
		if (existingProjects == null) {
			return null;
		}
		foreach (XmlNode existingProject in existingProjects) {
			if (existingProject is XmlElement element
				&& string.Equals(element.Attributes?["Path"]?.Value.Replace('\\', '/'), path.Replace('\\', '/'),
					OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
				return element;
			}
		}
		return null;
	}

	private void CreateNewMainSolution(string solutionPath) {
		string solutionContent = _templateProvider.GetTemplate("workspace/MainSolution.slnx");
		_fileSystem.WriteAllTextToFile(solutionPath, solutionContent);
		_logger.WriteInfo($"Created solution file {solutionPath}");
	}

	#endregion
}

#endregion
