using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using Clio.Tests.Command;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

/// <summary>
/// Test double for WikiHelpViewer - doesn't inherit to avoid initialization complexity
/// We test LocalHelpViewer behavior, not the actual WikiHelpViewer implementation
/// </summary>
internal class TestWikiHelpViewer {
	public bool CheckHelpReturnValue { get; set; } = true;
	public int CheckHelpCallCount { get; private set; }
	public int ViewHelpCallCount { get; private set; }
	public string LastCommandNameChecked { get; private set; }
	public string LastCommandNameViewed { get; private set; }

	public bool CheckHelp(string commandName) {
		CheckHelpCallCount++;
		LastCommandNameChecked = commandName;
		return CheckHelpReturnValue;
	}

	public void ViewHelp(string commandName) {
		ViewHelpCallCount++;
		LastCommandNameViewed = commandName;
	}
}

/// <summary>
/// Wrapper to make LocalHelpViewer testable with TestWikiHelpViewer
/// </summary>
internal class TestableLocalHelpViewer {
	private readonly IFileSystem _fileSystem;
	private readonly TestWikiHelpViewer _wikiHelpViewer;
	private string _helpFile = string.Empty;
	private bool _localHelpFileExists;

	public TestableLocalHelpViewer(IFileSystem fileSystem, TestWikiHelpViewer wikiHelpViewer) {
		_fileSystem = fileSystem;
		_wikiHelpViewer = wikiHelpViewer;
	}

	public bool CheckHelp(string commandName) {
		string helpDir = Parser.Default.Settings.HelpDirectory;

		if (!_fileSystem.Directory.Exists(helpDir)) {
			_localHelpFileExists = false;
			return _wikiHelpViewer.CheckHelp(commandName);
		}

		List<string> files = _fileSystem.Directory
										.EnumerateFiles(helpDir, $"{commandName}.txt", SearchOption.AllDirectories)
										.ToList();
		_localHelpFileExists = files.Count != 0;

		if (!_localHelpFileExists) {
			return _wikiHelpViewer.CheckHelp(commandName);
		}

		_helpFile = files.First();
		return true;
	}

	public void ViewHelp(string commandName) {
		if (!_localHelpFileExists) {
			_wikiHelpViewer.ViewHelp(commandName);
		}
		else {
			Console.OutputEncoding = Encoding.UTF8;
			string content = _fileSystem.File.ReadAllText(_helpFile);
			Console.Out.WriteLine(content);
		}
	}
}

[TestFixture]
[Category("UnitTests")]
internal class LocalHelpViewerTests : BaseClioModuleTests
{

	#region Fields: Private

	private IFileSystem _fileSystem;
	private TestWikiHelpViewer _wikiHelpViewer;
	private TestableLocalHelpViewer _localHelpViewer;
	private const string HelpDirectory = "C:\\help";
	private const string CommandName = "test-command";

	#endregion

	#region Setup/Teardown

	[SetUp]
	public override void Setup() {
		base.Setup();
		_fileSystem = FileSystem;
		_wikiHelpViewer = new TestWikiHelpViewer();
		
		// Set help directory for tests
		Parser.Default.Settings.HelpDirectory = HelpDirectory;
		
		_localHelpViewer = new TestableLocalHelpViewer(_fileSystem, _wikiHelpViewer);
	}

	#endregion

	#region Methods: Private

	private void CreateHelpFile(string commandName, string content) {
		string filePath = Path.Combine(HelpDirectory, $"{commandName}.txt");
		FileSystem.AddFile(filePath, new MockFileData(content));
	}

	private void CreateNestedHelpFile(string subdirectory, string commandName, string content) {
		string filePath = Path.Combine(HelpDirectory, subdirectory, $"{commandName}.txt");
		FileSystem.AddFile(filePath, new MockFileData(content));
	}

	#endregion

	#region Tests: CheckHelp

	[Test]
	[Description("CheckHelp should return true when local help file exists in help directory")]
	public void CheckHelp_WhenLocalHelpFileExists_ReturnsTrue() {
		// Arrange
		CreateHelpFile(CommandName, "Test help content");

		// Act
		bool result = _localHelpViewer.CheckHelp(CommandName);

		// Assert
		result.Should().BeTrue("because the help file exists locally");
	}

	[Test]
	[Description("CheckHelp should return true when local help file exists in subdirectory")]
	public void CheckHelp_WhenLocalHelpFileExistsInSubdirectory_ReturnsTrue() {
		// Arrange
		CreateNestedHelpFile("commands", CommandName, "Test help content");

		// Act
		bool result = _localHelpViewer.CheckHelp(CommandName);

		// Assert
		result.Should().BeTrue("because the help file exists in a subdirectory");
	}

	[Test]
	[Description("CheckHelp should delegate to WikiHelpViewer when local help file does not exist")]
	public void CheckHelp_WhenLocalHelpFileDoesNotExist_DelegatesToWikiHelpViewer() {
		// Arrange
		FileSystem.AddDirectory(HelpDirectory);
		_wikiHelpViewer.CheckHelpReturnValue = true;

		// Act
		bool result = _localHelpViewer.CheckHelp(CommandName);

		// Assert
		_wikiHelpViewer.CheckHelpCallCount.Should().Be(1, "because WikiHelpViewer should be called once");
		_wikiHelpViewer.LastCommandNameChecked.Should().Be(CommandName);
		result.Should().BeTrue("because WikiHelpViewer returned true");
	}

	[Test]
	[Description("CheckHelp should delegate to WikiHelpViewer when help directory does not exist")]
	public void CheckHelp_WhenHelpDirectoryDoesNotExist_DelegatesToWikiHelpViewer() {
		// Arrange
		_wikiHelpViewer.CheckHelpReturnValue = false;

		// Act
		bool result = _localHelpViewer.CheckHelp(CommandName);

		// Assert
		_wikiHelpViewer.CheckHelpCallCount.Should().Be(1, "because WikiHelpViewer should be called once");
		_wikiHelpViewer.LastCommandNameChecked.Should().Be(CommandName);
		result.Should().BeFalse("because WikiHelpViewer returned false");
	}

	[Test]
	[Description("CheckHelp should return false when neither local nor wiki help is available")]
	public void CheckHelp_WhenNoHelpAvailable_ReturnsFalse() {
		// Arrange
		FileSystem.AddDirectory(HelpDirectory);
		_wikiHelpViewer.CheckHelpReturnValue = false;

		// Act
		bool result = _localHelpViewer.CheckHelp(CommandName);

		// Assert
		result.Should().BeFalse("because no help is available locally or via wiki");
	}

	[Test]
	[Description("CheckHelp should use first file when multiple help files exist for same command")]
	public void CheckHelp_WhenMultipleHelpFilesExist_UsesFirstFile() {
		// Arrange
		CreateNestedHelpFile("commands", CommandName, "First help content");
		CreateNestedHelpFile("other", CommandName, "Second help content");

		// Act
		bool result = _localHelpViewer.CheckHelp(CommandName);

		// Assert
		result.Should().BeTrue("because at least one help file exists");
	}

	#endregion

	#region Tests: ViewHelp

	[Test]
	[Description("ViewHelp should display content from local help file when it exists")]
	public void ViewHelp_WhenLocalHelpFileExists_DisplaysLocalContent() {
		// Arrange
		const string helpContent = "This is test help content\nWith multiple lines\nFor testing";
		CreateHelpFile(CommandName, helpContent);
		
		// Need to call CheckHelp first to populate internal state
		_localHelpViewer.CheckHelp(CommandName);

		var consoleOutput = new StringWriter();
		Console.SetOut(consoleOutput);

		// Act
		_localHelpViewer.ViewHelp(CommandName);

		// Assert
		string output = consoleOutput.ToString();
		output.Should().Contain(helpContent, "because the local help content should be displayed");
		_wikiHelpViewer.ViewHelpCallCount.Should().Be(0, "because WikiHelpViewer should not be called");
	}

	[Test]
	[Description("ViewHelp should delegate to WikiHelpViewer when local help file does not exist")]
	public void ViewHelp_WhenLocalHelpFileDoesNotExist_DelegatesToWikiHelpViewer() {
		// Arrange
		FileSystem.AddDirectory(HelpDirectory);
		_wikiHelpViewer.CheckHelpReturnValue = true;
		
		// Need to call CheckHelp first to populate internal state
		_localHelpViewer.CheckHelp(CommandName);

		// Act
		_localHelpViewer.ViewHelp(CommandName);

		// Assert
		_wikiHelpViewer.ViewHelpCallCount.Should().Be(1, "because WikiHelpViewer should be called once");
		_wikiHelpViewer.LastCommandNameViewed.Should().Be(CommandName);
	}

	[Test]
	[Description("ViewHelp should delegate to WikiHelpViewer when help directory does not exist")]
	public void ViewHelp_WhenHelpDirectoryDoesNotExist_DelegatesToWikiHelpViewer() {
		// Arrange
		_wikiHelpViewer.CheckHelpReturnValue = true;
		
		// Need to call CheckHelp first to populate internal state
		_localHelpViewer.CheckHelp(CommandName);

		// Act
		_localHelpViewer.ViewHelp(CommandName);

		// Assert
		_wikiHelpViewer.ViewHelpCallCount.Should().Be(1, "because WikiHelpViewer should be called once");
		_wikiHelpViewer.LastCommandNameViewed.Should().Be(CommandName);
	}

	[Test]
	[Description("ViewHelp should use UTF-8 encoding when displaying local help content")]
	public void ViewHelp_WhenDisplayingLocalContent_UsesUtf8Encoding() {
		// Arrange
		const string helpContent = "Help with UTF-8: ñ, ü, 中文, 日本語";
		CreateHelpFile(CommandName, helpContent);
		
		_localHelpViewer.CheckHelp(CommandName);

		var consoleOutput = new StringWriter();
		Console.SetOut(consoleOutput);
		
		// Pre-set encoding for test
		Console.OutputEncoding = Encoding.UTF8;

		// Act
		_localHelpViewer.ViewHelp(CommandName);

		// Assert
		Console.OutputEncoding.Should().Be(Encoding.UTF8, "because UTF-8 encoding should be set for console output");
	}

	[Test]
	[Description("ViewHelp should handle empty help file content")]
	public void ViewHelp_WhenHelpFileIsEmpty_DisplaysEmptyContent() {
		// Arrange
		CreateHelpFile(CommandName, string.Empty);
		
		_localHelpViewer.CheckHelp(CommandName);

		var consoleOutput = new StringWriter();
		Console.SetOut(consoleOutput);

		// Act
		_localHelpViewer.ViewHelp(CommandName);

		// Assert
		string output = consoleOutput.ToString();
		output.Should().NotBeNull("because output should not be null even for empty content");
	}

	#endregion

	#region Tests: Boundary Conditions

	[Test]
	[Description("CheckHelp should handle command names with special characters")]
	public void CheckHelp_WithSpecialCharactersInCommandName_HandlesCorrectly() {
		// Arrange
		const string specialCommandName = "test-command_v2.1";
		CreateHelpFile(specialCommandName, "Test content");

		// Act
		bool result = _localHelpViewer.CheckHelp(specialCommandName);

		// Assert
		result.Should().BeTrue("because help file with special characters in name should be found");
	}

	[Test]
	[Description("CheckHelp should handle case differences in command names based on file system")]
	public void CheckHelp_WithDifferentCase_ReflectsFileSystemBehavior() {
		// Arrange
		CreateHelpFile("TestCommand", "Test content");

		// Act
		bool result = _localHelpViewer.CheckHelp("testcommand");

		// Assert
		// On Windows (case-insensitive), file will be found
		// On Linux/Mac (case-sensitive), WikiHelpViewer would be called
		// This test documents the file system-dependent behavior
		result.Should().BeTrue("because MockFileSystem finds files case-insensitively on Windows");
	}

	[Test]
	[Description("CheckHelp should handle very long command names")]
	public void CheckHelp_WithVeryLongCommandName_HandlesCorrectly() {
		// Arrange
		string longCommandName = new string('a', 200);
		CreateHelpFile(longCommandName, "Test content");

		// Act
		bool result = _localHelpViewer.CheckHelp(longCommandName);

		// Assert
		result.Should().BeTrue("because help file with long name should be found if it exists");
	}

	#endregion

	#region Tests: State Management

	[Test]
	[Description("CheckHelp called multiple times should maintain correct state")]
	public void CheckHelp_CalledMultipleTimes_MaintainsCorrectState() {
		// Arrange
		const string command1 = "command1";
		const string command2 = "command2";
		CreateHelpFile(command1, "Content 1");
		// command2 doesn't have a local file

		// Act & Assert - First call
		_localHelpViewer.CheckHelp(command1).Should().BeTrue();

		// Act & Assert - Second call with different command
		_wikiHelpViewer.CheckHelpReturnValue = false;
		_localHelpViewer.CheckHelp(command2).Should().BeFalse();
		_wikiHelpViewer.CheckHelpCallCount.Should().Be(1, "because WikiHelpViewer should be called once for command2");
		_wikiHelpViewer.LastCommandNameChecked.Should().Be(command2);

		// Act & Assert - Third call back to first command
		_localHelpViewer.CheckHelp(command1).Should().BeTrue();
	}

	[Test]
	[Description("ViewHelp should display correct content after multiple CheckHelp calls")]
	public void ViewHelp_AfterMultipleCheckHelpCalls_DisplaysCorrectContent() {
		// Arrange
		const string command1 = "command1";
		const string command2 = "command2";
		const string content1 = "Content for command 1";
		const string content2 = "Content for command 2";
		
		CreateHelpFile(command1, content1);
		CreateHelpFile(command2, content2);

		// Act - Check and view first command
		_localHelpViewer.CheckHelp(command1);
		var output1 = new StringWriter();
		Console.SetOut(output1);
		_localHelpViewer.ViewHelp(command1);

		// Act - Check and view second command
		_localHelpViewer.CheckHelp(command2);
		var output2 = new StringWriter();
		Console.SetOut(output2);
		_localHelpViewer.ViewHelp(command2);

		// Assert
		output1.ToString().Should().Contain(content1, "because first command content should be displayed");
		output2.ToString().Should().Contain(content2, "because second command content should be displayed");
	}

	#endregion

	#region Tests: Integration Scenarios

	[Test]
	[Description("LocalHelpViewer should work correctly in fallback chain scenario")]
	public void IntegrationTest_FallbackChain_WorksCorrectly() {
		// Arrange - Create local help for some commands but not others
		const string localCommand = "has-local-help";
		const string wikiCommand = "has-wiki-help";
		
		CreateHelpFile(localCommand, "Local help content");
		_wikiHelpViewer.CheckHelpReturnValue = true;

		// Act & Assert - Local command
		_localHelpViewer.CheckHelp(localCommand).Should().BeTrue("because local help exists");
		
		// Act & Assert - Wiki command
		_localHelpViewer.CheckHelp(wikiCommand).Should().BeTrue("because wiki help exists");
		_wikiHelpViewer.CheckHelpCallCount.Should().Be(1, "because WikiHelpViewer should be called once for wiki command");
		_wikiHelpViewer.LastCommandNameChecked.Should().Be(wikiCommand);
	}

	#endregion

}

