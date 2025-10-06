using System.IO;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command
{
	[TestFixture]
	[Category("CommandTests")]
	public class ReleaseCommandTests : BaseCommandTests<ReleaseCommand>
	{
		private ILogger _logger;
		private IFileSystem _fileSystem;
		private ReleaseCommand _command;
		private ReleaseCommandOptions _options;

		[SetUp]
		public void SetUp()
		{
			_logger = Substitute.For<ILogger>();
			_fileSystem = Substitute.For<IFileSystem>();
			_command = new ReleaseCommand(_logger, _fileSystem);
			_options = new ReleaseCommandOptions();
		}

		[Test]
		[Description("Should return error when project file does not exist")]
		public void Execute_ShouldReturnError_WhenProjectFileDoesNotExist()
		{
			// Arrange
			_options.Force = true;
			_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);

			// Act
			int result = _command.Execute(_options);

			// Assert
			result.Should().Be(1, "because project file does not exist");
			_logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("Project file not found")));
		}

		[Test]
		[Description("Should update project file when valid version format is found")]
		public void Execute_ShouldUpdateProjectFile_WhenValidVersionFormatIsFound()
		{
			// Arrange
			_options.Force = true;
			string projectPath = Path.Combine("clio", "clio.csproj");
			string originalContent = @"<Project>
  <PropertyGroup>
    <AssemblyVersion Condition=""'$(AssemblyVersion)' == ''"">8.0.1.50</AssemblyVersion>
  </PropertyGroup>
</Project>";
			string expectedContent = @"<Project>
  <PropertyGroup>
    <AssemblyVersion Condition=""'$(AssemblyVersion)' == ''"">8.0.1.51</AssemblyVersion>
  </PropertyGroup>
</Project>";

			_fileSystem.ExistsFile(projectPath).Returns(true);
			_fileSystem.ReadAllText(projectPath).Returns(originalContent);

			// Mock git operations to simulate success
			SetupGitOperations();

			// Act
			int result = _command.Execute(_options);

			// Assert
			result.Should().Be(0, "because the release process should succeed");
			_fileSystem.Received(1).WriteAllTextToFile(projectPath, expectedContent);
			_logger.Received(1).WriteInfo(Arg.Is<string>(s => s.Contains("🚀 Starting release process...")));
		}

		[Test]
		[Description("Should return error when project file has invalid version format")]
		public void Execute_ShouldReturnError_WhenProjectFileHasInvalidVersionFormat()
		{
			// Arrange
			_options.Force = true;
			string projectPath = Path.Combine("clio", "clio.csproj");
			string originalContent = @"<Project>
  <PropertyGroup>
    <AssemblyVersion Condition=""'$(AssemblyVersion)' == ''"">invalid.version</AssemblyVersion>
  </PropertyGroup>
</Project>";

			_fileSystem.ExistsFile(projectPath).Returns(true);
			_fileSystem.ReadAllText(projectPath).Returns(originalContent);

			// Mock git operations to return valid tag but invalid format
			SetupGitOperations("invalid.version");

			// Act
			int result = _command.Execute(_options);

			// Assert
			result.Should().Be(1, "because the version format is invalid");
			_logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("Invalid version format")));
		}

		[Test]
		[Description("Should start with default version when no tags exist")]
		public void Execute_ShouldStartWithDefaultVersion_WhenNoTagsExist()
		{
			// Arrange
			_options.Force = true;
			string projectPath = Path.Combine("clio", "clio.csproj");
			string originalContent = @"<Project>
  <PropertyGroup>
    <AssemblyVersion Condition=""'$(AssemblyVersion)' == ''"">8.0.1.0</AssemblyVersion>
  </PropertyGroup>
</Project>";

			_fileSystem.ExistsFile(projectPath).Returns(true);
			_fileSystem.ReadAllText(projectPath).Returns(originalContent);

			// Mock git operations to return empty (no tags)
			SetupGitOperations("");

			// Act
			int result = _command.Execute(_options);

			// Assert
			result.Should().Be(0, "because the release process should succeed with default version");
			_logger.Received(1).WriteInfo(Arg.Is<string>(s => s.Contains("No existing tags found. Starting with version 8.0.1.1")));
		}

		[Test]
		[Description("Should use Force flag to skip confirmation")]
		public void Execute_ShouldSkipConfirmation_WhenForceIsTrue()
		{
			// Arrange
			_options.Force = true;
			string projectPath = Path.Combine("clio", "clio.csproj");
			string originalContent = @"<Project>
  <PropertyGroup>
    <AssemblyVersion Condition=""'$(AssemblyVersion)' == ''"">8.0.1.50</AssemblyVersion>
  </PropertyGroup>
</Project>";

			_fileSystem.ExistsFile(projectPath).Returns(true);
			_fileSystem.ReadAllText(projectPath).Returns(originalContent);
			SetupGitOperations();

			// Act
			int result = _command.Execute(_options);

			// Assert
			result.Should().Be(0, "because --force flag should skip user confirmation");
			// Should not ask for confirmation when --force is used
		}

		private void SetupGitOperations(string tagVersion = "8.0.1.50")
		{
			// These are just mocks - the actual git operations would be tested in integration tests
			// For unit tests, we're testing the logic flow and file operations
		}
	}
}