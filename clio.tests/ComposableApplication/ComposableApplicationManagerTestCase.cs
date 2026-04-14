using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Linq;
using Clio.Common;
using Clio.ComposableApplication;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using FluentAssertions;
using FluentValidation;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;
using FileSystem = System.IO.Abstractions.FileSystem;
using IZipFile = Clio.Common.IZipFile;

namespace Clio.Tests.ComposableApplication;

[Property("Module", "Core")]
public class ComposableApplicationManagerTestCase : BaseClioModuleTests {

	#region Constants: Private



	#endregion

	#region Fields: Private

	private IComposableApplicationManager _sut;
	private readonly string _tempPath = Path.GetTempPath();

	#endregion

	#region Properties: Private

	private string IconPath => Path.Combine(_tempPath, "SVG_Icons", "Partner.svg");

	private string WorkspacesFolderPath => Path.Combine(_tempPath, "workspaces");

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		ILogger logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton<IZipFile>(
			new ZipFileMockWrapper(FileSystem, new WorkingDirectoriesProvider(logger, new FileSystem())));
	}

	#endregion

	#region Methods: Public

	public override void Setup() {
		base.Setup();
		FileSystem.MockExamplesFolder("workspaces", WorkspacesFolderPath);
		FileSystem.MockExamplesFolder("SVG_Icons", Path.Combine(_tempPath, "SVG_Icons"));
		FileSystem.MockExamplesFolder("AppZips", Path.Combine(_tempPath, "AppZips"));
		_sut = Container.GetRequiredService<IComposableApplicationManager>();
	}

	#endregion

	[Test]
	public void GetAppCode() {
		// Arrange
		const string appName = "ApolloAppWorkspace";
		const string appCode = "MrktApolloApp";
		string workspacePath = Path.Combine(WorkspacesFolderPath, appName);
		// Act
		string actualAppCode = _sut.GetCode(workspacePath);

		// Assert

		actualAppCode.Should().NotBeNullOrEmpty();
		actualAppCode.Should().Be(appCode);
	}

	[Test]
	public void GetAppCode_ThrowException_IfAppDescriptorNotFound() {
		// Arrange
		const string appName = "iframe-sample";
		string workspacePath = Path.Combine(WorkspacesFolderPath, appName);

		// Act
		Action act = () => _sut.GetCode(workspacePath);

		// Assert
		act.Should()
			.Throw<FileNotFoundException>()
			.WithMessage($"No app-descriptor.json file found in the specified workspace path. {workspacePath}",
				"the app-descriptor.json file is expected to be present in the workspace folder to identify the application.");
	}

	[Test]
	public void SetIcon_ShouldSetCorrectFileNameAndIcon() {
		// Arrange
		string expectedFilePath
			= Path.Combine(WorkspacesFolderPath, "ApolloAppWorkspace", "packages", "MrktApolloApp", "Files",
				"app-descriptor.json");
		const string appName = "MrktApolloApp";

		// Act
		_sut.SetIcon(WorkspacesFolderPath, IconPath, appName);

		// Assert
		FileSystem.FileExists(expectedFilePath);
		string appDescriptorContent = FileSystem.File.ReadAllText(expectedFilePath);

		AppDescriptorJson appDescriptor = JsonConvert.DeserializeObject<AppDescriptorJson>(appDescriptorContent);
		string iconFileName = Path.GetFileNameWithoutExtension(IconPath);
		const string timestampPattern = @"\d{14}.svg$"; // Matches the datetime format "yyyyMMddHHmmss"
		appDescriptor.IconName.Should().MatchRegex($"{iconFileName}_{timestampPattern}");

		string expectedIconBase64 = Convert.ToBase64String(
			File.ReadAllBytes(Path.Combine(FileSystemExtension.ExamplesFolderPath, "SVG_Icons", "Partner.svg")));
		appDescriptor.Icon.Should().Be(expectedIconBase64,
			"the icon in the descriptor should match the base64-encoded SVG file content");
	}

	[Test]
	[Description("Ensures that the correct icon is set when using a zip archive.")]
	public void SetIcon_ShouldSetCorrectIcon_WhenUsingZipArchive() {
		// Arrange
		string zipAppPath = Path.Combine(_tempPath, "AppZips", "MrktApolloApp.zip");
		string unzipAppPath = Path.Combine(_tempPath, "AppZips");

		// Act
		_sut.SetIcon(zipAppPath, IconPath, string.Empty);

		// Assert
		FileSystem.FileExists(zipAppPath).Should().BeTrue("because the zip file should exist after setting the icon");
		Container
			.GetRequiredService<IPackageArchiver>()
			.ExtractPackages(zipAppPath, true, true, true, false, unzipAppPath);
		string appDescriptorContent
			= FileSystem.File.ReadAllText(Path.Combine(unzipAppPath, "MrktApolloApp", "Files", "app-descriptor.json"));
		AppDescriptorJson appDescriptor = JsonConvert.DeserializeObject<AppDescriptorJson>(appDescriptorContent);
		string iconFileName = Path.GetFileNameWithoutExtension(IconPath);
		string timestampPattern = @"\d{14}.svg$";
		appDescriptor.IconName.Should().MatchRegex($"{iconFileName}_{timestampPattern}",
			"because the icon name should follow the expected pattern");

		string expectedIconBase64 = Convert.ToBase64String(
			File.ReadAllBytes(Path.Combine(FileSystemExtension.ExamplesFolderPath, "SVG_Icons", "Partner.svg")));
		appDescriptor.Icon.Should()
					.Be(expectedIconBase64, "because the icon content should match the expected base64 string");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_AppDescriptorDoesNotExist() {
		// Arrange
		const string appName = "iframe-sample";

		string packagesFolderPath = Path.Combine(WorkspacesFolderPath, "iframe-sample");
		// Act
		Action act = () => _sut.SetIcon(packagesFolderPath, IconPath, appName);

		// Assert
		act.Should().Throw<FileNotFoundException>()
			.WithMessage($"No app-descriptor.json file found in the specified packages folder path. {packagesFolderPath}");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_AppNotFound() {
		// Arrange
		const string appName = "NonExistingApp";

		// Act
		Action act = () => _sut.SetIcon(WorkspacesFolderPath, IconPath, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage($"App {appName} not found.");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_AppPathIsEmpty() {
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon(string.Empty, IconPath, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage($"Validation failed: {Environment.NewLine} -- AppPath: App path is required. Severity: Error");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_IconPathIsEmpty() {
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon(WorkspacesFolderPath, string.Empty, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage($"Validation failed: {Environment.NewLine} -- IconPath: Icon path is required. Severity: Error");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_IconPathNonExistant() {
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon(WorkspacesFolderPath, "C:\\1.svg", appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage(
				$"Validation failed: {Environment.NewLine} -- IconPath: Icon file 'C:\\1.svg' must exist. Severity: Error");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_MultipleAppDescriptorFoundWithAppCode() {
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon(WorkspacesFolderPath, IconPath, appName);

		string pathV1 = Path.Combine(WorkspacesFolderPath, "MyAppV1", "packages", "MrktApolloApp", "Files",
			"app-descriptor.json");
		string pathV2 = Path.Combine(WorkspacesFolderPath, "MyAppV2", "packages", "MrktApolloApp", "Files",
			"app-descriptor.json");

		// Assert
		InvalidOperationException ex = act
										.Should()
										.Throw<InvalidOperationException>()
										.Which;

		ex.Message.Should()
		.StartWith("More than one app-descriptor.json file found with the same Code:");

		var normalizedMessage = NormalizeLineEndings(ex.Message);
		string[] lines = normalizedMessage
							.Split(["\n"], StringSplitOptions.RemoveEmptyEntries);

		lines.Skip(1)
			.Should()
			.BeEquivalentTo(pathV1, pathV2);
	}

	private Func<string, string> NormalizeLineEndings = (text) =>
		text.Replace("\r\n", "\n").Replace("\r", "\n");
	
	[Test]
	public void SetIcon_ShouldThrow_When_PackagesFolderPathNonExistant() {
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon("C:\\NonRealDir", IconPath, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage(
				$"Validation failed: {Environment.NewLine} -- AppPath: Path 'C:\\NonRealDir' must exist as a directory or a file. Severity: Error");
	}

}

public class ZipFileMockWrapper : IZipFile {

	#region Fields: Private

	private readonly MockFileSystem _fileSystem;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

	#endregion

	#region Constructors: Public

	public ZipFileMockWrapper(MockFileSystem fileSystem, IWorkingDirectoriesProvider workingDirectoriesProvider) {
		_fileSystem = fileSystem;
		_workingDirectoriesProvider = workingDirectoriesProvider;
	}

	#endregion

	#region Methods: Public

	public void CreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName) {
		List<string> allFiles = _fileSystem.Directory
											.GetFiles(sourceDirectoryName, "*", SearchOption.AllDirectories)
											.ToList();

		_workingDirectoriesProvider.CreateTempDirectory(tempDir => {
			foreach (string file in allFiles) {
				string relativePathFileName = file.Replace(sourceDirectoryName, string.Empty);
				string realFsFileName = Path.Join(tempDir, relativePathFileName.TrimStart('\\'));
				DirectoryInfo newDir = Directory.CreateDirectory(Path.GetDirectoryName(realFsFileName));
				byte[] fakeFsBytes = _fileSystem.File.ReadAllBytes(file);
				File.WriteAllBytes(realFsFileName, fakeFsBytes); //write to real FS
			}

			_workingDirectoriesProvider.CreateTempDirectory(realFsSrcPath => {
				string fileName = Path.Combine(realFsSrcPath, Path.GetFileName(destinationArchiveFileName));
				ZipFile.CreateFromDirectory(tempDir, fileName);
				byte[] fakeFsBytes = File.ReadAllBytes(fileName);

				_fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(fileName));
				_fileSystem.File.WriteAllBytes(destinationArchiveFileName, fakeFsBytes);
			});
		});
	}

	public void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName) {
		_workingDirectoriesProvider.CreateTempDirectory(tempDir => {
			_workingDirectoriesProvider.CreateTempDirectory(realFsSrcPath => {
				byte[] fakeFsBytes = _fileSystem.File.ReadAllBytes(sourceArchiveFileName);
				string fileName = Path.Combine(realFsSrcPath, Path.GetFileName(sourceArchiveFileName));
				File.WriteAllBytes(fileName, fakeFsBytes); //write to real FS
				ZipFile.ExtractToDirectory(fileName, tempDir); //write to real FS
				_fileSystem.MockFolder(tempDir, destinationDirectoryName);
			});
		});
	}

	#endregion

}
