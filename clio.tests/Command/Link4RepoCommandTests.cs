using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class Link4RepoCommandTests {
	[Test]
	[Description("Treats a relative env package path as a direct filesystem path so link-from-repository works on macOS and Linux without forcing an absolute path.")]
	public void TryResolveDirectoryPath_Should_Return_FullPath_For_Relative_Directory_Path() {
		// Arrange
		MockFileSystem msFileSystem = new(new Dictionary<string, MockFileData>(), "/repo");
		msFileSystem.AddDirectory("/repo/Projects/Creatio/creatio_app/gartner/Terrasoft.Configuration/Pkg");
		Clio.Common.IFileSystem fileSystem = new FileSystem(msFileSystem);

		// Act
		bool result = Link4RepoCommand.TryResolveDirectoryPath(
			"../../Projects/Creatio/creatio_app/gartner/Terrasoft.Configuration/Pkg",
			fileSystem,
			out string resolvedPath);

		// Assert
		result.Should().BeTrue(
			because: "relative env package paths should be handled as direct paths instead of being mistaken for environment names");
		resolvedPath.Should().Be(
			"/Projects/Creatio/creatio_app/gartner/Terrasoft.Configuration/Pkg",
			because: "the command should normalize the relative path before passing it to the linking flow");
	}

	[Test]
	[Description("Leaves plain environment names unresolved so the Windows-only registered-environment flow keeps working as before.")]
	public void TryResolveDirectoryPath_Should_Return_False_For_Plain_Environment_Name() {
		// Arrange
		MockFileSystem msFileSystem = new(new Dictionary<string, MockFileData>(), "/repo");
		Clio.Common.IFileSystem fileSystem = new FileSystem(msFileSystem);

		// Act
		bool result = Link4RepoCommand.TryResolveDirectoryPath("dev", fileSystem, out string resolvedPath);

		// Assert
		result.Should().BeFalse(
			because: "plain environment names should still go through the registered-environment resolution branch");
		resolvedPath.Should().BeNull(
			because: "no filesystem path should be produced for a plain environment name");
	}
}