using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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

        private IWorkspaceMerger _workspaceMerger;
        private ILogger _logger;
        private MergeWorkspacesCommand _command;
        private string _testOutputPath;
        private string _testZipFileName;

        #endregion

        #region SetUp and TearDown

        [SetUp]
        public void SetUp()
        {
            _workspaceMerger = Substitute.For<IWorkspaceMerger>();
            _logger = Substitute.For<ILogger>();
            _command = new MergeWorkspacesCommand(_workspaceMerger, _logger);
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
            _workspaceMerger.Received(1).MergeAndInstall(
                Arg.Is<string[]>(paths => paths.Length == 2),
                _testZipFileName
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
            _workspaceMerger.MergeToZip(
                Arg.Is<string[]>(paths => paths.Length == 2),
                _testOutputPath,
                _testZipFileName
            ).Returns(expectedZipPath);

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
            _workspaceMerger.Received(1).MergeToZip(
                Arg.Is<string[]>(paths => paths.Length == 2),
                _testOutputPath,
                _testZipFileName
            );
            
            // Should also call MergeAndInstall when install is true
            _workspaceMerger.Received(1).MergeAndInstall(
                Arg.Is<string[]>(paths => paths.Length == 2),
                _testZipFileName
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
            _workspaceMerger.MergeToZip(
                Arg.Is<string[]>(paths => paths.Length == 2),
                _testOutputPath,
                _testZipFileName
            ).Returns(expectedZipPath);

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
            _workspaceMerger.Received(1).MergeToZip(
                Arg.Is<string[]>(paths => paths.Length == 2),
                _testOutputPath,
                _testZipFileName
            );
            
            // MergeAndInstall should not be called
            _workspaceMerger.DidNotReceive().MergeAndInstall(
                Arg.Any<string[]>(),
                Arg.Any<string>()
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
            _workspaceMerger.DidNotReceive().MergeToZip(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
            
            _workspaceMerger.DidNotReceive().MergeAndInstall(
                Arg.Any<string[]>(),
                Arg.Any<string>()
            );
            
            // Should log a warning
            _logger.Received(1).WriteWarning(Arg.Is<string>(s => s.Contains("No action was performed")));
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
            _logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("not found")));
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
    _workspaceMerger
        .When(x => x.MergeAndInstall(Arg.Any<string[]>(), Arg.Any<string>()))
        .Do(x => throw new InvalidOperationException("Test error"));

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
            _logger.Received(1).WriteError("Test error");
        }

        #endregion
    }
}
