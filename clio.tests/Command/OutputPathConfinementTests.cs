namespace Clio.Tests.Command;

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

// The categories are per TEST, not per fixture: the confinement PREDICATES are pure and belong in the fast
// unit lane, while every case that creates real host files, symlinks or inspects Unix modes is Integration.
// Selecting the filesystem cases into the fast lane made a ~40-case unit filter pay for real disk I/O, and
// the dedicated Unix file-mode workflow now selects the integration cases explicitly instead.
[TestFixture]
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
	[Category("Integration")]
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
	[Category("Integration")]
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
	[Category("Integration")]
	[Description("ResolveForRead returns the CANONICAL path, so the caller opens the same file confinement approved instead of a lexical path whose components can still be swapped.")]
	public void ResolveForRead_ShouldReturn_TheCanonicalPath() {
		// Arrange — an in-bounds file reached through an in-bounds directory symlink. Both the link and its
		// target stay inside the sandbox, so the path is allowed either way; what is under test is WHICH form
		// comes back, because that is the path the caller then opens.
		string realDirectory = Path.Combine(_sandbox, "real");
		Directory.CreateDirectory(realDirectory);
		string payload = Path.Combine(realDirectory, "payload.json");
		File.WriteAllText(payload, "{}");
		string linkDirectory = Path.Combine(_sandbox, "link");
		try {
			// A RELATIVE target, so the link resolves against its own already-canonicalized directory. An
			// absolute target would be taken verbatim, and on macOS the sandbox path (/var/...) and its
			// canonical form (/private/var/...) then disagree - which is a separate, pre-existing quirk of
			// link-target resolution and not what this test is about.
			Directory.CreateSymbolicLink(linkDirectory, "real");
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}
		string throughLink = Path.Combine(linkDirectory, "payload.json");

		// Act
		(string path, string error) = OutputPathConfinement.ResolveForRead(_fileSystem, throughLink, "rows-file");

		// Assert
		error.Should().BeNull(because: "the file is inside the sandbox under the OS temp root");
		path.Should().NotBe(Path.GetFullPath(throughLink),
			because: "returning the lexical path let the check run on one file and the open land on another");
		path.Should().NotContain($"{Path.DirectorySeparatorChar}link{Path.DirectorySeparatorChar}",
			because: "the symlink component must be resolved away before the caller opens the path");
		File.ReadAllText(path!).Should().Be("{}",
			because: "the canonical path must still name the same file the caller asked for");
	}

	[Test]
	[Category("Integration")]
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
	[Category("Integration")]
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
	[Category("Integration")]
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
	[Category("Unit")]
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
	[Category("Unit")]
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
	[Category("Unit")]
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

	[Test]
	[Category("Integration")]
	[Description("Resolve follows a DANGLING terminal symlink (target not yet created) and rejects it when the target escapes the allowed zones — File.Exists/Directory.Exists report false for such a link, so it must not be trusted as an ordinary lexical tail segment.")]
	public void Resolve_ShouldReject_DanglingTerminalSymlinkEscape() {
		// Arrange — a file symlink under the sandbox whose target does NOT exist and lies at the filesystem root
		// (outside every allowed zone). The write would follow the link at the OS level and land on the target.
		string root = Path.GetPathRoot(_sandbox)!;
		string danglingTarget = Path.Combine(root, "opc-dangling-" + Guid.NewGuid().ToString("N"));
		string link = Path.Combine(_sandbox, "dead");
		try {
			File.CreateSymbolicLink(link, danglingTarget);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, link);

		// Assert
		path.Should().BeNull(
			because: "a dangling symlink whose target escapes the allowed zones must be rejected, not written through");
		error.Should().Contain("output-file",
			because: "the dangling-link escape is reported rather than silently followed by the write");
	}

	[Test]
	[Category("Integration")]
	[Description("Resolve follows a DANGLING intermediate symlink (a parent component whose target does not exist) and rejects the escape — the write's directory creation would otherwise follow it out of the allowed zones.")]
	public void Resolve_ShouldReject_DanglingIntermediateSymlinkEscape() {
		// Arrange — a directory symlink under the sandbox pointing at a non-existent directory at the filesystem
		// root; the output-file then descends through it, so the dangling link is an intermediate component.
		string root = Path.GetPathRoot(_sandbox)!;
		string danglingTarget = Path.Combine(root, "opc-dangling-dir-" + Guid.NewGuid().ToString("N"));
		string link = Path.Combine(_sandbox, "deadlink");
		try {
			Directory.CreateSymbolicLink(link, danglingTarget);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}
		string throughLink = Path.Combine(link, "opc-" + Guid.NewGuid().ToString("N") + ".js");

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, throughLink);

		// Assert
		path.Should().BeNull(
			because: "a dangling intermediate symlink resolving outside the allowed zones must be rejected");
		error.Should().Contain("output-file",
			because: "a dangling parent-chain symlink escape is caught the same as an existing-target one");
	}

	[Test]
	[Category("Integration")]
	[Description("A dangling symlink whose not-yet-created target stays inside an allowed zone is never resolved to an out-of-bounds write: it is either refused (the link node already occupies the path) or its resolved path stays inside the sandbox. File.Exists reports a dangling link differently on Windows vs POSIX, so only the cross-OS no-escape invariant is asserted (the hardening must not redirect an in-bounds link out of bounds).")]
	public void Resolve_ShouldNeverEscape_ForDanglingSymlinkTargetInsideAllowedZone() {
		// Arrange — a file symlink under the sandbox whose (absent) target is also under the sandbox
		string insideTarget = Path.Combine(_sandbox, "opc-inside-" + Guid.NewGuid().ToString("N") + ".js");
		string link = Path.Combine(_sandbox, "inlink");
		try {
			File.CreateSymbolicLink(link, insideTarget);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, link);

		// Assert
		if (error == null) {
			Path.GetFullPath(path).Should().StartWith(_sandbox,
				because: "an allowed in-bounds link must never resolve to a path outside the sandbox");
		}
		else {
			path.Should().BeNull(
				because: "a refusal (the dangling link node already occupies the path) must not hand back a path to write");
		}
	}

	[Test]
	[Category("Integration")]
	[Description("A write that fails part-way leaves neither the final file nor the sibling temporary file behind, and the same path can be written again - the no-overwrite guard must not be left refusing every retry against a half-written file.")]
	public void WriteAtomic_ShouldLeaveNoFileAndAllowRetry_WhenTheWriteFailsPartWay() {
		// Arrange
		string outputFile = Path.Combine(_sandbox, "nested", "odata-response.json");
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, outputFile);
		error.Should().BeNull(because: "a fresh nested path under the sandbox is inside an allowed zone");

		// Act - a payload that dies after some bytes have already reached the stream, the way a full disk does
		Action failingWrite = () => OutputPathConfinement.WriteAtomic(_fileSystem, path, stream => {
			stream.Write("{\"value\":[{\"Id\":\"partial\""u8);
			stream.Flush();
			throw new IOException("There is not enough space on the disk.");
		});

		// Assert
		failingWrite.Should().Throw<IOException>(
			because: "the caller has to learn the write failed rather than believe a truncated file is the response");
		File.Exists(outputFile).Should().BeFalse(
			because: "the content is completed in a sibling temporary file and only then renamed, so a failed write never reaches the final name");
		Directory.GetFiles(Path.GetDirectoryName(outputFile)!, "*.tmp").Should().BeEmpty(
			because: "the temporary file is removed on every failure path, so a failed write leaves nothing at all - not even a half-written sibling");

		// Act - the retry the stale-wreckage bug used to block
		OutputPathConfinement.WriteAtomic(_fileSystem, path, "{\"value\":[]}");

		// Assert
		File.ReadAllText(outputFile).Should().Be("{\"value\":[]}",
			because: "nothing was left occupying the name, so the next attempt writes the complete payload");
	}

	[Test]
	[Category("Integration")]
	[Description("WriteAtomic creates the parent directory and writes the content to a fresh confined path.")]
	public void WriteAtomic_ShouldCreateParentAndWrite_FreshPath() {
		// Arrange
		string outputFile = Path.Combine(_sandbox, "nested", "schema-body.js");
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, outputFile);
		error.Should().BeNull(because: "a fresh nested path under the sandbox is inside an allowed zone");

		// Act
		OutputPathConfinement.WriteAtomic(_fileSystem, path, "content");

		// Assert
		File.ReadAllText(outputFile).Should().Be("content",
			because: "WriteAtomic writes the content to the resolved path, creating the parent directory");
	}

	[Test]
	[Category("Integration")]
	[Description("On Unix, WriteAtomic creates the output under the SHARED temp root with owner-only permissions (0600), so a raw service response is not left readable by other local users of that root. Skipped on Windows, where the mode has no meaning; the Unix File Mode Tests job in build.yml runs this fixture on ubuntu-latest so the guarantee is gated in CI.")]
	public void WriteAtomic_ShouldCreateOwnerOnlyFile_UnderSharedTempRoot() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Ignore("Unix file modes do not apply on Windows.");
		}

		// Arrange — the OS temp root is shared between local users, which is what makes the mode matter
		string outputFile = Path.Combine(_sandbox, "odata-response.json");
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, outputFile);
		error.Should().BeNull(because: "a fresh path under the temp-root sandbox is inside an allowed zone");

		// Act
		OutputPathConfinement.WriteAtomic(_fileSystem, path, "{\"value\":[]}");

		// Assert
		File.GetUnixFileMode(outputFile).Should().Be(OutputPathConfinement.OwnerOnlyFile,
			because: "the payload is a raw service response written into a shared root, so no group or other bit "
				+ "may be set - and the final name inherits the mode because the move renames the same inode");
	}

	[Test]
	[Category("Integration")]
	[Description("On Unix the TEMPORARY file is already owner-only while its stream is open, which is what distinguishes a creation-time 0600 from an unsafe create-then-chmod: the latter leaves a window in which the raw service response is world-readable under the shared temp root. Gated in CI by the Unix File Mode Tests job.")]
	public void WriteAtomic_ShouldCreateTheTemporaryFileOwnerOnly_WhileItsStreamIsOpen() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Ignore("Unix file modes do not apply on Windows.");
		}

		// Arrange
		string outputFile = Path.Combine(_sandbox, "odata-response.json");
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, outputFile);
		error.Should().BeNull(because: "a fresh path under the temp-root sandbox is inside an allowed zone");
		UnixFileMode? temporaryMode = null;
		string temporaryName = null;

		// Act — the assertion has to happen INSIDE the write, while the sibling temporary file still exists
		OutputPathConfinement.WriteAtomic(_fileSystem, path, stream => {
			string[] temporaries = Directory.GetFiles(_sandbox, "*.tmp");
			temporaries.Should().HaveCount(1,
				because: "the payload is completed in one sibling temporary file before it is renamed");
			temporaryName = temporaries[0];
			temporaryMode = File.GetUnixFileMode(temporaryName);
			stream.Write("{\"value\":[]}"u8);
		});

		// Assert
		temporaryMode.Should().Be(OutputPathConfinement.OwnerOnlyFile,
			because: "the mode must be set when the file is CREATED - a chmod afterwards leaves a window in "
				+ "which a raw service response is readable by every other local user of the shared temp root");
		File.Exists(temporaryName).Should().BeFalse(
			because: "the temporary file is renamed onto the final name, not left behind");
		File.GetUnixFileMode(outputFile).Should().Be(OutputPathConfinement.OwnerOnlyFile,
			because: "the move renames the same inode, so the final name keeps the mode it was created with");
	}

	[Test]
	[Category("Integration")]
	[Description("Resolve fails CLOSED on a symlink CYCLE: the resolution throws UnresolvableLinkException and Resolve refuses with the specific 'unresolvable symbolic link' message, rather than degrading to the lexical path. Locks in the fail-closed branch and its ordering above the broad lexical-fallback catch.")]
	public void Resolve_ShouldFailClosed_OnSymlinkCycle() {
		// Arrange — a two-node symlink cycle under the sandbox (a -> b, b -> a)
		string a = Path.Combine(_sandbox, "cycle-a");
		string b = Path.Combine(_sandbox, "cycle-b");
		try {
			File.CreateSymbolicLink(a, b);
			File.CreateSymbolicLink(b, a);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}

		// Act
		(string path, string error) = OutputPathConfinement.Resolve(_fileSystem, a);

		// Assert
		path.Should().BeNull(because: "a symlink cycle cannot be resolved, so no path may be handed back for writing");
		error.Should().Contain("unresolvable symbolic link",
			because: "the cycle must fail CLOSED via the specific branch, not degrade to the lexical fallback");
	}

	[Test]
	[Category("Integration")]
	[Description("WriteAtomic refuses to overwrite a target that appears after Resolve (FileMode.CreateNew is the atomic gate), keeping the additive Destructive=false contract honest against a resolve->write race.")]
	public void WriteAtomic_ShouldRefuse_TargetThatAppearedAfterResolve() {
		// Arrange — Resolve confirms the path is allowed while it does not exist; the file then appears (a racing
		// writer / a target created between the check and the write).
		string outputFile = Path.Combine(_sandbox, "raced.js");
		(string path, string _) = OutputPathConfinement.Resolve(_fileSystem, outputFile);
		File.WriteAllText(outputFile, "planted");

		// Act
		Action write = () => OutputPathConfinement.WriteAtomic(_fileSystem, path, "new");

		// Assert
		write.Should().Throw<IOException>().WithMessage("*already exists*",
			because: "CreateNew fails atomically when the target exists, so no overwrite occurs");
		File.ReadAllText(outputFile).Should().Be("planted",
			because: "the pre-existing file is left untouched when the atomic create is refused");
		Directory.GetFiles(_sandbox, "*.tmp").Should().BeEmpty(
			because: "the raw response was already written to the sibling temporary file before the move was "
				+ "refused; leaving it behind would leak the whole response body next to the target under a "
				+ "name nobody cleans up");
	}
}
