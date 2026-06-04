using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Xml;
using Clio.Common;
using Clio.Package;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using AbstractionsFileSystem = System.IO.Abstractions.FileSystem;

namespace Clio.Tests.Package;

/// <summary>
/// Exercises <see cref="UiProjectCreator"/> against the <b>real</b> shipped templates
/// (<c>tpl/esproj.tpl</c>, <c>tpl/ui-project</c>, <c>tpl/workspace/MainSolution.slnx</c>) and a real
/// filesystem in a temp workspace, then asserts the four esproj-integration artifacts on disk.
/// This is the test that catches a malformed template or a not-copied-to-output template.
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "Package")]
public class UiProjectCreatorIntegrationTests {

	#region Constants: Private

	private const string ProjectName = "rss_reader";
	private const string PackageName = "UsrRssReader";
	private const string VendorPrefix = "usr";

	#endregion

	#region Fields: Private

	private string _tempDir;
	private UiProjectCreator _creator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_tempDir = Path.Combine(Path.GetTempPath(), "clio-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);

		AbstractionsFileSystem abstractionsFileSystem = new();
		IFileSystem fileSystem = new FileSystem(abstractionsFileSystem);
		ILogger logger = Substitute.For<ILogger>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = new WorkingDirectoriesProvider(logger, abstractionsFileSystem);
		ITemplateProvider templateProvider = new TemplateProvider(workingDirectoriesProvider, fileSystem);
		ISolutionCreator solutionCreator = new SolutionCreator(fileSystem, logger, templateProvider);

		IWorkspacePathBuilder pathBuilder = Substitute.For<IWorkspacePathBuilder>();
		pathBuilder.IsWorkspace.Returns(true);
		pathBuilder.RootPath.Returns(_tempDir);
		pathBuilder.PackagesFolderPath.Returns(Path.Combine(_tempDir, "packages"));
		pathBuilder.ProjectsFolderPath.Returns(Path.Combine(_tempDir, "projects"));
		pathBuilder.MainSolutionFolderPath.Returns(_tempDir);
		pathBuilder.MainSolutionPath.Returns(Path.Combine(_tempDir, "MainSolution.slnx"));

		IWorkspacePackageProvisioner packageProvisioner = Substitute.For<IWorkspacePackageProvisioner>();
		_creator = new UiProjectCreator(
			packageProvisioner,
			pathBuilder,
			templateProvider,
			workingDirectoriesProvider,
			fileSystem,
			solutionCreator);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_tempDir)) {
			Directory.Delete(_tempDir, true);
		}
	}

	[Test]
	[Description("Generates all four esproj-integration artifacts from the real shipped templates.")]
	public void Create_Should_Produce_All_Esproj_Artifacts_From_Real_Templates() {
		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert — 1. esproj wrapper
		string esprojPath = Path.Combine(_tempDir, "projects", ProjectName, $"{ProjectName}.esproj");
		File.Exists(esprojPath).Should().BeTrue("the .esproj wrapper must be written next to package.json");
		string esprojContent = File.ReadAllText(esprojPath);
		esprojContent.Should().NotContain("<%", "all template tokens must be substituted");
		XmlDocument esprojDoc = new();
		Action loadEsproj = () => esprojDoc.LoadXml(esprojContent);
		loadEsproj.Should().NotThrow("the generated .esproj must be valid XML");
		esprojDoc.DocumentElement.GetAttribute("Sdk").Should()
			.Be("Microsoft.VisualStudio.JavaScript.Sdk", "the Sdk reference must be version-less (version lives in global.json)");
		// Forward slashes are used on every platform (POSIX-native, normalized by MSBuild on Windows).
		esprojContent.Should().Contain($"../../packages/{PackageName}/Files/src/js/{ProjectName}");

		// Assert — 2. global.json pins the JavaScript SDK
		string globalJsonPath = Path.Combine(_tempDir, "global.json");
		File.Exists(globalJsonPath).Should().BeTrue();
		JsonObject globalJson = JsonNode.Parse(File.ReadAllText(globalJsonPath)).AsObject();
		globalJson["msbuild-sdks"]["Microsoft.VisualStudio.JavaScript.Sdk"].GetValue<string>()
			.Should().Be("1.0.5581896");

		// Assert — 3. MainSolution.slnx has the esproj with a <Build /> element
		string solutionPath = Path.Combine(_tempDir, "MainSolution.slnx");
		File.Exists(solutionPath).Should().BeTrue();
		XmlDocument solutionDoc = new();
		solutionDoc.Load(solutionPath);
		// The .slnx stores the path produced by Path.GetRelativePath, which uses the OS separator
		// (backslash on Windows, forward slash on POSIX). Compare in C# rather than embedding a fixed
		// separator in an XPath predicate so the assertion holds on every platform.
		string expectedRelativePath = Path.Combine("projects", ProjectName, $"{ProjectName}.esproj");
		XmlNode esprojNode = FindProjectNode(solutionDoc, expectedRelativePath);
		esprojNode.Should().NotBeNull("the esproj must be registered in the main solution");
		esprojNode.SelectSingleNode("Build").Should()
			.NotBeNull("the empty <Build /> element forces participation in every solution configuration");

		// Assert — 4. package.json has a clean script with the substituted bundle path
		string packageJsonPath = Path.Combine(_tempDir, "projects", ProjectName, "package.json");
		File.Exists(packageJsonPath).Should().BeTrue();
		JsonObject packageJson = JsonNode.Parse(File.ReadAllText(packageJsonPath)).AsObject();
		string cleanScript = packageJson["scripts"]["clean"].GetValue<string>();
		cleanScript.Should().NotContain("<%distPath%>", "the dist path token must be substituted");
		cleanScript.Should().Contain("packages/UsrRssReader/Files/src/js/rss_reader");
	}

	#endregion

	#region Methods: Private

	private static XmlNode FindProjectNode(XmlDocument solutionDoc, string relativePath) {
		foreach (XmlNode project in solutionDoc.SelectNodes("Solution/Project")) {
			if (project.Attributes?["Path"]?.Value == relativePath) {
				return project;
			}
		}
		return null;
	}

	#endregion

}
