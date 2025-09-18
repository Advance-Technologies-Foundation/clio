using Castle.Core.Logging;
using Clio.Common;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Terrasoft.Common;
using ConsoleLogger = Clio.Common.ConsoleLogger;

namespace Clio.Tests;

[TestFixture]
internal class WorkingDirectoryProviderTest
{
	[Test]
	[Description("Verifies that GetTemplatePath returns the correct .tpl file path when the template file exists")]
	public void GetTemplatePath_TemplateName_ReturnsTemplatePath() {
		// Arrange
		var logger = ConsoleLogger.Instance; 
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);
		var templateName = "TestTemplate";
		var expectedPath = Path.Combine(provider.TemplateDirectory, $"{templateName}.tpl");

		// Add the template file to the mock file system
		mockFileSystem.AddFile(expectedPath, new MockFileData("dummy content"));

		// Act
		var actualPath = provider.GetTemplatePath(templateName);

		// Assert
		expectedPath.Should().Be(actualPath);
	}

	[Test]
	[Description("Ensures GenerateTempDirectoryPath produces unique paths when called concurrently and uses proper GUID format without dashes")]
	public void GenerateTempDirectoryPath_MultipleCallsInParallel_ReturnsUniqueValues() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var provider = new WorkingDirectoriesProvider(logger, new MockFileSystem());
		ConcurrentBag<string> paths = [];

		// Act
		var repeatCount = 100;
		Parallel.ForEach(Enumerable.Range(0, repeatCount), i => {
			paths.Add(provider.GenerateTempDirectoryPath());
		});

		var uniqueCount = paths.Distinct().Count();
		var dashContainsCount = paths.Count(p => p.Contains("-"));

		// Assert
		paths.Should().HaveCount(repeatCount, "because we called the method {0} times", repeatCount);
		uniqueCount.Should().Be(repeatCount, "because all generated paths should be unique");
		dashContainsCount.Should().Be(0, "because GUIDs should not contain dashes in N format");
	}

	[Test]
	[Description("Tests fallback behavior when .tpl file doesn't exist - should return path without .tpl extension")]
	public void GetTemplatePath_TemplateName_ReturnsPathWithoutTplExtension_WhenTplFileDoesNotExist() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);
		var templateName = "TestTemplate.exe";
		var expectedPath = Path.Combine(provider.TemplateDirectory, templateName);

		// Add only the file without .tpl extension
		mockFileSystem.AddFile(expectedPath, new MockFileData("dummy content"));

		// Act
		var actualPath = provider.GetTemplatePath(templateName);

		// Assert
		actualPath.Should().Be(expectedPath, "because only the file without .tpl extension exists");
	}

	[Test]
	[Description("Validates that null template name parameter throws ArgumentException for proper input validation")]
	public void GetTemplatePath_NullTemplateName_ThrowsArgumentException() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);

		// Act & Assert
		Action act = () => provider.GetTemplatePath(null);
		act.Should().Throw<ArgumentException>("because null template name is invalid");
	}

	[Test]
	[Description("Validates that empty string template name parameter throws ArgumentException for proper input validation")]
	public void GetTemplatePath_EmptyTemplateName_ThrowsArgumentException() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);

		// Act & Assert
		Action act = () => provider.GetTemplatePath(string.Empty);
		act.Should().Throw<ArgumentException>("because empty template name is invalid");
	}

	[Test]
	[Description("Validates that whitespace-only template name parameter throws ArgumentException for proper input validation")]
	public void GetTemplatePath_WhitespaceTemplateName_ThrowsArgumentException() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);

		// Act & Assert
		Action act = () => provider.GetTemplatePath("   ");
		act.Should().Throw<ArgumentException>("because whitespace-only template name is invalid");
	}

	[Test]
	[Description("Tests precedence logic when both .tpl and non-.tpl files exist - should prefer .tpl file")]
	public void GetTemplatePath_BothTplAndNonTplExist_PrefersTplFile() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);
		var templateName = "TestTemplate";
		var tplPath = Path.Combine(provider.TemplateDirectory, $"{templateName}.tpl");
		var nonTplPath = Path.Combine(provider.TemplateDirectory, templateName);

		// Add both files to mock file system
		mockFileSystem.AddFile(tplPath, new MockFileData("tpl content"));
		mockFileSystem.AddFile(nonTplPath, new MockFileData("non-tpl content"));

		// Act
		var actualPath = provider.GetTemplatePath(templateName);

		// Assert
		actualPath.Should().Be(tplPath, "because .tpl file should take precedence when both exist");
	}

	[Test]
	[Description("Ensures template names containing path separators (like workspace/tasks/script.cmd) are handled correctly")]
	public void GetTemplatePath_TemplateNameWithPathSeparators_HandlesCorrectly() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);
		var templateName = Path.Combine("workspace", "tasks", "script.cmd");
		var expectedTplPath = Path.Combine(provider.TemplateDirectory, $"{templateName}.tpl");

		mockFileSystem.AddFile(expectedTplPath, new MockFileData("script content"));

		// Act
		var actualPath = provider.GetTemplatePath(templateName);

		// Assert
		actualPath.Should().Be(expectedTplPath, "because template name with path separators should be handled correctly");
	}

	[Test]
	[Description("Tests default behavior when neither .tpl nor non-.tpl file exists - should return non-.tpl path")]
	public void GetTemplatePath_NeitherFileExists_ReturnsNonTplPath() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);
		var templateName = "NonExistentTemplate";
		var expectedPath = Path.Combine(provider.TemplateDirectory, templateName);

		// Act
		var actualPath = provider.GetTemplatePath(templateName);

		// Assert
		actualPath.Should().Be(expectedPath, "because it should return the non-.tpl path when neither file exists");
	}

	[Test]
	[Description("Verifies that template names with special characters (dashes, underscores, dots) are processed correctly")]
	public void GetTemplatePath_TemplateNameWithSpecialCharacters_HandlesCorrectly() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);
		var templateName = "Template-With_Special.Characters";
		var expectedTplPath = Path.Combine(provider.TemplateDirectory, $"{templateName}.tpl");

		mockFileSystem.AddFile(expectedTplPath, new MockFileData("special content"));

		// Act
		var actualPath = provider.GetTemplatePath(templateName);

		// Assert
		actualPath.Should().Be(expectedTplPath, "because template names with special characters should be handled correctly");
	}

	[Test]
	[Description("Tests scenario where only the non-.tpl version of the file exists - should return that path")]
	public void GetTemplatePath_OnlyNonTplFileExists_ReturnsNonTplPath() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var mockFileSystem = new MockFileSystem();
		var provider = new WorkingDirectoriesProvider(logger, mockFileSystem);
		var templateName = "OnlyNonTpl";
		var nonTplPath = Path.Combine(provider.TemplateDirectory, templateName);

		// Add only the non-.tpl file
		mockFileSystem.AddFile(nonTplPath, new MockFileData("non-tpl content"));

		// Act
		var actualPath = provider.GetTemplatePath(templateName);

		// Assert
		actualPath.Should().Be(nonTplPath, "because only the non-.tpl file exists");
	}
}