using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Common;
using Clio.Tests.Command;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SysIoAbstractions = System.IO.Abstractions;

namespace Clio.Tests.Workspace;

[TestFixture(Category = "Unit")]
[Description("Tests for ZipBasedApplicationDownloader class that extracts Creatio configuration from ZIP files")]
public class ZipBasedApplicationDownloaderTests : BaseClioModuleTests
{

	#region Fields: Private

	private ICompressionUtilities _compressionUtilitiesMock;
	private IWorkspacePathBuilder _workspacePathBuilderMock;
	private IWorkingDirectoriesProvider _workingDirectoriesProviderMock;
	private ILogger _loggerMock;
	private MockFileSystem _mockFileSystem;
	private const string TempDir = @"C:\temp\extracted";
	private const string WorkspaceRoot = @"C:\workspace";

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		_compressionUtilitiesMock = Substitute.For<ICompressionUtilities>();
		_workspacePathBuilderMock = Substitute.For<IWorkspacePathBuilder>();
		_workingDirectoriesProviderMock = Substitute.For<IWorkingDirectoriesProvider>();
		_loggerMock = Substitute.For<ILogger>();
		_mockFileSystem = new MockFileSystem();

		containerBuilder.RegisterInstance(_compressionUtilitiesMock).As<ICompressionUtilities>();
		containerBuilder.RegisterInstance(_workspacePathBuilderMock).As<IWorkspacePathBuilder>();
		containerBuilder.RegisterInstance(_workingDirectoriesProviderMock).As<IWorkingDirectoriesProvider>();
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
		containerBuilder.RegisterInstance((SysIoAbstractions.IFileSystem)_mockFileSystem).As<SysIoAbstractions.IFileSystem>();
		containerBuilder.RegisterInstance((IFileSystem)new FileSystem(_mockFileSystem)).As<IFileSystem>();
	}

	#endregion

	#region Methods: Private

	private void SetupWorkspacePaths(){
		_workspacePathBuilderMock.IsWorkspace.Returns(true);
		_workspacePathBuilderMock.ApplicationFolderPath.Returns(Path.Combine(WorkspaceRoot, ".application"));
		_workspacePathBuilderMock.CoreBinFolderPath.Returns(Path.Combine(WorkspaceRoot, ".application", "net-framework", "core-bin"));
		_workspacePathBuilderMock.LibFolderPath.Returns(Path.Combine(WorkspaceRoot, ".application", "net-framework", "bin"));
		_workspacePathBuilderMock.ConfigurationBinFolderPath.Returns(Path.Combine(WorkspaceRoot, ".application", "net-framework", "bin"));
		_workspacePathBuilderMock.PackagesFolderPath.Returns(Path.Combine(WorkspaceRoot, ".application", "net-framework", "packages"));
	}	private void SetupTempDirectoryCallback(Action<string> callback = null){
		_workingDirectoriesProviderMock
			.When(x => x.CreateTempDirectory(Arg.Any<Action<string>>()))
			.Do(callInfo => {
				var cleanupCallback = callInfo.ArgAt<Action<string>>(0);
				// Create temp directory in mock filesystem
				_mockFileSystem.AddDirectory(TempDir);
				callback?.Invoke(TempDir);
				cleanupCallback?.Invoke(TempDir);
			});
	}

	private void SetupZipFile(string zipFilePath){
		// Add ZIP file to mock filesystem so existence check passes
		_mockFileSystem.AddFile(zipFilePath, new MockFileData("fake zip content"));
		
		// Setup Unzip to create extracted structure
		_compressionUtilitiesMock
			.When(x => x.Unzip(zipFilePath, TempDir))
			.Do(callInfo => {
				// Unzip will be called, no need to do anything here
				// The test will create the structure manually
			});
	}

	private void CreateNetFrameworkStructure(){
		// Create Terrasoft.WebApp folder (NetFramework marker)
		var webAppPath = Path.Combine(TempDir, "Terrasoft.WebApp");
		_mockFileSystem.AddDirectory(webAppPath);

		// Create bin folder with sample DLLs
		var binPath = Path.Combine(webAppPath, "bin");
		_mockFileSystem.AddDirectory(binPath);
		_mockFileSystem.AddFile(Path.Combine(binPath, "Terrasoft.Core.dll"), new MockFileData("core dll content"));
		_mockFileSystem.AddFile(Path.Combine(binPath, "Terrasoft.Common.dll"), new MockFileData("common dll content"));

		// Create Terrasoft.Configuration/Lib folder
		var libPath = Path.Combine(webAppPath, "Terrasoft.Configuration", "Lib");
		_mockFileSystem.AddDirectory(libPath);
		_mockFileSystem.AddFile(Path.Combine(libPath, "Newtonsoft.Json.dll"), new MockFileData("json dll content"));

		// Create conf/bin/{NUMBER} folders with configuration DLLs
		var confBinPath1 = Path.Combine(webAppPath, "conf", "bin", "1");
		var confBinPath2 = Path.Combine(webAppPath, "conf", "bin", "2");
		var confBinPath3 = Path.Combine(webAppPath, "conf", "bin", "3");
		_mockFileSystem.AddDirectory(confBinPath1);
		_mockFileSystem.AddDirectory(confBinPath2);
		_mockFileSystem.AddDirectory(confBinPath3);
		
		_mockFileSystem.AddFile(Path.Combine(confBinPath3, "Terrasoft.Configuration.dll"), 
			new MockFileData("configuration dll content"));
		_mockFileSystem.AddFile(Path.Combine(confBinPath3, "Terrasoft.Configuration.ODataEntities.dll"), 
			new MockFileData("odata dll content"));

		// Create packages with Files/bin
		var pkgPath = Path.Combine(webAppPath, "Terrasoft.Configuration", "Pkg");
		_mockFileSystem.AddDirectory(pkgPath);
		
		// Package with Files/bin - should be copied
		var package1Path = Path.Combine(pkgPath, "CustomPackage1", "Files", "bin");
		_mockFileSystem.AddDirectory(package1Path);
		_mockFileSystem.AddFile(Path.Combine(package1Path, "CustomPackage1.dll"), 
			new MockFileData("package1 dll content"));
		
		// Package without Files/bin - should be skipped
		var package2Path = Path.Combine(pkgPath, "CustomPackage2");
		_mockFileSystem.AddDirectory(package2Path);
		_mockFileSystem.AddFile(Path.Combine(package2Path, "descriptor.json"), 
			new MockFileData("{}"));
		
		// Package with Files/bin - should be copied
		var package3Path = Path.Combine(pkgPath, "CustomPackage3", "Files", "bin");
		_mockFileSystem.AddDirectory(package3Path);
		_mockFileSystem.AddFile(Path.Combine(package3Path, "CustomPackage3.dll"), 
			new MockFileData("package3 dll content"));
	}

	private void CreateNetCoreStructure(){
		// No Terrasoft.WebApp folder for NetCore (NET8)
		// Root directory contains DLL and PDB files
		_mockFileSystem.AddFile(Path.Combine(TempDir, "Terrasoft.Core.dll"), new MockFileData("core dll content"));
		_mockFileSystem.AddFile(Path.Combine(TempDir, "Terrasoft.Core.pdb"), new MockFileData("core pdb content"));
		_mockFileSystem.AddFile(Path.Combine(TempDir, "Terrasoft.Common.dll"), new MockFileData("common dll content"));
		_mockFileSystem.AddFile(Path.Combine(TempDir, "Terrasoft.Common.pdb"), new MockFileData("common pdb content"));
		_mockFileSystem.AddFile(Path.Combine(TempDir, "SomeOther.dll"), new MockFileData("other dll content"));
		_mockFileSystem.AddFile(Path.Combine(TempDir, "config.json"), new MockFileData("{}")); // Should NOT be copied

		// Terrasoft.Configuration/Lib folder
		var libPath = Path.Combine(TempDir, "Terrasoft.Configuration", "Lib");
		_mockFileSystem.AddDirectory(libPath);
		_mockFileSystem.AddFile(Path.Combine(libPath, "Newtonsoft.Json.dll"), new MockFileData("json dll content"));
		_mockFileSystem.AddFile(Path.Combine(libPath, "AutoMapper.dll"), new MockFileData("automapper dll content"));

		// conf/bin/{NUMBER} structure - create multiple numbered folders
		var confBinPath = Path.Combine(TempDir, "conf", "bin");
		_mockFileSystem.AddDirectory(confBinPath);

		// Folder 1 (older)
		var confBin1 = Path.Combine(confBinPath, "1");
		_mockFileSystem.AddDirectory(confBin1);
		_mockFileSystem.AddFile(Path.Combine(confBin1, "Terrasoft.Configuration.dll"), 
			new MockFileData("old configuration dll"));

		// Folder 2 (older)
		var confBin2 = Path.Combine(confBinPath, "2");
		_mockFileSystem.AddDirectory(confBin2);
		_mockFileSystem.AddFile(Path.Combine(confBin2, "Terrasoft.Configuration.dll"), 
			new MockFileData("old configuration dll"));

		// Folder 3 (latest - should be selected)
		var confBin3 = Path.Combine(confBinPath, "3");
		_mockFileSystem.AddDirectory(confBin3);
		_mockFileSystem.AddFile(Path.Combine(confBin3, "Terrasoft.Configuration.dll"), 
			new MockFileData("latest configuration dll"));
		_mockFileSystem.AddFile(Path.Combine(confBin3, "Terrasoft.Configuration.ODataEntities.dll"), 
			new MockFileData("latest odata dll"));

		// Terrasoft.Configuration/Pkg - packages structure
		var pkgPath = Path.Combine(TempDir, "Terrasoft.Configuration", "Pkg");
		_mockFileSystem.AddDirectory(pkgPath);

		// Package with Files/bin - should be copied
		var package1Path = Path.Combine(pkgPath, "NetCorePackage1", "Files", "bin");
		_mockFileSystem.AddDirectory(package1Path);
		_mockFileSystem.AddFile(Path.Combine(package1Path, "NetCorePackage1.dll"), 
			new MockFileData("netcore package1 dll content"));

		// Package without Files/bin - should NOT be copied
		var package2Path = Path.Combine(pkgPath, "NetCorePackage2");
		_mockFileSystem.AddDirectory(package2Path);
		_mockFileSystem.AddFile(Path.Combine(package2Path, "descriptor.json"), 
			new MockFileData("{}"));

		// Package with Files/bin - should be copied
		var package3Path = Path.Combine(pkgPath, "NetCorePackage3", "Files", "bin");
		_mockFileSystem.AddDirectory(package3Path);
		_mockFileSystem.AddFile(Path.Combine(package3Path, "NetCorePackage3.dll"), 
			new MockFileData("netcore package3 dll content"));
	}

	#endregion

	#region Tests: NetFramework

	[Test]
	[Description("Should correctly detect NetFramework Creatio by presence of Terrasoft.WebApp folder")]
	public void DownloadFromZip_DetectsNetFramework_WhenTerrasoftWebAppFolderExists(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetFrameworkStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_loggerMock.Received(1).WriteInfo("Detected NetFramework Creatio");
		_compressionUtilitiesMock.Received(1).Unzip(zipFilePath, TempDir);
	}

	[Test]
	[Description("Should copy core bin files from Terrasoft.WebApp/bin to .application/net-framework/core-bin")]
	public void DownloadFromZip_CopiesCoreBinFiles_ForNetFramework(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetFrameworkStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_mockFileSystem.FileExists(Path.Combine(_workspacePathBuilderMock.CoreBinFolderPath, "Terrasoft.Core.dll"))
			.Should().BeTrue(because: "Core DLL should be copied to core-bin folder");
		_mockFileSystem.FileExists(Path.Combine(_workspacePathBuilderMock.CoreBinFolderPath, "Terrasoft.Common.dll"))
			.Should().BeTrue(because: "Common DLL should be copied to core-bin folder");
	}

	[Test]
	[Description("Should copy library files from Terrasoft.Configuration/Lib to .application/net-framework/bin")]
	public void DownloadFromZip_CopiesLibFiles_ForNetFramework(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetFrameworkStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_mockFileSystem.FileExists(Path.Combine(_workspacePathBuilderMock.LibFolderPath, "Newtonsoft.Json.dll"))
			.Should().BeTrue(because: "Library DLL should be copied to lib folder");
	}

	[Test]
	[Description("Should select latest numbered folder from conf/bin and copy configuration DLLs")]
	public void DownloadFromZip_SelectsLatestNumberedFolder_ForNetFramework(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetFrameworkStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_mockFileSystem.FileExists(Path.Combine(_workspacePathBuilderMock.ConfigurationBinFolderPath, "Terrasoft.Configuration.dll"))
			.Should().BeTrue(because: "Configuration DLL from latest numbered folder should be copied");
		_mockFileSystem.FileExists(Path.Combine(_workspacePathBuilderMock.ConfigurationBinFolderPath, "Terrasoft.Configuration.ODataEntities.dll"))
			.Should().BeTrue(because: "OData DLL from latest numbered folder should be copied");
	}

	[Test]
	[Description("Should copy only packages that have Files/bin folder")]
	public void DownloadFromZip_CopiesOnlyPackagesWithFilesBin_ForNetFramework(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetFrameworkStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_mockFileSystem.Directory.Exists(Path.Combine(_workspacePathBuilderMock.PackagesFolderPath, "CustomPackage1"))
			.Should().BeTrue(because: "Package with Files/bin should be copied");
		_mockFileSystem.Directory.Exists(Path.Combine(_workspacePathBuilderMock.PackagesFolderPath, "CustomPackage2"))
			.Should().BeFalse(because: "Package without Files/bin should not be copied");
		_mockFileSystem.Directory.Exists(Path.Combine(_workspacePathBuilderMock.PackagesFolderPath, "CustomPackage3"))
			.Should().BeTrue(because: "Package with Files/bin should be copied");
	}

	[Test]
	[Description("Should log warning when Terrasoft.WebApp/bin folder is missing")]
	public void DownloadFromZip_LogsWarning_WhenBinFolderMissing(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => {
			// Create structure without bin folder
			var webAppPath = Path.Combine(TempDir, "Terrasoft.WebApp");
			_mockFileSystem.AddDirectory(webAppPath);
		});
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_loggerMock.Received().WriteWarning(
			Arg.Is<string>(msg => msg.Contains("bin") || msg.Contains("not found")));
	}

	#endregion

	#region Tests: NetCore

	[Test]
	[Description("Should detect NetCore Creatio when Terrasoft.WebApp folder is absent")]
	public void DownloadFromZip_DetectsNetCore_WhenTerrasoftWebAppFolderAbsent(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetCoreStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_loggerMock.Received(1).WriteInfo("Detected NetCore Creatio");
	}

	[Test]
	[Description("Should copy root DLL/PDB, lib files, conf/bin/{NUMBER}, and packages for NetCore")]
	public void DownloadFromZip_CopiesFiles_ForNetCore(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetCoreStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert - Root assemblies (DLL and PDB from root directory)
		var netCoreCoreBin = Path.Combine(WorkspaceRoot, ".application", "net-core", "core-bin");
		_mockFileSystem.FileExists(Path.Combine(netCoreCoreBin, "Terrasoft.Core.dll"))
			.Should().BeTrue(because: "Root DLL should be copied to net-core/core-bin");
		_mockFileSystem.FileExists(Path.Combine(netCoreCoreBin, "Terrasoft.Core.pdb"))
			.Should().BeTrue(because: "Root PDB should be copied to net-core/core-bin");
		_mockFileSystem.FileExists(Path.Combine(netCoreCoreBin, "Terrasoft.Common.dll"))
			.Should().BeTrue(because: "Root DLL should be copied to net-core/core-bin");
		_mockFileSystem.FileExists(Path.Combine(netCoreCoreBin, "SomeOther.dll"))
			.Should().BeTrue(because: "All root DLLs should be copied");
		_mockFileSystem.FileExists(Path.Combine(netCoreCoreBin, "config.json"))
			.Should().BeFalse(because: "Only DLL and PDB files should be copied, not JSON");

		// Assert - Lib files
		var netCoreBin = Path.Combine(WorkspaceRoot, ".application", "net-core", "bin");
		_mockFileSystem.FileExists(Path.Combine(netCoreBin, "Newtonsoft.Json.dll"))
			.Should().BeTrue(because: "Lib DLL should be copied to net-core/bin");
		_mockFileSystem.FileExists(Path.Combine(netCoreBin, "AutoMapper.dll"))
			.Should().BeTrue(because: "Lib DLL should be copied to net-core/bin");

		// Assert - Configuration bin files from latest numbered folder
		_mockFileSystem.FileExists(Path.Combine(netCoreBin, "Terrasoft.Configuration.dll"))
			.Should().BeTrue(because: "Configuration DLL from latest numbered folder should be copied");
		_mockFileSystem.FileExists(Path.Combine(netCoreBin, "Terrasoft.Configuration.ODataEntities.dll"))
			.Should().BeTrue(because: "OData DLL from latest numbered folder should be copied");

		// Assert - Packages with Files/bin filter
		var netCorePackages = Path.Combine(WorkspaceRoot, ".application", "net-core", "packages");
		_mockFileSystem.Directory.Exists(Path.Combine(netCorePackages, "NetCorePackage1"))
			.Should().BeTrue(because: "Package with Files/bin should be copied");
		_mockFileSystem.Directory.Exists(Path.Combine(netCorePackages, "NetCorePackage2"))
			.Should().BeFalse(because: "Package without Files/bin should not be copied");
		_mockFileSystem.Directory.Exists(Path.Combine(netCorePackages, "NetCorePackage3"))
			.Should().BeTrue(because: "Package with Files/bin should be copied");
	}

	[Test]
	[Description("Should select latest numbered folder from conf/bin for NetCore")]
	public void DownloadFromZip_SelectsLatestNumberedFolder_ForNetCore(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetCoreStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		var netCoreBin = Path.Combine(WorkspaceRoot, ".application", "net-core", "bin");
		var configDllContent = _mockFileSystem.File.ReadAllText(Path.Combine(netCoreBin, "Terrasoft.Configuration.dll"));
		configDllContent.Should().Be("latest configuration dll", because: "Should select files from folder 3 (latest)");
	}

	[Test]
	[Description("Should copy only packages with Files/bin folder for NetCore")]
	public void DownloadFromZip_CopiesOnlyPackagesWithFilesBin_ForNetCore(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => CreateNetCoreStructure());
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		var netCorePackages = Path.Combine(WorkspaceRoot, ".application", "net-core", "packages");
		_mockFileSystem.Directory.Exists(Path.Combine(netCorePackages, "NetCorePackage1"))
			.Should().BeTrue(because: "NetCore package with Files/bin should be copied");
		_mockFileSystem.Directory.Exists(Path.Combine(netCorePackages, "NetCorePackage2"))
			.Should().BeFalse(because: "NetCore package without Files/bin should not be copied");
		_mockFileSystem.Directory.Exists(Path.Combine(netCorePackages, "NetCorePackage3"))
			.Should().BeTrue(because: "NetCore package with Files/bin should be copied");
	}

	#endregion

	#region Tests: Error Handling

	[Test]
	[Description("Should throw exception when workspace is not valid")]
	public void DownloadFromZip_ThrowsException_WhenWorkspaceInvalid(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		_workspacePathBuilderMock.RootPath.Returns((string)null);
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act & Assert
		Action act = () => downloader.DownloadFromZip(zipFilePath);
		act.Should().Throw<InvalidOperationException>(
			because: "Invalid workspace should cause exception")
			.WithMessage("*workspace*", 
			because: "Error message should mention workspace");
	}

	[Test]
	[Description("Should log warning when conf/bin folder has no numbered subfolders")]
	public void DownloadFromZip_LogsWarning_WhenNoNumberedFolders(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupTempDirectoryCallback(tempDir => {
			// Create NetFramework structure without numbered folders
			var webAppPath = Path.Combine(TempDir, "Terrasoft.WebApp");
			_mockFileSystem.AddDirectory(webAppPath);
			var confBinPath = Path.Combine(webAppPath, "conf", "bin");
			_mockFileSystem.AddDirectory(confBinPath);
		});
		SetupZipFile(zipFilePath);
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		_loggerMock.Received().WriteWarning(
			Arg.Is<string>(msg => msg.Contains("numbered") || msg.Contains("conf")));
	}

	#endregion

	#region Tests: Cleanup

	[Test]
	[Description("Should call cleanup callback for temp directory")]
	public void DownloadFromZip_CallsCleanupCallback(){
		// Arrange
		var zipFilePath = @"C:\creatio.zip";
		SetupWorkspacePaths();
		SetupZipFile(zipFilePath);
		
		bool cleanupCalled = false;
		_workingDirectoriesProviderMock
			.When(x => x.CreateTempDirectory(Arg.Any<Action<string>>()))
			.Do(callInfo => {
				var callback = callInfo.ArgAt<Action<string>>(0);
				_mockFileSystem.AddDirectory(TempDir);
				CreateNetFrameworkStructure();
				callback?.Invoke(TempDir);
				cleanupCalled = true;
			});
		
		var downloader = Container.Resolve<IZipBasedApplicationDownloader>();

		// Act
		downloader.DownloadFromZip(zipFilePath);

		// Assert
		cleanupCalled.Should().BeTrue(because: "Cleanup callback should be invoked to delete temp directory");
	}

	#endregion

}
