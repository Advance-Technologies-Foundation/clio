using System;
using System.Collections.Generic;
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

		_creator = new UiProjectCreator(
			new EnvironmentSettings(),
			Substitute.For<IWorkspace>(),
			Substitute.For<IApplicationPackageListProvider>(),
			Substitute.For<IPackageCreator>(),
			Substitute.For<IPackageDownloader>(),
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
	[Description("Preserves the complete existing package tree while scaffolding a UI project that targets it.")]
	public void Create_ShouldPreserveExistingPackageContent_WhenPackageIsReused() {
		// Arrange
		string packagePath = Path.Combine(_tempDir, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		string descriptorContent =
			$"{{\"Descriptor\":{{\"Name\":\"{PackageName}\",\"UId\":\"{Guid.NewGuid()}\",\"PackageVersion\":\"1.2.3\"}}}}";
		Dictionary<string, byte[]> originalPackageFiles = new() {
			[descriptorPath] = System.Text.Encoding.UTF8.GetBytes(descriptorContent),
			[Path.Combine(packagePath, "Schemas", "ExistingSchema", "schema.json")] =
				System.Text.Encoding.UTF8.GetBytes("{\"existing\":true}"),
			[Path.Combine(packagePath, "Data", "ExistingData", "data.json")] =
				System.Text.Encoding.UTF8.GetBytes("{\"rows\":[1]}"),
			[Path.Combine(packagePath, "DataBinding", "ExistingBinding", "binding.json")] =
				System.Text.Encoding.UTF8.GetBytes("{\"bound\":true}"),
			[Path.Combine(packagePath, "Files", "existing.bin")] = [0x00, 0xFF, 0x10, 0x80]
		};
		foreach ((string path, byte[] content) in originalPackageFiles) {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllBytes(path, content);
		}

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, true, string.Empty, _ => false);

		// Assert
		string[] actualPackageFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);
		actualPackageFiles.Should().BeEquivalentTo(originalPackageFiles.Keys,
			because: "reusing a package must neither add nor remove package content");
		foreach ((string path, byte[] content) in originalPackageFiles) {
			File.ReadAllBytes(path).Should().Equal(content,
				because: "every existing package file must remain byte-for-byte equivalent");
		}
		File.Exists(Path.Combine(_tempDir, "projects", ProjectName, "package.json")).Should().BeTrue(
			because: "the Angular project should still be scaffolded when its host package already exists");
		string rspackConfig = File.ReadAllText(Path.Combine(_tempDir, "projects", ProjectName, "rspack.config.js"));
		rspackConfig.Should().Contain($"const OUTPUT_PATH = '../../packages/{PackageName}/Files/src/js/{ProjectName}';",
			because: "the empty project must emit its bundle into the reused package");
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

	[TestCase(false)]
	[TestCase(true)]
	[Description("Substitutes the remote name and bundle path into rspack.config.js and ships no webpack configuration for every shipped UI template.")]
	public void Create_ShouldSubstituteTokensInRspackConfig_WhenTemplateIsScaffolded(bool isEmpty) {
		// Arrange
		string projectPath = Path.Combine(_tempDir, "projects", ProjectName);

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, isEmpty, string.Empty, _ => false);

		// Assert
		string rspackConfig = File.ReadAllText(Path.Combine(projectPath, "rspack.config.js"));
		rspackConfig.Should().NotContain("<%", because: "all template tokens in the Rspack configuration must be substituted");
		rspackConfig.Should().Contain($"const REMOTE_NAME = '{ProjectName}';",
			because: "the Module Federation container name must match the build folder name the host resolves");
		rspackConfig.Should().Contain($"const OUTPUT_PATH = '../../packages/{PackageName}/Files/src/js/{ProjectName}';",
			because: "the bundle must be emitted straight into the package file content");
		File.Exists(Path.Combine(projectPath, "webpack.config.js")).Should().BeFalse(
			because: "the template builds with Rspack, so no webpack configuration is shipped");
		File.Exists(Path.Combine(projectPath, "webpack.prod.config.js")).Should().BeFalse(
			because: "the template builds with Rspack, so no webpack configuration is shipped");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Runs Jest directly and initializes Angular's zone-based test environment in setup-jest.ts for every shipped UI template.")]
	public void Create_ShouldInitializeZoneTestEnvironmentForDirectJestRun_WhenTemplateIsScaffolded(bool isEmpty) {
		// Arrange
		string projectPath = Path.Combine(_tempDir, "projects", ProjectName);

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, isEmpty, string.Empty, _ => false);

		// Assert
		string setupContent = File.ReadAllText(Path.Combine(projectPath, "setup-jest.ts"));
		setupContent.ReplaceLineEndings("\n").Trim().Should().Be(
			"import { setupZoneTestEnv } from 'jest-preset-angular/setup-env/zone';\n\n" +
			"setupZoneTestEnv();",
			because: "without an Angular CLI Jest builder the setup file owns Angular test-environment initialization");

		string jestConfig = File.ReadAllText(Path.Combine(projectPath, "jest.config.ts"));
		jestConfig.ReplaceLineEndings("\n").Trim().Should().Be(
			"import type { Config } from 'jest';\n\n" +
			"export default {\n" +
			"  preset: 'jest-preset-angular',\n" +
			"  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],\n" +
			"} satisfies Config;",
			because: "the generated project should retain exactly one project-specific Jest setup extension point");

		JsonObject packageJson = JsonNode.Parse(File.ReadAllText(Path.Combine(projectPath, "package.json"))).AsObject();
		string testScript = packageJson["scripts"]?["test"]?.GetValue<string>();
		testScript.Should().Be("jest",
			because: "the Angular CLI Jest builder depends on the deprecated webpack builder, and a plain script keeps Jest's documented zero-spec exit result");

		JsonObject angularJson = JsonNode.Parse(File.ReadAllText(Path.Combine(projectPath, "angular.json"))).AsObject();
		JsonNode project = angularJson["projects"]?[ProjectName];
		project.Should().NotBeNull(because: "the angular.json project key must be substituted with the project name");
		project!["prefix"]?.GetValue<string>().Should().Be(VendorPrefix,
			because: "the devkit AI guides read the component selector prefix from angular.json");
		JsonObject targets = project["architect"]!.AsObject();
		foreach (string target in new[] { "build", "serve", "test" }) {
			targets.ContainsKey(target).Should().BeFalse(
				because: $"{target} runs through an npm script, so ng {target} must not pick a builder");
		}
		targets["ng-update"]?["options"]?["tsConfig"]?.GetValue<string>().Should().Be("tsconfig.app.json",
			because: "ng update migrations find the application sources through a target's tsConfig");
		targets["ng-update-test"]?["options"]?["tsConfig"]?.GetValue<string>().Should().Be("tsconfig.spec.json",
			because: "ng update migrations find the specs through a target whose name contains test");
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
