using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace Clio.Tests.Command
{
    [TestFixture]
    internal class MergeWorkspacesCommandTest
    {
        #region Fields: Private

        private Mock<IWorkspaceMerger> _workspaceMergerMock;
        private Mock<ILogger> _loggerMock;
        private MergeWorkspacesCommand _command;
        private string _testOutputPath;
        private string _testZipFileName;

        #endregion

        #region SetUp and TearDown

        [SetUp]
        public void SetUp()
        {
            _workspaceMergerMock = new Mock<IWorkspaceMerger>();
            _loggerMock = new Mock<ILogger>();
            _command = new MergeWorkspacesCommand(_workspaceMergerMock.Object, _loggerMock.Object);
            _testOutputPath = Path.Combine(Path.GetTempPath(), "test-output");
            _testZipFileName = "TestMergedPackages";
            
            // Ensure test directory exists during test
            if (!Directory.Exists(_testOutputPath))
            {
                Directory.CreateDirectory(_testOutputPath);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test directory after test
            if (Directory.Exists(_testOutputPath))
            {
                try
                {
                    Directory.Delete(_testOutputPath, true);
                }
                catch (IOException)
                {
                    // Ignore cleanup errors
                }
            }
        }

        #endregion

        #region Test Methods

        [Test]
        public void Execute_WithValidWorkspacesAndInstall_ShouldCallMergeAndInstall()
        {
            // Arrange
            var options = new MergeWorkspacesCommandOptions
            {
                WorkspacePaths = new[] { "./workspace1", "./workspace2" },
                ZipFileName = _testZipFileName,
                Install = true,
                OutputPath = ""
            };

            // Mock needed directories
            foreach (string path in options.WorkspacePaths)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _workspaceMergerMock.Verify(
                m => m.MergeAndInstall(
                    It.Is<string[]>(paths => paths.Length == 2),
                    _testZipFileName
                ),
                Times.Once
            );
        }

        [Test]
        public void Execute_WithValidWorkspacesAndOutputPath_ShouldCallMergeToZip()
        {
            // Arrange
            var options = new MergeWorkspacesCommandOptions
            {
                WorkspacePaths = new[] { "./workspace1", "./workspace2" },
                ZipFileName = _testZipFileName,
                Install = true,
                OutputPath = _testOutputPath
            };

            string expectedZipPath = Path.Combine(_testOutputPath, $"{_testZipFileName}.zip");
            _workspaceMergerMock.Setup(m => m.MergeToZip(
                It.Is<string[]>(paths => paths.Length == 2),
                _testOutputPath,
                _testZipFileName
            )).Returns(expectedZipPath);

            // Mock needed directories
            foreach (string path in options.WorkspacePaths)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _workspaceMergerMock.Verify(
                m => m.MergeToZip(
                    It.Is<string[]>(paths => paths.Length == 2),
                    _testOutputPath,
                    _testZipFileName
                ),
                Times.Once
            );
            
            // Should also call MergeAndInstall when install is true
            _workspaceMergerMock.Verify(
                m => m.MergeAndInstall(
                    It.Is<string[]>(paths => paths.Length == 2),
                    _testZipFileName
                ),
                Times.Once
            );
        }

        [Test]
        public void Execute_WithOutputPathAndNoInstall_ShouldOnlyCallMergeToZip()
        {
            // Arrange
            var options = new MergeWorkspacesCommandOptions
            {
                WorkspacePaths = new[] { "./workspace1", "./workspace2" },
                ZipFileName = _testZipFileName,
                Install = false,  // Don't install
                OutputPath = _testOutputPath
            };

            string expectedZipPath = Path.Combine(_testOutputPath, $"{_testZipFileName}.zip");
            _workspaceMergerMock.Setup(m => m.MergeToZip(
                It.Is<string[]>(paths => paths.Length == 2),
                _testOutputPath,
                _testZipFileName
            )).Returns(expectedZipPath);

            // Mock needed directories
            foreach (string path in options.WorkspacePaths)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _workspaceMergerMock.Verify(
                m => m.MergeToZip(
                    It.Is<string[]>(paths => paths.Length == 2),
                    _testOutputPath,
                    _testZipFileName
                ),
                Times.Once
            );
            
            // MergeAndInstall should not be called
            _workspaceMergerMock.Verify(
                m => m.MergeAndInstall(
                    It.IsAny<string[]>(),
                    It.IsAny<string>()
                ),
                Times.Never
            );
        }

        [Test]
        public void Execute_WithNoOutputPathAndNoInstall_ShouldReturnSuccessWithWarning()
        {
            // Arrange
            var options = new MergeWorkspacesCommandOptions
            {
                WorkspacePaths = new[] { "./workspace1", "./workspace2" },
                ZipFileName = _testZipFileName,
                Install = false,  // Don't install
                OutputPath = ""   // No output path
            };

            // Mock needed directories
            foreach (string path in options.WorkspacePaths)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            
            // Neither method should be called
            _workspaceMergerMock.Verify(
                m => m.MergeToZip(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                ),
                Times.Never
            );
            
            _workspaceMergerMock.Verify(
                m => m.MergeAndInstall(
                    It.IsAny<string[]>(),
                    It.IsAny<string>()
                ),
                Times.Never
            );
            
            // Should log a warning
            _loggerMock.Verify(
                l => l.WriteWarning(It.Is<string>(s => s.Contains("No action was performed"))),
                Times.Once
            );
        }

        [Test]
        public void Execute_WithNonExistingWorkspace_ShouldReturnError()
        {
            // Arrange
            var options = new MergeWorkspacesCommandOptions
            {
                WorkspacePaths = new[] { "./non-existent-workspace" },
                ZipFileName = _testZipFileName,
                Install = true
            };

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
            _loggerMock.Verify(
                l => l.WriteError(It.Is<string>(s => s.Contains("not found"))),
                Times.Once
            );
        }

        [Test]
        public void Execute_WhenMergerThrowsException_ShouldLogErrorAndReturnErrorCode()
        {
            // Arrange
            var options = new MergeWorkspacesCommandOptions
            {
                WorkspacePaths = new[] { "./workspace1" },
                ZipFileName = _testZipFileName,
                Install = true
            };

            // Mock needed directories
            foreach (string path in options.WorkspacePaths)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            // Setup merger to throw exception
            _workspaceMergerMock.Setup(m => m.MergeAndInstall(
                It.IsAny<string[]>(),
                It.IsAny<string>()
            )).Throws(new InvalidOperationException("Test error"));

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
            _loggerMock.Verify(
                l => l.WriteError("Test error"),
                Times.Once
            );
        }

        #endregion
    }
}