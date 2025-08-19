using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command;
using Clio.Common;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Integration
{
    [TestFixture]
    public class UpdateShellIntegrationTests
    {
        #region Fields: Private

        private UpdateShellCommand _command;
        private IApplicationClient _mockApplicationClient;
        private EnvironmentSettings _environmentSettings;
        private IFileSystem _mockFileSystem;
        private ICompressionUtilities _mockCompressionUtilities;
        private IProcessExecutor _mockProcessExecutor;
        private ISysSettingsManager _mockSysSettingsManager;
        private IServiceUrlBuilder _mockServiceUrlBuilder;
        private string _testRepositoryRoot;
        private string _testShellDirectory;

        #endregion

        #region Methods: Public

        [SetUp]
        public void SetUp()
        {
            // Setup test paths
            _testRepositoryRoot = @"C:\TestRepo";
            _testShellDirectory = Path.Combine(_testRepositoryRoot, "dist", "apps", "studio-enterprise", "shell");

            // Setup mocks
            _mockApplicationClient = Substitute.For<IApplicationClient>();
            _environmentSettings = new EnvironmentSettings
            {
                Uri = "http://test.creatio.com",
                Login = "test",
                Password = "test"
            };
            _mockFileSystem = Substitute.For<IFileSystem>();
            _mockCompressionUtilities = Substitute.For<ICompressionUtilities>();
            _mockProcessExecutor = Substitute.For<IProcessExecutor>();
            _mockSysSettingsManager = Substitute.For<ISysSettingsManager>();
            _mockServiceUrlBuilder = Substitute.For<IServiceUrlBuilder>();

            // Setup file system mocks
            _mockFileSystem.ExistsFile(Path.Combine(_testRepositoryRoot, "package.json")).Returns(true);
            _mockFileSystem.ExistsDirectory(_testShellDirectory).Returns(true);
            _mockFileSystem.GetFiles(_testShellDirectory, "*", SearchOption.AllDirectories)
                .Returns(new[] { 
                    Path.Combine(_testShellDirectory, "main.js"),
                    Path.Combine(_testShellDirectory, "styles.css"),
                    Path.Combine(_testShellDirectory, "assets", "logo.png"),
                    Path.Combine(_testShellDirectory, "components", "app.component.js")
                });

            // Create command with mocked dependencies
            _command = new UpdateShellCommand(
                _mockApplicationClient,
                _environmentSettings,
                _mockFileSystem,
                _mockCompressionUtilities,
                _mockProcessExecutor,
                _mockSysSettingsManager,
                _mockServiceUrlBuilder);

            // Setup default successful responses
            _mockProcessExecutor.Execute("npm", "run build:shell", true, _testRepositoryRoot, Arg.Any<bool>())
                .Returns("Build completed successfully");

            _mockSysSettingsManager.GetSysSettingValueByCode("MaxFileSize").Returns("50");

            // Setup ServiceUrlBuilder mock
            _mockServiceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.UploadStaticFile)
                .Returns("/rest/CreatioApiGateway/UploadStaticFile");

            _mockApplicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
                .Returns("{\"success\": true}");
        }

        #endregion

        #region Tests: Full Workflow

        [Test]
        public void Execute_WithBuildOption_ShouldExecuteFullWorkflow()
        {
            // Arrange
            var options = new UpdateShellOptions
            {
                Environment = "test",
                Build = true,
                Verbose = true
            };

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0);

            // Verify build was executed
            _mockProcessExecutor.Received(1).Execute("npm", "run build:shell", true, _testRepositoryRoot, true);

            // Verify compression was called
            _mockCompressionUtilities.Received(1).ZipDirectory(_testShellDirectory, Arg.Any<string>());

            // Verify MaxFileSize was checked
            _mockSysSettingsManager.Received(1).GetSysSettingValueByCode("MaxFileSize");

            // Verify upload was attempted
            _mockApplicationClient.Received(1).ExecutePostRequest(
                "/rest/CreatioApiGateway/UploadStaticFile",
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>());
        }

        [Test]
        public void Execute_WithoutBuildOption_ShouldSkipBuild()
        {
            // Arrange
            var options = new UpdateShellOptions
            {
                Environment = "test",
                Build = false
            };

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0);

            // Verify build was NOT executed
            _mockProcessExecutor.DidNotReceive().Execute("npm", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>());

            // Verify other steps were executed
            _mockCompressionUtilities.Received(1).ZipDirectory(_testShellDirectory, Arg.Any<string>());
            _mockApplicationClient.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
        }

        [Test]
        public void Execute_WithDryRun_ShouldNotUpload()
        {
            // Arrange
            var options = new UpdateShellOptions
            {
                Environment = "test",
                DryRun = true
            };

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0);

            // Verify compression was called (for size calculation)
            _mockCompressionUtilities.Received(1).ZipDirectory(_testShellDirectory, Arg.Any<string>());

            // Verify MaxFileSize was checked
            _mockSysSettingsManager.Received(1).GetSysSettingValueByCode("MaxFileSize");

            // Verify upload was NOT attempted
            _mockApplicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
        }

        #endregion

        #region Tests: Error Scenarios

        [Test]
        public void Execute_WithMissingShellDirectory_ShouldFail()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test" };
            
            // Mock missing shell directory
            _mockFileSystem.ExistsDirectory(_testShellDirectory).Returns(false);

            // Act & Assert
            var result = _command.Execute(options);
            result.Should().Be(1);
        }

        [Test]
        public void Execute_WithBuildFailure_ShouldFail()
        {
            // Arrange
            var options = new UpdateShellOptions
            {
                Environment = "test",
                Build = true
            };

            _mockProcessExecutor.When(x => x.Execute("npm", "run build:shell", true, _testRepositoryRoot, false))
                .Do(x => throw new Exception("npm build failed"));

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
        }

        [Test]
        public void Execute_WithNetworkError_ShouldFail()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test" };

            _mockApplicationClient.When(x => x.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
                .Do(x => throw new Exception("Network error"));

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
        }

        #endregion

        #region Tests: File System Integration

        [Test]
        public void Execute_ShouldCreateAndCleanupTempFiles()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test" };
            var tempFiles = new List<string>();

            // Track temporary files created during compression
            _mockCompressionUtilities.When(x => x.ZipDirectory(Arg.Any<string>(), Arg.Any<string>()))
                .Do(callInfo =>
                {
                    var tempFile = callInfo.ArgAt<string>(1);
                    tempFiles.Add(tempFile);
                    // Simulate file creation
                    _mockFileSystem.ExistsFile(tempFile).Returns(true);
                    _mockFileSystem.ReadAllBytes(tempFile).Returns(new byte[1024 * 1024]); // 1MB file
                });

            // Act
            _command.Execute(options);

            // Assert
            tempFiles.Should().HaveCount(1);
            
            // Verify temporary file was created and then cleaned up
            var tempFile = tempFiles[0];
            tempFile.Should().NotBeNullOrEmpty();
            
            // Verify file deletion was attempted
            _mockFileSystem.Received(1).DeleteFile(tempFile);
        }

        #endregion

        #region Tests: MaxFileSize Updates

        [Test]
        public void Execute_WithInsufficientMaxFileSize_ShouldUpdateSetting()
        {
            // Arrange
            var options = new UpdateShellOptions
            {
                Environment = "test",
                Force = true
            };

            // Mock insufficient MaxFileSize (5MB)
            _mockSysSettingsManager.GetSysSettingValueByCode("MaxFileSize").Returns("5");

            // Mock large archive creation (simulating 10MB)
            _mockCompressionUtilities.When(x => x.ZipDirectory(Arg.Any<string>(), Arg.Any<string>()))
                .Do(callInfo =>
                {
                    var tempFile = callInfo.ArgAt<string>(1);
                    _mockFileSystem.ExistsFile(tempFile).Returns(true);
                    _mockFileSystem.ReadAllBytes(tempFile).Returns(new byte[10 * 1024 * 1024]); // 10MB file
                });

            // Act
            _command.Execute(options);

            // Assert
            _mockSysSettingsManager.Received(1).SetSysSettingByCode("MaxFileSize", Arg.Any<string>());
        }

        [Test]
        public void Execute_WithSufficientMaxFileSize_ShouldNotUpdateSetting()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test" };

            // Mock sufficient MaxFileSize (50MB)
            _mockSysSettingsManager.GetSysSettingValueByCode("MaxFileSize").Returns("50");

            // Mock small archive creation (simulating 1MB)
            _mockCompressionUtilities.When(x => x.ZipDirectory(Arg.Any<string>(), Arg.Any<string>()))
                .Do(callInfo =>
                {
                    var tempFile = callInfo.ArgAt<string>(1);
                    _mockFileSystem.ExistsFile(tempFile).Returns(true);
                    _mockFileSystem.ReadAllBytes(tempFile).Returns(new byte[1024 * 1024]); // 1MB file
                });

            // Act
            _command.Execute(options);

            // Assert
            _mockSysSettingsManager.DidNotReceive().SetSysSettingByCode("MaxFileSize", Arg.Any<string>());
        }

        #endregion
    }

    #region Helper Classes

    // SysSettings is now imported from CreatioModel namespace

    #endregion
}