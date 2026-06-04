using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Package;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class UiProjectCreatorTests {

	#region Constants: Private

	private const string ProjectName = "rss_reader";
	private const string PackageName = "UsrRssReader";
	private const string VendorPrefix = "usr";
	private const string RootPath = @"C:\ws";
	private const string JavaScriptSdkName = "Microsoft.VisualStudio.JavaScript.Sdk";
	private const string JavaScriptSdkVersion = "1.0.5581896";

	private const string EsprojTemplate =
		"<Project Sdk=\"Microsoft.VisualStudio.JavaScript.Sdk\">\n" +
		"  <PropertyGroup>\n" +
		"    <BuildOutputFolder>$(MSBuildProjectDirectory)\\<%distPath%></BuildOutputFolder>\n" +
		"    <!-- <%projectName%> -->\n" +
		"  </PropertyGroup>\n" +
		"</Project>";

	#endregion

	#region Fields: Private

	private IWorkspacePackageProvisioner _packageProvisioner;
	private IWorkspacePathBuilder _workspacePathBuilder;
	private ITemplateProvider _templateProvider;
	private IFileSystem _fileSystem;
	private ISolutionCreator _solutionCreator;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private Dictionary<string, string> _writtenFiles;
	private UiProjectCreator _creator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_writtenFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.IsWorkspace.Returns(true);
		_workspacePathBuilder.RootPath.Returns(RootPath);
		_workspacePathBuilder.PackagesFolderPath.Returns(Path.Combine(RootPath, "packages"));
		_workspacePathBuilder.ProjectsFolderPath.Returns(Path.Combine(RootPath, "projects"));
		_workspacePathBuilder.MainSolutionFolderPath.Returns(RootPath);
		_workspacePathBuilder.MainSolutionPath.Returns(Path.Combine(RootPath, "MainSolution.slnx"));

		_templateProvider = Substitute.For<ITemplateProvider>();
		_templateProvider.GetTemplate("esproj").Returns(EsprojTemplate);

		_fileSystem = Substitute.For<IFileSystem>();
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(Array.Empty<string>());
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);
		_fileSystem.WhenForAnyArgs(fs => fs.WriteAllTextToFile(default, default))
			.Do(ci => _writtenFiles[ci.ArgAt<string>(0)] = ci.ArgAt<string>(1));

		_solutionCreator = Substitute.For<ISolutionCreator>();

		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_workingDirectoriesProvider.CurrentDirectory.Returns(RootPath);

		_packageProvisioner = Substitute.For<IWorkspacePackageProvisioner>();

		_creator = new UiProjectCreator(
			_packageProvisioner,
			_workspacePathBuilder,
			_templateProvider,
			_workingDirectoriesProvider,
			_fileSystem,
			_solutionCreator);
	}

	[Test]
	[Description("Writes a version-less .esproj wrapper next to the Angular project with the bundle " +
		"output folder pointing into the Creatio package.")]
	public void Create_Should_Write_Esproj_File() {
		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		string esprojPath = Path.Combine(RootPath, "projects", ProjectName, $"{ProjectName}.esproj");
		_writtenFiles.Should().ContainKey(esprojPath);
		string esproj = _writtenFiles[esprojPath];
		esproj.Should().Contain("Sdk=\"Microsoft.VisualStudio.JavaScript.Sdk\"")
			.And.NotContain("<%projectName%>")
			.And.NotContain("<%distPath%>");
		// Forward slashes are used on every platform (POSIX-native, normalized by MSBuild on Windows).
		esproj.Should().Contain($"../../packages/{PackageName}/Files/src/js/{ProjectName}");
	}

	[Test]
	[Description("Creates a repo-root global.json pinning the JavaScript SDK version when none exists.")]
	public void Create_Should_Pin_JavaScript_Sdk_In_New_GlobalJson() {
		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		string globalJsonPath = Path.Combine(RootPath, "global.json");
		_writtenFiles.Should().ContainKey(globalJsonPath);
		JsonObject root = JsonNode.Parse(_writtenFiles[globalJsonPath]).AsObject();
		root["msbuild-sdks"][JavaScriptSdkName].GetValue<string>().Should().Be(JavaScriptSdkVersion);
	}

	[Test]
	[Description("Merges the msbuild-sdks key into an existing global.json without clobbering the sdk node.")]
	public void Create_Should_Merge_Into_Existing_GlobalJson() {
		// Arrange
		string globalJsonPath = Path.Combine(RootPath, "global.json");
		_fileSystem.ExistsFile(globalJsonPath).Returns(true);
		_fileSystem.ReadAllText(globalJsonPath).Returns(
			"{ \"sdk\": { \"version\": \"10.0.100\", \"rollForward\": \"latestMinor\" } }");

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		JsonObject root = JsonNode.Parse(_writtenFiles[globalJsonPath]).AsObject();
		root["sdk"]["version"].GetValue<string>().Should().Be("10.0.100", "because the existing .NET SDK pin must be preserved");
		root["msbuild-sdks"][JavaScriptSdkName].GetValue<string>().Should().Be(JavaScriptSdkVersion);
	}

	[Test]
	[Description("Adds the .esproj to MainSolution.slnx with ForceBuild so a <Build /> element is emitted.")]
	public void Create_Should_Add_Esproj_To_MainSolution_With_ForceBuild() {
		// Arrange
		List<SolutionProject> captured = [];
		_solutionCreator.WhenForAnyArgs(sc => sc.AddProjectToSolution(default, default))
			.Do(ci => captured.AddRange(ci.ArgAt<IEnumerable<SolutionProject>>(1)));

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		_solutionCreator.Received(1)
			.AddProjectToSolution(Path.Combine(RootPath, "MainSolution.slnx"), Arg.Any<IEnumerable<SolutionProject>>());
		captured.Should().ContainSingle();
		captured[0].ForceBuild.Should().BeTrue();
		captured[0].Path.Should().Be(Path.Combine("projects", ProjectName, $"{ProjectName}.esproj"));
	}

	[Test]
	[Description("Outside a workspace there is no main solution, so no esproj integration is performed.")]
	public void Create_Should_Skip_Esproj_Integration_Outside_Workspace() {
		// Arrange
		_workspacePathBuilder.IsWorkspace.Returns(false);

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		_writtenFiles.Keys.Should().NotContain(k => k.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase));
		_writtenFiles.Should().NotContainKey(Path.Combine(RootPath, "global.json"));
		_solutionCreator.DidNotReceiveWithAnyArgs().AddProjectToSolution(default, default);
	}

	#endregion

}
