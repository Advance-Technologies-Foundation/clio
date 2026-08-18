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

	private IWorkspacePathBuilder _workspacePathBuilder;
	private ITemplateProvider _templateProvider;
	private IFileSystem _fileSystem;
	private ISolutionCreator _solutionCreator;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private IPackageCreator _packageCreator;
	private IPackageDownloader _packageDownloader;
	private IApplicationPackageListProvider _applicationPackageListProvider;
	private IWorkspace _workspace;
	private System.IO.Abstractions.IDirectoryInfo _stagingDirectory;
	private System.IO.Abstractions.IFileInfo _descriptorFileInfo;
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
		_stagingDirectory = Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		_fileSystem.GetDirectoryInfo(Arg.Any<string>()).Returns(_stagingDirectory);
		_descriptorFileInfo = Substitute.For<System.IO.Abstractions.IFileInfo>();
		_fileSystem.GetFilesInfos(Arg.Any<string>()).Returns(_descriptorFileInfo);
		_fileSystem.WhenForAnyArgs(fs => fs.WriteAllTextToFile(default, default))
			.Do(ci => _writtenFiles[ci.ArgAt<string>(0)] = ci.ArgAt<string>(1));

		_solutionCreator = Substitute.For<ISolutionCreator>();

		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_workingDirectoriesProvider.CurrentDirectory.Returns(RootPath);
		_packageCreator = Substitute.For<IPackageCreator>();
		_packageDownloader = Substitute.For<IPackageDownloader>();
		_applicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_workspace = Substitute.For<IWorkspace>();

		_creator = new UiProjectCreator(
			new EnvironmentSettings(),
			_workspace,
			_applicationPackageListProvider,
			_packageCreator,
			_packageDownloader,
			_workspacePathBuilder,
			_templateProvider,
			_workingDirectoriesProvider,
			_fileSystem,
			_solutionCreator);
	}

	[Test]
	[Description("Creates the host package when no local package directory exists.")]
	public void Create_ShouldCreateMissingPackage_WhenLocalPackageDoesNotExist() {
		// Arrange

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		_packageCreator.Received(1).Create(Path.Combine(RootPath, "packages"), PackageName);
	}

	[Test]
	[Description("Reuses a valid existing local package without recreating or downloading it.")]
	public void Create_ShouldReuseValidExistingPackage_WhenPackageDirectoryExists() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_fileSystem.OpenReadStream(descriptorPath).Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
			$"{{\"Descriptor\":{{\"Name\":\"{PackageName}\",\"UId\":\"{Guid.NewGuid()}\"}}}}")));

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_packageDownloader.DidNotReceiveWithAnyArgs().DownloadPackage(default, default, default);
		_applicationPackageListProvider.DidNotReceive().GetPackages();
		_workspace.Received(1).AddPackageIfNeeded(PackageName);
		_templateProvider.Received(1).CopyTemplateFolder(
			"ui-project", Arg.Is<string>(path => path.StartsWith(Path.Combine(RootPath, "projects"))
				&& path.EndsWith(".tmp")), string.Empty, string.Empty);
		_stagingDirectory.Received(1).MoveTo(Path.Combine(RootPath, "projects", ProjectName));
	}

	[Test]
	[Description("Reuses a valid existing package whose descriptor begins with a UTF-8 byte-order mark.")]
	public void Create_ShouldReuseValidExistingPackage_WhenDescriptorHasUtf8Bom() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		byte[] json = System.Text.Encoding.UTF8.GetBytes(
			$"{{\"Descriptor\":{{\"Name\":\"{PackageName}\",\"UId\":\"{Guid.NewGuid()}\"}}}}");
		byte[] descriptor = new byte[json.Length + 3];
		descriptor[0] = 0xEF;
		descriptor[1] = 0xBB;
		descriptor[2] = 0xBF;
		Buffer.BlockCopy(json, 0, descriptor, 3, json.Length);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_fileSystem.OpenReadStream(descriptorPath).Returns(new MemoryStream(descriptor));

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_workspace.Received(1).AddPackageIfNeeded(PackageName);
	}

	[Test]
	[Description("Prefers an exact package directory name when a case-insensitive duplicate is enumerated first.")]
	public void Create_ShouldReuseExactPackageDirectory_WhenCaseInsensitiveDuplicateExists() {
		// Arrange
		string packagesPath = Path.Combine(RootPath, "packages");
		string packagePath = Path.Combine(packagesPath, PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		_fileSystem.ExistsDirectory(packagesPath).Returns(true);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.GetDirectories(packagesPath).Returns([
			Path.Combine(packagesPath, PackageName.ToLowerInvariant()),
			packagePath
		]);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_fileSystem.OpenReadStream(descriptorPath).Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
			$"{{\"Descriptor\":{{\"Name\":\"{PackageName}\",\"UId\":\"{Guid.NewGuid()}\"}}}}")));

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_workspace.Received(1).AddPackageIfNeeded(PackageName);
	}

	[Test]
	[Description("Reuses a valid package exposed through the supported filesystem-mode package junction.")]
	public void Create_ShouldReuseValidExistingPackage_WhenPackageDirectoryIsJunction() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_stagingDirectory.Attributes.Returns(FileAttributes.ReparsePoint);
		_fileSystem.OpenReadStream(descriptorPath).Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
			$"{{\"Descriptor\":{{\"Name\":\"{PackageName}\",\"UId\":\"{Guid.NewGuid()}\"}}}}")));

		// Act
		_creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_workspace.Received(1).AddPackageIfNeeded(PackageName);
	}

	[Test]
	[Description("Rejects an existing package directory without a Creatio package descriptor.")]
	public void Create_ShouldRejectExistingDirectory_WhenDescriptorIsMissing() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an arbitrary directory must not be silently accepted as the bundle host")
			.WithMessage($"*{packagePath}*not a valid Creatio package*descriptor.json*missing*");
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_templateProvider.DidNotReceiveWithAnyArgs().CopyTemplateFolder(default, default, default, default);
	}

	[TestCase("not-json", "malformed")]
	[TestCase("{\"Descriptor\":null}", "does not contain a package descriptor")]
	[TestCase(
		"{\"Descriptor\":{\"Name\":\"OtherPackage\",\"UId\":\"11111111-1111-1111-1111-111111111111\"}}",
		"does not match")]
	[TestCase(
		"{\"Descriptor\":{\"Name\":\"UsrRssReader\",\"UId\":\"00000000-0000-0000-0000-000000000000\"}}",
		"UId is empty")]
	[TestCase(
		"{\"Descriptor\":{\"Name\":\"usrrssreader\",\"UId\":\"11111111-1111-1111-1111-111111111111\"}}",
		"does not match")]
	[Description("Rejects malformed or mismatched descriptors instead of treating their directories as packages.")]
	public void Create_ShouldRejectExistingPackage_WhenDescriptorIsInvalid(string descriptorContent,
		string expectedReason) {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_fileSystem.OpenReadStream(descriptorPath).Returns(
			new MemoryStream(System.Text.Encoding.UTF8.GetBytes(descriptorContent)));

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "only a descriptor that identifies the requested Creatio package is safe to reuse")
			.WithMessage($"*not a valid Creatio package*{expectedReason}*");
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_templateProvider.DidNotReceiveWithAnyArgs().CopyTemplateFolder(default, default, default, default);
	}

	[Test]
	[Description("Rejects a package path occupied by a file before attempting remote lookup or package creation.")]
	public void Create_ShouldRejectExistingPackage_WhenPackagePathIsFile() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		_fileSystem.ExistsFile(packagePath).Returns(true);

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an occupied package path is an invalid package rather than a missing package")
			.WithMessage($"*{packagePath}*not a valid Creatio package*package path is a file*");
		_applicationPackageListProvider.DidNotReceive().GetPackages();
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Rejects a differently cased package directory consistently on every supported filesystem.")]
	public void Create_ShouldRejectExistingPackage_WhenPackageDirectoryCasingDiffers() {
		// Arrange
		string packagesPath = Path.Combine(RootPath, "packages");
		string requestedPackageName = PackageName.ToLowerInvariant();
		_fileSystem.ExistsDirectory(packagesPath).Returns(true);
		_fileSystem.GetDirectories(packagesPath).Returns([Path.Combine(packagesPath, PackageName)]);

		// Act
		Action act = () => _creator.Create(ProjectName, requestedPackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "package identity and workspace registration must not vary with filesystem casing rules")
			.WithMessage($"*package directory name '{PackageName}' does not match the requested casing*");
		_applicationPackageListProvider.DidNotReceive().GetPackages();
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Rejects an oversized descriptor before loading it into the long-running MCP process.")]
	public void Create_ShouldRejectExistingPackage_WhenDescriptorExceedsSizeLimit() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_fileSystem.GetFileSize(descriptorPath).Returns(1024 * 1024 + 1);

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a package descriptor must be bounded before it is read into memory")
			.WithMessage("*package descriptor exceeds the 1048576-byte size limit*");
		_fileSystem.DidNotReceive().OpenReadStream(descriptorPath);
	}

	[Test]
	[Description("Rejects a descriptor that grows beyond the size limit after its initial size check.")]
	public void Create_ShouldRejectExistingPackage_WhenDescriptorStreamExceedsSizeLimit() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_fileSystem.GetFileSize(descriptorPath).Returns(1024);
		_fileSystem.OpenReadStream(descriptorPath).Returns(new MemoryStream(new byte[1024 * 1024 + 1]));

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the bounded stream read must defend against a descriptor growing after its size check")
			.WithMessage("*package descriptor exceeds the 1048576-byte size limit*");
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_templateProvider.DidNotReceiveWithAnyArgs().CopyTemplateFolder(default, default, default, default);
	}

	[Test]
	[Description("Rejects a descriptor-level link without rejecting supported package-directory junctions.")]
	public void Create_ShouldRejectExistingPackage_WhenDescriptorIsLinked() {
		// Arrange
		string packagePath = Path.Combine(RootPath, "packages", PackageName);
		string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);
		_fileSystem.ExistsFile(descriptorPath).Returns(true);
		_descriptorFileInfo.Attributes.Returns(FileAttributes.ReparsePoint);

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "only the supported package-directory junction should be followed")
			.WithMessage("*linked package descriptors are not supported*");
		_fileSystem.DidNotReceive().OpenReadStream(descriptorPath);
		_applicationPackageListProvider.DidNotReceive().GetPackages();
	}

	[Test]
	[Description("Rejects an existing UI project before creating or changing its host package.")]
	public void Create_ShouldRejectExistingProjectPath_WhenProjectDirectoryExists() {
		// Arrange
		string projectPath = Path.Combine(RootPath, "projects", ProjectName);
		_fileSystem.ExistsDirectory(projectPath).Returns(true);

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "project scaffolding has no explicit overwrite or update mode")
			.WithMessage($"UI project path '{projectPath}' already exists*");
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_templateProvider.DidNotReceiveWithAnyArgs().CopyTemplateFolder(default, default, default, default);
	}

	[Test]
	[Description("Rejects an existing file at the UI project path before creating or changing its host package.")]
	public void Create_ShouldRejectExistingProjectPath_WhenProjectPathIsFile() {
		// Arrange
		string projectPath = Path.Combine(RootPath, "projects", ProjectName);
		_fileSystem.ExistsFile(projectPath).Returns(true);

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a file collision must be rejected before package creation can partially mutate the workspace")
			.WithMessage($"UI project path '{projectPath}' already exists*");
		_packageCreator.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<string>());
		_templateProvider.DidNotReceiveWithAnyArgs().CopyTemplateFolder(default, default, default, default);
	}

	[Test]
	[Description("Deletes only the owned staging directory and preserves the original failure when publication fails.")]
	public void Create_ShouldCleanStagingDirectory_WhenProjectPublishFails() {
		// Arrange
		const string failureMessage = "project publish failed";
		string projectPath = Path.Combine(RootPath, "projects", ProjectName);
		string stagingPath = null;
		_templateProvider.When(provider => provider.CopyTemplateFolder(
			"ui-project", Arg.Any<string>(), string.Empty, string.Empty))
			.Do(call => stagingPath = call.ArgAt<string>(1));
		_stagingDirectory.When(directory => directory.MoveTo(projectPath))
			.Do(_ => throw new IOException(failureMessage));
		_fileSystem.ExistsDirectory(Arg.Is<string>(path => path.EndsWith(".tmp"))).Returns(true);
		_fileSystem.When(fileSystem => fileSystem.DeleteDirectory(
			Arg.Is<string>(path => path.EndsWith(".tmp")), true))
			.Do(_ => throw new IOException("cleanup also failed"));

		// Act
		Action act = () => _creator.Create(ProjectName, PackageName, VendorPrefix, false, string.Empty, _ => false);

		// Assert
		IOException exception = act.Should()
			.Throw<IOException>(because: "cleanup must not mask the primary publish failure")
			.WithMessage(failureMessage).Which;
		exception.Data["UiProjectStagingCleanupFailure"].Should().Be("cleanup also failed",
			because: "the preserved primary exception should retain cleanup diagnostics");
		stagingPath.Should().NotBeNull(because: "the template must be copied into an owned staging directory");
		_fileSystem.Received(1).DeleteDirectory(stagingPath, true);
		_fileSystem.DidNotReceive().DeleteDirectory(projectPath, true);
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
