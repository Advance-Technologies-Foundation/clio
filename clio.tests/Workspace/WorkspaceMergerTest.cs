using Autofac;
using Clio.Common;
using Clio.Package;
using Clio.Utilities;
using Clio.Workspaces;
using FluentAssertions;
using Moq;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;

namespace Clio.Tests.Workspace
{
    [TestFixture]
    internal class WorkspaceMergerTest
    {
        #region Fields: Private

        private Mock<IWorkspacePathBuilder> _workspacePathBuilderMock;
        private Mock<IPackageArchiver> _packageArchiverMock;
        private Mock<IPackageInstaller> _packageInstallerMock;
        private Mock<IWorkingDirectoriesProvider> _workingDirectoriesProviderMock;
        private MockFileSystem _fileSystemMock;
        private Mock<ILogger> _loggerMock;
        private EnvironmentSettings _testEnvironmentSettings;
        private WorkspaceMerger _workspaceMerger;

        #endregion

        #region Methods: Private

        private void SetupDefaultMocks()
        {
            _workspacePathBuilderMock = new Mock<IWorkspacePathBuilder>();
            _packageArchiverMock = new Mock<IPackageArchiver>();
            _packageInstallerMock = new Mock<IPackageInstaller>();
            _workingDirectoriesProviderMock = new Mock<IWorkingDirectoriesProvider>();
            _fileSystemMock = new MockFileSystem();
            _loggerMock = new Mock<ILogger>();

            _testEnvironmentSettings = new EnvironmentSettings
            {
                Uri = "https://test-environment.creatio.com/",
                Login = "Supervisor",
                Password = "Supervisor",
                IsNetCore = true
            };

            // Setup workspace paths
            string[] workspacePaths = new[] { "/path/to/workspace1", "/path/to/workspace2" };
            foreach (string workspacePath in workspacePaths)
            {
                string packagesPath = Path.Combine(workspacePath, "packages");
                _fileSystemMock.AddDirectory(workspacePath);
                _fileSystemMock.AddDirectory(packagesPath);

                // Add sample packages to each workspace
                _fileSystemMock.AddDirectory(Path.Combine(packagesPath, "Package1"));
                _fileSystemMock.AddDirectory(Path.Combine(packagesPath, "Package2"));
                // Add a unique package to the second workspace
                if (workspacePath.EndsWith("workspace2"))
                {
                    _fileSystemMock.AddDirectory(Path.Combine(packagesPath, "UniquePackage"));
                }
            }

            // Setup workspace path builder behavior
            _workspacePathBuilderMock.Setup(w => w.PackagesFolderPath).Returns("/path/to/packages");

            // Setup working directories provider to execute action with a temp directory
            _workingDirectoriesProviderMock
                .Setup(w => w.CreateTempDirectory(It.IsAny<Action<string>>()))
                .Callback<Action<string>>(action => action("/temp"));

            _workspaceMerger = new WorkspaceMerger(
                _testEnvironmentSettings,
                _workspacePathBuilderMock.Object,
                _packageArchiverMock.Object,
                _packageInstallerMock.Object,
                _workingDirectoriesProviderMock.Object,
                _fileSystemMock,
                _loggerMock.Object
            );
        }

        #endregion

        #region SetUp and TearDown

        [SetUp]
        public void SetUp()
        {
            SetupDefaultMocks();
        }

        #endregion

        #region Test Methods

        [Test]
        public void MergeAndInstall_WithValidWorkspaces_ShouldCallPackageInstallerWithMergedZip()
        {
            // Arrange
            string[] workspacePaths = new[] { "/path/to/workspace1", "/path/to/workspace2" };
            string tempDir = "/temp";
            string rootPackedDir = Path.Combine(tempDir, "MergedCreatioPackages");
            string resultZipPath = Path.Combine(tempDir, "MergedCreatioPackages.zip");

            _workspacePathBuilderMock.SetupSequence(w => w.RootPath)
                .Returns(workspacePaths[0])
                .Returns(workspacePaths[1]);

            _workspacePathBuilderMock.SetupSequence(w => w.PackagesFolderPath)
                .Returns(Path.Combine(workspacePaths[0], "packages"))
                .Returns(Path.Combine(workspacePaths[1], "packages"));

            // Setup filesystem directory existence check
            foreach (string workspacePath in workspacePaths)
            {
                string packagesPath = Path.Combine(workspacePath, "packages");
                _fileSystemMock.AddDirectory(workspacePath);
                _fileSystemMock.AddDirectory(packagesPath);
            }

            _fileSystemMock.AddDirectory(tempDir);
            _fileSystemMock.AddFile(resultZipPath, new MockFileData("test zip content"));

            // Setup GetDirectories to return package directories
            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[0], "packages", "Package1"));
            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[0], "packages", "Package2"));
            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[1], "packages", "Package3"));
            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[1], "packages", "Package4"));

            _packageArchiverMock.Setup(p => p.ZipPackages(rootPackedDir, resultZipPath, true))
                .Returns(resultZipPath);

            // Act
            _workspaceMerger.MergeAndInstall(workspacePaths);

            // Assert
            _packageInstallerMock.Verify(p => p.Install(resultZipPath, _testEnvironmentSettings), Times.Once);
            _loggerMock.Verify(l => l.WriteInfo(It.IsAny<string>()), Times.AtLeast(3));
        }

        [Test]
        public void MergeAndInstall_WithDuplicatePackages_ShouldSkipDuplicates()
        {
            // Arrange
            string[] workspacePaths = new[] { "/path/to/workspace1", "/path/to/workspace2" };
            string tempDir = "/temp";
            string rootPackedDir = Path.Combine(tempDir, "MergedCreatioPackages");
            string resultZipPath = Path.Combine(tempDir, "MergedCreatioPackages.zip");

            _workspacePathBuilderMock.SetupSequence(w => w.RootPath)
                .Returns(workspacePaths[0])
                .Returns(workspacePaths[1]);

            _workspacePathBuilderMock.SetupSequence(w => w.PackagesFolderPath)
                .Returns(Path.Combine(workspacePaths[0], "packages"))
                .Returns(Path.Combine(workspacePaths[1], "packages"));

            // Setup filesystem
            foreach (string workspacePath in workspacePaths)
            {
                string packagesPath = Path.Combine(workspacePath, "packages");
                _fileSystemMock.AddDirectory(workspacePath);
                _fileSystemMock.AddDirectory(packagesPath);
            }

            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[0], "packages", "Common"));
            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[0], "packages", "Unique1"));
            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[1], "packages", "Common"));
            _fileSystemMock.AddDirectory(Path.Combine(workspacePaths[1], "packages", "Unique2"));

            _fileSystemMock.AddDirectory(tempDir);
            _fileSystemMock.AddFile(resultZipPath, new MockFileData("test zip content"));

            _packageArchiverMock.Setup(p => p.ZipPackages(rootPackedDir, resultZipPath, true))
                .Returns(resultZipPath);

            // Act
            _workspaceMerger.MergeAndInstall(workspacePaths);

            // Assert
            // Verify that Pack is called exactly 3 times - once for each unique package
            _packageArchiverMock.Verify(
                p => p.Pack(It.IsAny<string>(), It.IsAny<string>(), true, true),
                Times.Exactly(3)
            );

            // Verify warning for duplicate package was logged
            _loggerMock.Verify(
                l => l.WriteWarning(It.Is<string>(s => s.Contains("already processed"))),
                Times.Once
            );
        }

        [Test]
        public void MergeToZip_WithValidWorkspaces_ReturnsCorrectZipPath()
        {
            // Arrange
            string[] workspacePaths = new[] { "/path/to/workspace1", "/path/to/workspace2" };
            string outputPath = "/output";
            string zipFileName = "TestMergedPackages";
            string tempDir = "/temp";
            string rootPackedDir = Path.Combine(tempDir, zipFileName);
            string tempZipPath = Path.Combine(tempDir, $"{zipFileName}.zip");
            string expectedResultPath = Path.Combine(outputPath, $"{zipFileName}.zip");

            _workspacePathBuilderMock.SetupSequence(w => w.RootPath)
                .Returns(workspacePaths[0])
                .Returns(workspacePaths[1]);

            _workspacePathBuilderMock.SetupSequence(w => w.PackagesFolderPath)
                .Returns(Path.Combine(workspacePaths[0], "packages"))
                .Returns(Path.Combine(workspacePaths[1], "packages"));

            // Setup filesystem
            foreach (string workspacePath in workspacePaths)
            {
                string packagesPath = Path.Combine(workspacePath, "packages");
                _fileSystemMock.AddDirectory(workspacePath);
                _fileSystemMock.AddDirectory(packagesPath);
                _fileSystemMock.AddDirectory(Path.Combine(packagesPath, "TestPackage"));
            }

            _fileSystemMock.AddDirectory(tempDir);
            _fileSystemMock.AddDirectory(outputPath);
            _fileSystemMock.AddFile(tempZipPath, new MockFileData("test zip content"));

            _packageArchiverMock.Setup(p => p.ZipPackages(rootPackedDir, tempZipPath, true))
                .Returns(tempZipPath);

            // Act
            string result = _workspaceMerger.MergeToZip(workspacePaths, outputPath, zipFileName);

            // Assert
            result.Should().Be(expectedResultPath);
            _fileSystemMock.FileExists(expectedResultPath).Should().BeTrue();
        }

        [Test]
        public void MergeAndInstall_WithNoWorkspacePaths_ThrowsArgumentException()
        {
            // Arrange
            string[] emptyWorkspacePaths = Array.Empty<string>();

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                _workspaceMerger.MergeAndInstall(emptyWorkspacePaths)
            );
        }

        [Test]
        public void MergeAndInstall_WithNonExistingWorkspace_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            string[] workspacePaths = new[] { "/path/to/nonexistent" };

            // Act & Assert
            Assert.Throws<DirectoryNotFoundException>(() =>
                _workspaceMerger.MergeAndInstall(workspacePaths)
            );
        }

        [Test]
        public void MergeToZip_WithNoPackagesFound_ThrowsInvalidOperationException()
        {
            // Arrange
            string[] workspacePaths = new[] { "/path/to/workspace1", "/path/to/workspace2" };
            string outputPath = "/output";
            string zipFileName = "TestMergedPackages";

            _workspacePathBuilderMock.SetupSequence(w => w.RootPath)
                .Returns(workspacePaths[0])
                .Returns(workspacePaths[1]);

            _workspacePathBuilderMock.SetupSequence(w => w.PackagesFolderPath)
                .Returns(Path.Combine(workspacePaths[0], "packages"))
                .Returns(Path.Combine(workspacePaths[1], "packages"));

            // Setup filesystem with empty packages directories
            foreach (string workspacePath in workspacePaths)
            {
                string packagesPath = Path.Combine(workspacePath, "packages");
                _fileSystemMock.AddDirectory(workspacePath);
                _fileSystemMock.AddDirectory(packagesPath);
                // No package directories added
            }

            _fileSystemMock.AddDirectory("/output");
            _fileSystemMock.AddDirectory("/temp");

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                _workspaceMerger.MergeToZip(workspacePaths, outputPath, zipFileName)
            );
        }

        #endregion
    }
}