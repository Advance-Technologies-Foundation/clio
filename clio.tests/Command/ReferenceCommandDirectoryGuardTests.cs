using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Common;
using Clio.Project;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers the directory-rejection guard of <see cref="ReferenceCommand"/>.
/// </summary>
/// <remarks>
/// A SEPARATE fixture on purpose: the pre-existing <see cref="ReferenceCommandTestCase"/> is disabled at the
/// fixture level with <c>[Ignore("Not passing in github runner")]</c>, so a case added there would not run and
/// the guard - a user-facing message and an early <c>return 1</c> - could regress unnoticed, for instance
/// through an inverted condition. These cases need no real file system: the command takes an
/// <c>IFileSystem</c>, so a MockFileSystem is enough.
/// </remarks>
[TestFixture]
[Property("Module", "Command")]
public sealed class ReferenceCommandDirectoryGuardTests {

	[Test]
	[Category("Unit")]
	[Description("Refuses a path that names a directory, before the project loader is reached, so the caller is told to pass the .csproj instead of seeing an access-denied error.")]
	public void Execute_ShouldRefuseADirectory_BeforeLoadingTheProject() {
		// Arrange
		MockFileSystem fileSystem = new();
		string directory = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "SomePackage");
		fileSystem.AddDirectory(directory);
		ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
		ILogger logger = Substitute.For<ILogger>();
		ReferenceCommand command = new(creator, logger, fileSystem);

		// Act
		int result = command.Execute(new ReferenceOptions { Path = directory, ReferenceType = "src" });

		// Assert
		result.Should().Be(1, because: "a directory is not a package project file and the command cannot proceed");
		creator.DidNotReceiveWithAnyArgs().CreateFromFile(default);
		// because: XElement.Load on a directory raises UnauthorizedAccessException, which the user reads as a
		// permission problem rather than as the wrong kind of path
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("is a directory") && message.Contains(".csproj")));
		// because: the message has to name both what was wrong and what to pass instead
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves an ordinary project-file path to the project loader, so the guard rejects directories only and not every path.")]
	public void Execute_ShouldReachTheProjectLoader_WhenThePathIsAFile() {
		// Arrange - the companion to the case above: without it an inverted condition would still pass, since
		// rejecting every path also satisfies "a directory is rejected". Only the hand-off to the loader is
		// asserted, not the exit code: what happens after the hand-off belongs to the reference-rewriting
		// cases, and ICreatioPkgProject's fluent members return the concrete type, so a substituted project
		// cannot stand in for the rest of the pipeline.
		MockFileSystem fileSystem = new();
		string projectFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "SomePackage.csproj");
		fileSystem.AddFile(projectFile, new MockFileData("<Project />"));
		ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
		ILogger logger = Substitute.For<ILogger>();
		ReferenceCommand command = new(creator, logger, fileSystem);

		// Act
		command.Execute(new ReferenceOptions { Path = projectFile, ReferenceType = "src" });

		// Assert
		creator.Received(1).CreateFromFile(projectFile);
		// because: the guard must not stand between an ordinary project file and the loader
		logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("is a directory")));
		// because: a file path must never be reported as a directory
	}
}
