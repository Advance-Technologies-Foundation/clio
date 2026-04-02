using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Common;

[TestFixture]
public class TemplateProviderTests {

	[Test]
	[Description("CopyTemplateFolderIfMissing should add missing workspace template files without overwriting files that already exist in the destination directory.")]
	public void CopyTemplateFolderIfMissing_Should_Preserve_Existing_Files() {
		// Arrange
		const string executingDirectory = "/clio";
		WorkingDirectoriesProvider._executingDirectory = executingDirectory;
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData> {
			[$"{executingDirectory}/tpl/workspace/gitignore.txt"] = new("template-gitignore"),
			[$"{executingDirectory}/tpl/workspace/tasks/open-solution.cmd"] = new("template-task"),
			["/repo/gitignore.txt"] = new("custom-gitignore"),
			["/repo/README.md"] = new("existing-readme")
		}, "/repo");
		IWorkingDirectoriesProvider workingDirectoriesProvider = new WorkingDirectoriesProvider(
			Substitute.For<ILogger>(),
			mockFileSystem);
		ITemplateProvider templateProvider = new TemplateProvider(
			workingDirectoriesProvider,
			new FileSystem(mockFileSystem));

		// Act
		templateProvider.CopyTemplateFolderIfMissing("workspace", "/repo");

		// Assert
		mockFileSystem.File.ReadAllText("/repo/gitignore.txt").Should().Be("custom-gitignore",
			because: "safe workspace initialization must not overwrite existing files in the destination directory");
		mockFileSystem.File.ReadAllText("/repo/tasks/open-solution.cmd").Should().Be("template-task",
			because: "safe workspace initialization should still create missing template files");
		mockFileSystem.File.ReadAllText("/repo/README.md").Should().Be("existing-readme",
			because: "unrelated user files in the directory must remain untouched");
	}
}
