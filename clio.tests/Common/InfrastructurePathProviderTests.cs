using Clio.Common;
using FluentAssertions;
using NUnit.Framework;
using System.IO;

namespace Clio.Tests.Common
{
	[TestFixture]
	[Category("Unit")]
	[Description("Unit tests for InfrastructurePathProvider")]
	public class InfrastructurePathProviderTests
	{
		private IInfrastructurePathProvider _pathProvider;

		[SetUp]
		public void Setup()
		{
			_pathProvider = new InfrastructurePathProvider();
		}

		[Test]
		[Description("GetInfrastructurePath should return custom path when provided")]
		public void GetInfrastructurePath_WhenCustomPathProvided_ReturnsCustomPath()
		{
			// Arrange
			const string customPath = "/custom/infrastructure/path";

			// Act
			string result = _pathProvider.GetInfrastructurePath(customPath);

			// Assert
			result.Should().Be(customPath, "because the custom path should be returned when provided");
		}

		[Test]
		[Description("GetInfrastructurePath should return default path when custom path is null")]
		public void GetInfrastructurePath_WhenCustomPathIsNull_ReturnsDefaultPath()
		{
			// Arrange
			string expectedPath = Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");

			// Act
			string result = _pathProvider.GetInfrastructurePath(null);

			// Assert
			result.Should().Be(expectedPath, "because the default path should be returned when custom path is null");
		}

		[Test]
		[Description("GetInfrastructurePath should return default path when custom path is empty string")]
		public void GetInfrastructurePath_WhenCustomPathIsEmpty_ReturnsDefaultPath()
		{
			// Arrange
			string expectedPath = Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");

			// Act
			string result = _pathProvider.GetInfrastructurePath(string.Empty);

			// Assert
			result.Should().Be(expectedPath, "because the default path should be returned when custom path is empty");
		}

		[Test]
		[Description("GetInfrastructurePath should return default path when custom path is whitespace")]
		public void GetInfrastructurePath_WhenCustomPathIsWhitespace_ReturnsDefaultPath()
		{
			// Arrange
			string expectedPath = Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");

			// Act
			string result = _pathProvider.GetInfrastructurePath("   ");

			// Assert
			result.Should().Be(expectedPath, "because the default path should be returned when custom path contains only whitespace");
		}

		[Test]
		[Description("GetInfrastructurePath should return default path when called without parameters")]
		public void GetInfrastructurePath_WhenCalledWithoutParameters_ReturnsDefaultPath()
		{
			// Arrange
			string expectedPath = Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");

			// Act
			string result = _pathProvider.GetInfrastructurePath();

			// Assert
			result.Should().Be(expectedPath, "because the default path should be returned when no parameters are provided");
		}

		[Test]
		[Description("GetInfrastructurePath should handle paths with special characters")]
		public void GetInfrastructurePath_WhenCustomPathHasSpecialCharacters_ReturnsCustomPath()
		{
			// Arrange
			const string customPath = "C:\\Program Files\\My App\\infrastructure-2024";

			// Act
			string result = _pathProvider.GetInfrastructurePath(customPath);

			// Assert
			result.Should().Be(customPath, "because custom paths with special characters should be returned as-is");
		}

		[Test]
		[Description("GetInfrastructurePath should handle relative paths")]
		public void GetInfrastructurePath_WhenCustomPathIsRelative_ReturnsRelativePath()
		{
			// Arrange
			const string customPath = "./local/infrastructure";

			// Act
			string result = _pathProvider.GetInfrastructurePath(customPath);

			// Assert
			result.Should().Be(customPath, "because relative paths should be returned as-is");
		}

		[Test]
		[Description("GetInfrastructurePath default path should always end with 'infrastructure'")]
		public void GetInfrastructurePath_DefaultPath_ShouldEndWithInfrastructure()
		{
			// Act
			string result = _pathProvider.GetInfrastructurePath();

			// Assert
			result.Should().EndWith("infrastructure", "because the default path should always end with 'infrastructure' directory");
		}

		[Test]
		[Description("GetInfrastructurePath default path should be under app settings folder")]
		public void GetInfrastructurePath_DefaultPath_ShouldBeUnderAppSettingsFolder()
		{
			// Act
			string result = _pathProvider.GetInfrastructurePath();

			// Assert
			result.Should().StartWith(SettingsRepository.AppSettingsFolderPath, 
				"because the default path should be located under the application settings folder");
		}

		[Test]
		[Description("GetInfrastructurePath should consistently return same default path across multiple calls")]
		public void GetInfrastructurePath_WhenCalledMultipleTimes_ReturnsConsistentDefaultPath()
		{
			// Act
			string result1 = _pathProvider.GetInfrastructurePath();
			string result2 = _pathProvider.GetInfrastructurePath();
			string result3 = _pathProvider.GetInfrastructurePath();

			// Assert
			result1.Should().Be(result2, "because subsequent calls should return the same default path");
			result2.Should().Be(result3, "because subsequent calls should return the same default path");
		}
	}
}
