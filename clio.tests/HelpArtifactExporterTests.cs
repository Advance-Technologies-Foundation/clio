using System.IO;
using Clio.Help;
using Clio.Tests.Command;
using Clio.Tests.Infrastructure;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class HelpArtifactExporterTests : BaseClioModuleTests {
	private string _helpDirectory;
	private string _repositoryRoot;

	[SetUp]
	public override void Setup() {
		base.Setup();
		_repositoryRoot = TestFileSystem.GetRootedPath("repo");
		_helpDirectory = Path.Combine(_repositoryRoot, "clio", "help", "en");
		FileSystem.AddDirectory(_helpDirectory);
		Parser.Default.Settings.HelpDirectory = _helpDirectory;
	}

	[Test]
	[Description("Leaves existing canonical manual help files untouched while exporting generated docs and indexes.")]
	public void Export_WhenManualHelpExists_DoesNotOverwriteCommandHelpText() {
		string helpPath = Path.Combine(_helpDirectory, "add-item.txt");
		string originalContent = """
NAME
    add-item - Manual help

DETAIL COLLECTIONS
    Preserved manual section.
""";
		FileSystem.AddFile(helpPath, originalContent);
		CommandHelpCatalog catalog = new();
		CommandHelpRenderer renderer = new(FileSystem, catalog, () => false);
		HelpArtifactExporter exporter = new(FileSystem, catalog, renderer);

		int exitCode = exporter.Export(_repositoryRoot);
		string updatedContent = FileSystem.File.ReadAllText(helpPath);

		exitCode.Should().Be(0, because: "help artifact export should succeed for a valid repository root");
		updatedContent.Should().Be(originalContent,
			because: "command help text files are now maintained manually and must not be regenerated");
	}
}
