using System;
using System.IO;
using System.Xml;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using AbstractionsFileSystem = System.IO.Abstractions.FileSystem;

namespace Clio.Tests.Workspace;

[TestFixture]
[Category("Unit")]
[Property("Module", "Workspace")]
public class SolutionCreatorTests {

	#region Fields: Private

	private string _tempDir;
	private SolutionCreator _solutionCreator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_tempDir = Path.Combine(Path.GetTempPath(), "clio-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
		IFileSystem fileSystem = new FileSystem(new AbstractionsFileSystem());
		ITemplateProvider templateProvider = Substitute.For<ITemplateProvider>();
		templateProvider.GetTemplate("workspace/MainSolution.slnx").Returns(
			"<Solution>\n  <Configurations>\n    <BuildType Name=\"Debug\" />\n    <BuildType Name=\"dev-n8\" />\n  </Configurations>\n</Solution>");
		_solutionCreator = new SolutionCreator(fileSystem, Substitute.For<ILogger>(), templateProvider);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_tempDir)) {
			Directory.Delete(_tempDir, true);
		}
	}

	[Test]
	[Description("Emits an empty <Build /> child for a project flagged with ForceBuild so the JS SDK " +
		"project participates in every (custom) solution configuration.")]
	public void AddProjectToSolution_Should_Emit_Build_Element_When_ForceBuild() {
		// Arrange
		string solutionPath = Path.Combine(_tempDir, "MainSolution.slnx");

		// Act
		_solutionCreator.AddProjectToSolution(solutionPath,
			[new SolutionProject("rss_reader", @"projects\rss_reader\rss_reader.esproj") { ForceBuild = true }]);

		// Assert
		XmlNode projectNode = LoadProjectNode(solutionPath, @"projects\rss_reader\rss_reader.esproj");
		projectNode.Should().NotBeNull();
		projectNode.SelectSingleNode("Build").Should()
			.NotBeNull("because the esproj must be forced into every solution configuration");
	}

	[Test]
	[Description("A regular project (ForceBuild not set) is added without a <Build /> child.")]
	public void AddProjectToSolution_Should_Not_Emit_Build_Element_By_Default() {
		// Arrange
		string solutionPath = Path.Combine(_tempDir, "MainSolution.slnx");

		// Act
		_solutionCreator.AddProjectToSolution(solutionPath,
			[new SolutionProject("UsrPkg", @"packages\UsrPkg\Files\UsrPkg.csproj")]);

		// Assert
		XmlNode projectNode = LoadProjectNode(solutionPath, @"packages\UsrPkg\Files\UsrPkg.csproj");
		projectNode.Should().NotBeNull();
		projectNode.SelectSingleNode("Build").Should().BeNull();
	}

	[Test]
	[Description("Re-adding the same ForceBuild project does not duplicate the project node or the " +
		"<Build /> element (idempotent re-runs).")]
	public void AddProjectToSolution_Should_Be_Idempotent_For_ForceBuild_Project() {
		// Arrange
		string solutionPath = Path.Combine(_tempDir, "MainSolution.slnx");
		SolutionProject esproj =
			new("rss_reader", @"projects\rss_reader\rss_reader.esproj") { ForceBuild = true };

		// Act
		_solutionCreator.AddProjectToSolution(solutionPath, [esproj]);
		_solutionCreator.AddProjectToSolution(solutionPath, [esproj]);

		// Assert
		XmlDocument doc = new();
		doc.Load(solutionPath);
		XmlNodeList esprojNodes =
			doc.SelectNodes("Solution/Project[@Path='projects\\rss_reader\\rss_reader.esproj']");
		esprojNodes.Count.Should().Be(1, "because re-runs must not duplicate the project node");
		esprojNodes[0].SelectNodes("Build").Count.Should().Be(1, "because the <Build /> element must not be duplicated");
	}

	#endregion

	#region Methods: Private

	private static XmlNode LoadProjectNode(string solutionPath, string projectPath) {
		XmlDocument doc = new();
		doc.Load(solutionPath);
		return doc.SelectSingleNode($"Solution/Project[@Path='{projectPath}']");
	}

	#endregion

}
