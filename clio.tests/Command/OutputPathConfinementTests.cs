namespace Clio.Tests.Command;

using System;
using System.IO;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class OutputPathConfinementTests {

	private readonly IoFileSystem _fileSystem = new System.IO.Abstractions.FileSystem();
	private string _sandbox;

	[SetUp]
	public void SetUp() {
		// A real directory under the OS temp root — one of the two zones OutputPathConfinement allows. Using the
		// real file system (not a mock) is required so the symlink and macOS /var->/private/var realpath behavior
		// is exercised as it runs in production.
		_sandbox = Path.Combine(Path.GetTempPath(), "opc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_sandbox);
	}

	[TearDown]
	public void TearDown() {
		try {
			if (Directory.Exists(_sandbox)) {
				Directory.Delete(_sandbox, recursive: true);
			}
		}
		catch (IOException) {
			// Best-effort cleanup; a leftover temp directory must never fail a test.
		}
	}

	[Test]
	[Description("Resolve allows a fresh output-file inside the OS temp directory and returns its resolved absolute path.")]
	public void Resolve_ShouldAllow_FreshPathInsideTempRoot() {
		// Arrange
		string outputFile = Path.Combine(_sandbox, "schema-body.js");

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, outputFile);

		// Assert
		error.Should().BeNull(because: "a fresh path under the OS temp root is inside an allowed zone");
		path.Should().Be(_fileSystem.Path.GetFullPath(outputFile),
			because: "the resolved absolute path is returned for the caller to write");
	}

	[Test]
	[Description("Resolve rejects an output-file that resolves outside the workspace anchor and the OS temp directory, naming the offending option.")]
	public void Resolve_ShouldReject_PathOutsideAllowedZones() {
		// Arrange — an absolute path at the filesystem root, outside both the temp root and any workspace anchor
		string escape = Path.Combine(Path.GetPathRoot(_sandbox)!, "opc-escape-" + Guid.NewGuid().ToString("N") + ".js");

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, escape);

		// Assert
		path.Should().BeNull(because: "a path escaping every allowed zone must not be handed back for writing");
		error.Should().Contain("output-file",
			because: "the error names the offending option so the caller can correct it");
	}

	[Test]
	[Description("Resolve follows a symlink and rejects an output-file whose parent link escapes the allowed zones, rather than trusting the lexical path.")]
	public void Resolve_ShouldReject_SymlinkEscapingAllowedZones() {
		// Arrange — a directory symlink under the sandbox pointing at the filesystem root (outside every allowed zone)
		string linkDir = Path.Combine(_sandbox, "link");
		string root = Path.GetPathRoot(_sandbox)!;
		try {
			Directory.CreateSymbolicLink(linkDir, root);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}
		string throughLink = Path.Combine(linkDir, "opc-escape-" + Guid.NewGuid().ToString("N") + ".js");

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, throughLink);

		// Assert
		path.Should().BeNull(
			because: "the symlink resolves to the filesystem root, which is outside the workspace and temp zones");
		error.Should().Contain("output-file",
			because: "the link escape is reported to the caller rather than silently followed");
	}

	[Test]
	[Description("Resolve follows an INTERMEDIATE (non-terminal) symlink in the path chain and rejects the escape, not just a terminal symlink — a link one level up cannot smuggle the write past the confinement check.")]
	public void Resolve_ShouldReject_IntermediateSymlinkEscape() {
		// Arrange — a directory symlink under the sandbox pointing at the filesystem root; the output-file then
		// descends through an EXISTING directory under the real root, so the symlink is a parent (intermediate)
		// component, not the terminal one.
		string root = Path.GetPathRoot(_sandbox)!;
		string existingRootChild;
		try {
			existingRootChild = Directory.GetDirectories(root)
				.Select(d => Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
				.FirstOrDefault(name => !string.IsNullOrEmpty(name));
		}
		catch (IOException) {
			Assert.Ignore("Filesystem root is not enumerable in this environment.");
			return;
		}
		if (string.IsNullOrEmpty(existingRootChild)) {
			Assert.Ignore("Filesystem root has no enumerable child directory to descend into.");
		}
		string linkDir = Path.Combine(_sandbox, "link");
		try {
			Directory.CreateSymbolicLink(linkDir, root);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}
		string throughIntermediateLink = Path.Combine(linkDir, existingRootChild, "opc-" + Guid.NewGuid().ToString("N") + ".js");

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, throughIntermediateLink);

		// Assert
		path.Should().BeNull(
			because: "the intermediate symlink resolves to the filesystem root, outside the workspace and temp zones");
		error.Should().Contain("output-file",
			because: "a parent-chain symlink escape must be caught the same as a terminal one");
	}

	[Test]
	[Description("Resolve refuses an output-file that already exists and leaves it untouched — an explicit output-file is additive, so the Destructive=false classification of every routing tool stays honest.")]
	public void Resolve_ShouldRefuse_ExistingTarget() {
		// Arrange
		string existing = Path.Combine(_sandbox, "already-there.js");
		File.WriteAllText(existing, "old");

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, existing);

		// Assert
		path.Should().BeNull(because: "an additive-only writer must not silently overwrite an existing file");
		error.Should().Contain("already exists",
			because: "the caller is told the target exists so the Destructive=false classification stays honest");
		File.ReadAllText(existing).Should().Be("old", because: "the existing file is left untouched when the write is refused");
	}

	[Test]
	[Description("IsTrustedAnchor rejects a filesystem root as a confinement boundary because it confines to the whole volume.")]
	public void IsTrustedAnchor_ShouldReject_FilesystemRoot() {
		// Arrange
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string root = Path.GetPathRoot(_sandbox)!;

		// Act
		bool trusted = OutputPathConfinement.IsTrustedAnchor(_fileSystem, root, home);

		// Assert
		trusted.Should().BeFalse(
			because: "a filesystem root would confine to every file on the volume and is too broad to be a write boundary");
	}

	[Test]
	[Description("IsTrustedAnchor rejects an ancestor of the user's home directory (e.g. /Users, C:\\Users) as a confinement boundary.")]
	public void IsTrustedAnchor_ShouldReject_AncestorOfHome() {
		// Arrange
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string ancestorOfHome = Path.GetDirectoryName(
			home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(ancestorOfHome)) {
			Assert.Ignore("The home directory has no parent on this platform.");
		}

		// Act
		bool trusted = OutputPathConfinement.IsTrustedAnchor(_fileSystem, ancestorOfHome, home);

		// Assert
		trusted.Should().BeFalse(
			because: "an ancestor of $HOME confines to every user profile and is too broad to be a write boundary");
	}

	[Test]
	[Description("IsTrustedAnchor accepts an ordinary project directory that is neither a filesystem root nor an ancestor of home.")]
	public void IsTrustedAnchor_ShouldAccept_OrdinaryDirectory() {
		// Arrange
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		// Act
		bool trusted = OutputPathConfinement.IsTrustedAnchor(_fileSystem, _sandbox, home);

		// Assert
		trusted.Should().BeTrue(
			because: "a specific directory that is neither a filesystem root nor an ancestor of home is a valid write boundary");
	}
}
