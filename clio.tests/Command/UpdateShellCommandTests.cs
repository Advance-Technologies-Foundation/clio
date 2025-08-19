using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command
{
    [TestFixture]
    public class UpdateShellCommandTests
    {
        #region Fields: Private

        private UpdateShellCommand _command;
        private IApplicationClient _mockApplicationClient;
        private EnvironmentSettings _mockEnvironmentSettings;
        private IFileSystem _mockFileSystem;
        private ICompressionUtilities _mockCompressionUtilities;
        private IProcessExecutor _mockProcessExecutor;
        private ISysSettingsManager _mockSysSettingsManager;
        private IServiceUrlBuilder _mockServiceUrlBuilder;

        #endregion

        #region Methods: Public

        [SetUp]
        public void SetUp()
        {
            _mockApplicationClient = Substitute.For<IApplicationClient>();
            _mockEnvironmentSettings = new EnvironmentSettings
            {
                Uri = "http://test.com",
                Login = "test",
                Password = "test"
            };
            _mockFileSystem = Substitute.For<IFileSystem>();
            _mockCompressionUtilities = Substitute.For<ICompressionUtilities>();
            _mockProcessExecutor = Substitute.For<IProcessExecutor>();
            _mockSysSettingsManager = Substitute.For<ISysSettingsManager>();
            _mockServiceUrlBuilder = Substitute.For<IServiceUrlBuilder>();

            _command = new UpdateShellCommand(
                _mockApplicationClient,
                _mockEnvironmentSettings,
                _mockFileSystem,
                _mockCompressionUtilities,
                _mockProcessExecutor,
                _mockSysSettingsManager,
                _mockServiceUrlBuilder);
        }

        #endregion

        #region Tests: Command Options

        [Test]
        public void UpdateShellOptions_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var options = new UpdateShellOptions();

            // Assert
            options.Build.Should().BeFalse();
            options.Force.Should().BeFalse();
            options.Verbose.Should().BeFalse();
            options.DryRun.Should().BeFalse();
        }

        [Test]
        public void UpdateShellOptions_ShouldAcceptAllParameters()
        {
            // Arrange & Act
            var options = new UpdateShellOptions
            {
                Build = true,
                Force = true,
                Verbose = true,
                DryRun = true,
                Environment = "test"
            };

            // Assert
            options.Build.Should().BeTrue();
            options.Force.Should().BeTrue();
            options.Verbose.Should().BeTrue();
            options.DryRun.Should().BeTrue();
            options.Environment.Should().Be("test");
        }

        #endregion

        #region Tests: Repository Root Finding

        [Test]
        public void FindRepositoryRoot_ShouldReturnCurrentDirectoryWhenPackageJsonExists()
        {
            // Arrange
            var currentDir = Directory.GetCurrentDirectory();
            var packageJsonPath = Path.Combine(currentDir, "package.json");
            _mockFileSystem.ExistsFile(packageJsonPath).Returns(true);

            var options = new UpdateShellOptions { Environment = "test" };

            // Mock shell directory exists
            var shellDir = Path.Combine(currentDir, "dist", "apps", "studio-enterprise", "shell");
            _mockFileSystem.ExistsDirectory(shellDir).Returns(true);
            _mockFileSystem.GetFiles(shellDir, "*", SearchOption.AllDirectories).Returns(new[] { "test.js" });

            // Mock successful compression
            var tempFile = Path.Combine(Path.GetTempPath(), "shell-test.gz");
            _mockCompressionUtilities.When(x => x.ZipDirectory(shellDir, Arg.Any<string>()))
                .Do(callInfo => { /* Simulate file creation */ });

            // Mock system settings
            _mockSysSettingsManager.GetSysSettingValueByCode("MaxFileSize").Returns("50");

            // Act & Assert
            Assert.DoesNotThrow(() => _command.Execute(options));
        }

        [Test]
        public void FindRepositoryRoot_ShouldThrowWhenPackageJsonNotFound()
        {
            // Arrange
            _mockFileSystem.ExistsFile(Arg.Any<string>()).Returns(false);
            var options = new UpdateShellOptions { Environment = "test" };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => _command.Execute(options));
            exception.Message.Should().Contain("Could not find repository root");
        }

        #endregion

        #region Tests: Build Process

        [Test]
        public void ExecuteBuildProcess_ShouldCallProcessExecutorWhenBuildOptionEnabled()
        {
            // Arrange
            var options = new UpdateShellOptions { Build = true, Environment = "test" };
            var currentDir = Directory.GetCurrentDirectory();
            
            SetupBasicMocks(currentDir);
            _mockProcessExecutor.Execute("npm", "run build:shell", true, currentDir, false).Returns("Build completed");

            // Act
            _command.Execute(options);

            // Assert
            _mockProcessExecutor.Received(1).Execute("npm", "run build:shell", true, currentDir, false);
        }

        [Test]
        public void ExecuteBuildProcess_ShouldNotCallProcessExecutorWhenBuildOptionDisabled()
        {
            // Arrange
            var options = new UpdateShellOptions { Build = false, Environment = "test" };
            var currentDir = Directory.GetCurrentDirectory();
            
            SetupBasicMocks(currentDir);

            // Act
            _command.Execute(options);

            // Assert
            _mockProcessExecutor.DidNotReceive().Execute(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>());
        }

        [Test]
        public void ExecuteBuildProcess_ShouldThrowWhenBuildFails()
        {
            // Arrange
            var options = new UpdateShellOptions { Build = true, Environment = "test" };
            var currentDir = Directory.GetCurrentDirectory();
            
            SetupBasicMocks(currentDir);
            _mockProcessExecutor.When(x => x.Execute("npm", "run build:shell", true, currentDir, false))
                .Do(x => throw new Exception("Build failed"));

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => _command.Execute(options));
            exception.Message.Should().Contain("Build process failed");
        }

        #endregion

        #region Tests: Shell Directory Validation

        [Test]
        public void ValidateShellDirectory_ShouldThrowWhenDirectoryNotExists()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test" };
            var currentDir = Directory.GetCurrentDirectory();
            var packageJsonPath = Path.Combine(currentDir, "package.json");
            var shellDir = Path.Combine(currentDir, "dist", "apps", "studio-enterprise", "shell");

            _mockFileSystem.ExistsFile(packageJsonPath).Returns(true);
            _mockFileSystem.ExistsDirectory(shellDir).Returns(false);

            // Act & Assert
            var exception = Assert.Throws<DirectoryNotFoundException>(() => _command.Execute(options));
            exception.Message.Should().Contain("Shell directory not found");
        }

        [Test]
        public void ValidateShellDirectory_ShouldThrowWhenDirectoryIsEmpty()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test" };
            var currentDir = Directory.GetCurrentDirectory();
            var packageJsonPath = Path.Combine(currentDir, "package.json");
            var shellDir = Path.Combine(currentDir, "dist", "apps", "studio-enterprise", "shell");

            _mockFileSystem.ExistsFile(packageJsonPath).Returns(true);
            _mockFileSystem.ExistsDirectory(shellDir).Returns(true);
            _mockFileSystem.GetFiles(shellDir, "*", SearchOption.AllDirectories).Returns(new string[0]);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => _command.Execute(options));
            exception.Message.Should().Contain("Shell directory is empty");
        }

        #endregion

        #region Tests: MaxFileSize Validation

        [Test]
        public void ValidateMaxFileSize_ShouldUpdateSettingWhenCurrentSizeInsufficient()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test", Force = true };
            var currentDir = Directory.GetCurrentDirectory();
            
            SetupBasicMocks(currentDir);
            
            // Mock small MaxFileSize setting
            _mockSysSettingsManager.GetSysSettingValueByCode("MaxFileSize").Returns("5"); // 5MB, smaller than our test file

            // Act
            _command.Execute(options);

            // Assert
            _mockSysSettingsManager.Received(1).SetSysSettingByCode("MaxFileSize", Arg.Any<string>());
        }

        [Test]
        public void ValidateMaxFileSize_ShouldNotUpdateSettingWhenCurrentSizeSufficient()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test" };
            var currentDir = Directory.GetCurrentDirectory();
            
            SetupBasicMocks(currentDir);
            
            // Mock large MaxFileSize setting
            _mockSysSettingsManager.GetSysSettingValueByCode("MaxFileSize").Returns("50"); // 50MB, larger than our simulated file

            // Act
            _command.Execute(options);

            // Assert
            _mockSysSettingsManager.DidNotReceive().SetSysSettingByCode("MaxFileSize", Arg.Any<string>());
        }

        #endregion

        #region Tests: Dry Run Mode

        [Test]
        public void DryRunMode_ShouldNotUploadFile()
        {
            // Arrange
            var options = new UpdateShellOptions { Environment = "test", DryRun = true };
            var currentDir = Directory.GetCurrentDirectory();
            
            SetupBasicMocks(currentDir);

            // Act
            _command.Execute(options);

            // Assert
            _mockApplicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
        }

        #endregion

        #region Tests: Constructor

        [Test]
        public void Constructor_ShouldThrowArgumentNullException_WhenFileSystemIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UpdateShellCommand(
                _mockApplicationClient,
                _mockEnvironmentSettings,
                null,
                _mockCompressionUtilities,
                _mockProcessExecutor,
                _mockSysSettingsManager,
                _mockServiceUrlBuilder));
        }

        [Test]
        public void Constructor_ShouldThrowArgumentNullException_WhenCompressionUtilitiesIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UpdateShellCommand(
                _mockApplicationClient,
                _mockEnvironmentSettings,
                _mockFileSystem,
                null,
                _mockProcessExecutor,
                _mockSysSettingsManager,
                _mockServiceUrlBuilder));
        }

        [Test]
        public void Constructor_ShouldThrowArgumentNullException_WhenProcessExecutorIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UpdateShellCommand(
                _mockApplicationClient,
                _mockEnvironmentSettings,
                _mockFileSystem,
                _mockCompressionUtilities,
                null,
                _mockSysSettingsManager,
                _mockServiceUrlBuilder));
        }

        [Test]
        public void Constructor_ShouldThrowArgumentNullException_WhenSysSettingsManagerIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UpdateShellCommand(
                _mockApplicationClient,
                _mockEnvironmentSettings,
                _mockFileSystem,
                _mockCompressionUtilities,
                _mockProcessExecutor,
                null,
                _mockServiceUrlBuilder));
        }

        [Test]
        public void Constructor_ShouldThrowArgumentNullException_WhenServiceUrlBuilderIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UpdateShellCommand(
                _mockApplicationClient,
                _mockEnvironmentSettings,
                _mockFileSystem,
                _mockCompressionUtilities,
                _mockProcessExecutor,
                _mockSysSettingsManager,
                null));
        }

        #endregion

        #region Methods: Private

        private void SetupBasicMocks(string currentDir)
        {
            // Mock repository root finding
            var packageJsonPath = Path.Combine(currentDir, "package.json");
            _mockFileSystem.ExistsFile(packageJsonPath).Returns(true);

            // Mock shell directory exists
            var shellDir = Path.Combine(currentDir, "dist", "apps", "studio-enterprise", "shell");
            _mockFileSystem.ExistsDirectory(shellDir).Returns(true);
            _mockFileSystem.GetFiles(shellDir, "*", SearchOption.AllDirectories).Returns(new[] { "test.js", "test.css" });

            // Mock file compression and size
            var tempFile = Path.Combine(Path.GetTempPath(), "shell-test.gz");
            _mockCompressionUtilities.When(x => x.ZipDirectory(shellDir, Arg.Any<string>()))
                .Do(callInfo => { /* Simulate file creation */ });
            
            // Simulate a 12.4MB file
            var fileInfo = Substitute.For<FileInfo>(tempFile);
            fileInfo.Length.Returns(12_582_912); // 12.4MB in bytes

            // Mock system settings
            _mockSysSettingsManager.GetSysSettingValueByCode("MaxFileSize").Returns("50");

            // Mock file system operations
            _mockFileSystem.ReadAllBytes(Arg.Any<string>()).Returns(new byte[1024]);
            _mockFileSystem.ExistsFile(Arg.Any<string>()).Returns(true);

            // Mock ServiceUrlBuilder
            _mockServiceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.UploadStaticFile)
                .Returns("/rest/CreatioApiGateway/UploadStaticFile");

            // Mock successful upload response
            _mockApplicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
                .Returns("{\"success\": true}");
        }

        #endregion
    }

    #region Helper Classes

    // SysSettings is now imported from CreatioModel namespace

    #endregion
}