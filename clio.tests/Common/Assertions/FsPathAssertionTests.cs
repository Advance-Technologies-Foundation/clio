using System;
using System.IO;
using System.Runtime.InteropServices;
using Clio.Common;
using Clio.Common.Assertions;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Assertions
{
	[TestFixture]
	[Category("Unit")]
	public class FsPathAssertionTests
	{
		private ISettingsRepository _settingsRepository;
		private ILogger _logger;
		private FsPathAssertion _sut;

		[SetUp]
		public void SetUp()
		{
			_settingsRepository = Substitute.For<ISettingsRepository>();
			_logger = Substitute.For<ILogger>();
			_sut = new FsPathAssertion(_settingsRepository, _logger);
		}

		[Test]
		[Description("Should return failure when path parameter is null")]
		public void Execute_WhenPathIsNull_ShouldReturnFailure()
		{
			// Arrange
			string path = null;

			// Act
			var result = _sut.Execute(path);

			// Assert
			result.Status.Should().Be("fail", because: "null path should fail validation");
			result.FailedAt.Should().Be(AssertionPhase.FsPath, because: "failure occurs at path validation phase");
			result.Reason.Should().Contain("required", because: "error message should indicate path is required");
		}

		[Test]
		[Description("Should return failure when path parameter is empty string")]
		public void Execute_WhenPathIsEmpty_ShouldReturnFailure()
		{
			// Arrange
			string path = string.Empty;

			// Act
			var result = _sut.Execute(path);

			// Assert
			result.Status.Should().Be("fail", because: "empty path should fail validation");
			result.FailedAt.Should().Be(AssertionPhase.FsPath, because: "failure occurs at path validation phase");
		}

		[Test]
		[Description("Should return failure when path parameter is whitespace")]
		public void Execute_WhenPathIsWhitespace_ShouldReturnFailure()
		{
			// Arrange
			string path = "   ";

			// Act
			var result = _sut.Execute(path);

			// Assert
			result.Status.Should().Be("fail", because: "whitespace-only path should fail validation");
			result.FailedAt.Should().Be(AssertionPhase.FsPath, because: "failure occurs at path validation phase");
		}

		[Test]
		[Description("Should return failure when directory does not exist")]
		public void Execute_WhenDirectoryDoesNotExist_ShouldReturnFailure()
		{
			// Arrange
			string nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

			// Act
			var result = _sut.Execute(nonExistentPath);

			// Assert
			result.Status.Should().Be("fail", because: "non-existent path should fail validation");
			result.FailedAt.Should().Be(AssertionPhase.FsPath, because: "failure occurs at path validation phase");
			result.Reason.Should().Contain("does not exist", because: "error should indicate path doesn't exist");
			result.Details["resolvedPath"].Should().Be(nonExistentPath, because: "details should include the resolved path");
		}

		[Test]
		[Description("Should return success when directory exists")]
		public void Execute_WhenDirectoryExists_ShouldReturnSuccess()
		{
			// Arrange
			string existingPath = Path.GetTempPath();

			// Act
			var result = _sut.Execute(existingPath);

			// Assert
			result.Status.Should().Be("pass", because: "existing path should pass validation");
			result.Scope.Should().Be(AssertionScope.Fs, because: "scope should be filesystem");
			result.Resolved["path"].Should().Be(existingPath, because: "resolved path should be included in result");
		}

		[Test]
		[Description("Should resolve iis-clio-root-path setting key to configured path")]
		public void Execute_WhenPathIsIisClioRootPathKey_ShouldResolveFromSettings()
		{
			// Arrange
			string settingKey = "iis-clio-root-path";
			string configuredPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
				? @"C:\inetpub\wwwroot\clio" 
				: "/tmp/clio";
			_settingsRepository.GetIISClioRootPath().Returns(configuredPath);

			// Create the directory if it doesn't exist for the test
			if (!Directory.Exists(configuredPath))
			{
				try
				{
					Directory.CreateDirectory(configuredPath);
				}
				catch
				{
					// Skip test if we can't create the directory
					Assert.Inconclusive($"Cannot create test directory: {configuredPath}");
					return;
				}
			}

			try
			{
				// Act
				var result = _sut.Execute(settingKey);

				// Assert
				_settingsRepository.Received(1).GetIISClioRootPath();
				result.Details["requestedPath"].Should().Be(settingKey, 
					because: "details should show the original setting key that was requested");
				result.Resolved["path"].Should().Be(configuredPath, 
					because: "resolved path should be the configured path from settings");
			}
			finally
			{
				// Cleanup
				try
				{
					if (Directory.Exists(configuredPath) && configuredPath.Contains("tmp"))
					{
						Directory.Delete(configuredPath);
					}
				}
				catch
				{
					// Ignore cleanup errors
				}
			}
		}

		[Test]
		[Description("Should be case-insensitive when matching iis-clio-root-path key")]
		public void Execute_WhenPathKeyHasDifferentCase_ShouldStillResolve()
		{
			// Arrange
			string settingKey = "IIS-CLIO-ROOT-PATH"; // Different case
			string configuredPath = Path.GetTempPath();
			_settingsRepository.GetIISClioRootPath().Returns(configuredPath);

			// Act
			var result = _sut.Execute(settingKey);

			// Assert
			_settingsRepository.Received(1).GetIISClioRootPath();
			result.Details["requestedPath"].Should().Be(settingKey, 
				because: "original key with different case should be preserved in details");
		}

		[Test]
		[Description("Should log validation message when checking path")]
		public void Execute_WhenValidatingPath_ShouldLogMessage()
		{
			// Arrange
			string path = Path.GetTempPath();

			// Act
			var result = _sut.Execute(path);

			// Assert
			_logger.Received(1).WriteInfo(Arg.Is<string>(msg => msg.Contains("Validating path")));
		}
	}
}
