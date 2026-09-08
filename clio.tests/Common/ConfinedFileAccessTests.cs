namespace Clio.Tests.Common;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

/// <summary>
/// Real-file-system coverage for <see cref="ConfinedFileAccess"/>.
/// </summary>
/// <remarks>
/// Every case here needs actual symbolic links and actual directory handles, so all of them are
/// <c>Integration</c>: a mock file system has neither, and a unit-lane version of these tests would pass
/// while proving nothing. Two of them exist specifically because the guarantee they check fails SILENTLY
/// when it regresses - the open simply succeeds without no-follow semantics and nothing looks wrong.
/// </remarks>
[TestFixture]
[Property("Module", "Common")]
public sealed class ConfinedFileAccessTests {

	private const long TestCeiling = 10L * 1024 * 1024;

	private readonly IConfinedFileAccess _access = new ConfinedFileAccess();
	private string _sandbox;

	[SetUp]
	public void SetUp() {
		string requested = Path.Combine(Path.GetTempPath(), "cfa-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(requested);
		// The descent refuses a symlinked component BY DESIGN, and the OS temp root is itself reached through
		// one on macOS (/var -> /private/var). Production never hands it a raw path either: confinement
		// resolves the canonical form first. The sandbox is canonicalized here for the same reason.
		_sandbox = CanonicalDirectory(requested);
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
	[Description("Reads an ordinary confined file through the handle-bound descent, so the guard does not break the normal path.")]
	public void OpenRead_ShouldReturn_TheFileContent_ForAnOrdinaryPath() {
		// Arrange
		string path = Path.Combine(_sandbox, "payload.json");
		File.WriteAllText(path, "{\"ok\":true}");

		// Act
		using Stream stream = _access.OpenRead(path, TestCeiling);
		using StreamReader reader = new(stream, Encoding.UTF8);
		string content = reader.ReadToEnd();

		// Assert
		content.Should().Be("{\"ok\":true}",
			because: "the descent must still deliver the bytes of the file it was asked for");
	}

	[Test]
	[Category("Integration")]
	[Description("Refuses to read through a directory that was replaced by a symbolic link AFTER its path was approved - the swap the pathname-based check could not see.")]
	public void OpenRead_ShouldRefuse_AParentDirectoryReplacedByALinkAfterApproval() {
		// Arrange - an approved file, and an out-of-reach file the swapped parent will point at.
		string approvedDirectory = Path.Combine(_sandbox, "approved");
		Directory.CreateDirectory(approvedDirectory);
		string approvedFile = Path.Combine(approvedDirectory, "payload.json");
		File.WriteAllText(approvedFile, "{\"approved\":true}");
		string secretDirectory = Path.Combine(_sandbox, "secret");
		Directory.CreateDirectory(secretDirectory);
		File.WriteAllText(Path.Combine(secretDirectory, "payload.json"), "{\"secret\":true}");
		string canonical = approvedFile;

		// Act - the swap happens after the path is approved and before it is opened.
		Directory.Delete(approvedDirectory, recursive: true);
		try {
			Directory.CreateSymbolicLink(approvedDirectory, "secret");
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}
		Action read = () => {
			using Stream stream = _access.OpenRead(canonical, TestCeiling);
			using StreamReader reader = new(stream, Encoding.UTF8);
			reader.ReadToEnd().Should().NotContain("secret",
				because: "if the read is allowed at all it must land on the approved file, never on the swap target");
		};

		// Assert
		read.Should().Throw<IOException>(
			because: "a component that became a symbolic link after approval means the path is no longer the "
				+ "one that was checked, so the read must fail closed rather than follow it");
	}

	[Test]
	[Category("Integration")]
	[Description("Refuses to read a file whose FINAL component is a symbolic link, which is the direct behavioural proof that no-follow semantics are actually in effect on this platform.")]
	public void OpenRead_ShouldRefuse_ALinkedFinalComponent() {
		// Arrange
		string target = Path.Combine(_sandbox, "target.json");
		File.WriteAllText(target, "{\"target\":true}");
		string link = Path.Combine(_sandbox, "link.json");
		try {
			File.CreateSymbolicLink(link, "target.json");
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}

		// Act
		Action read = () => _access.OpenRead(Path.Combine(_sandbox, "link.json"), TestCeiling).Dispose();

		// Assert
		// The platform flag values this relies on (O_NOFOLLOW / the reparse-point attribute) fail silently in
		// the DANGEROUS direction when they are wrong - the open just succeeds - so this assertion is the only
		// thing standing between a bad constant and a guarantee that quietly does nothing.
		read.Should().Throw<IOException>(
			because: "a symbolic link in place of the file itself must be refused, not followed");
	}

	[Test]
	[Category("Integration")]
	[Description("Writes a new confined file and refuses a second write to the same path, keeping the additive no-overwrite contract.")]
	public void WriteNew_ShouldCreateTheFile_AndRefuseAnExistingTarget() {
		// Arrange
		string path = Path.Combine(_sandbox, "nested", "out.json");
		byte[] content = Encoding.UTF8.GetBytes("{\"written\":true}");

		// Act
		_access.WriteNew(Path.Combine(_sandbox, "nested", "out.json"), content);
		Action second = () => _access.WriteNew(Path.Combine(_sandbox, "nested", "out.json"), content);

		// Assert
		File.ReadAllText(path).Should().Be("{\"written\":true}",
			because: "the write must publish exactly the bytes it was given, creating the parent directory");
		second.Should().Throw<IOException>(
			because: "an explicit output-file is additive and must never overwrite an existing file")
			.WithMessage("*already exists*");
	}

	[Test]
	[Category("Integration")]
	[Description("Refuses a file past the ceiling WITHOUT copying it into memory first, so a large file inside an allowed root cannot exhaust the process before the published bound runs.")]
	public void OpenRead_ShouldRefuse_AFilePastTheCeiling_BeforeBuffering() {
		// Arrange
		string path = Path.Combine(_sandbox, "oversized.json");
		File.WriteAllBytes(path, new byte[2048]);

		// Act
		Action read = () => _access.OpenRead(path, maxBytes: 1024).Dispose();

		// Assert
		read.Should().Throw<IOException>(
			because: "the ceiling has to bound the read itself; a stream handed back and measured afterwards "
				+ "has already cost whatever the file contained")
			.WithMessage("*exceeds*");
	}

	[Test]
	[Category("Integration")]
	[Description("Only one of two concurrent writers to the same path may win, and the winner's bytes survive - a check-then-rename publish would let the second silently overwrite the first.")]
	public void WriteNew_ShouldLetExactlyOneOfTwoConcurrentWritersWin() {
		// Arrange
		string path = Path.Combine(_sandbox, "contended.json");
		using Barrier barrier = new(2);
		int successes = 0;
		string winner = null;

		void Write(string marker) {
			barrier.SignalAndWait();
			try {
				_access.WriteNew(path, Encoding.UTF8.GetBytes(marker));
				Interlocked.Increment(ref successes);
				winner = marker;
			}
			catch (IOException) {
				// Expected for the loser: the name was taken first.
			}
		}

		// Act
		Task first = Task.Run(() => Write("{\"writer\":\"a\"}"));
		Task second = Task.Run(() => Write("{\"writer\":\"b\"}"));
		Task.WaitAll(first, second);

		// Assert
		successes.Should().Be(1,
			because: "publishing must be an atomic test-and-create; a check followed by a replacing rename "
				+ "lets both writers observe an absent target and both report success");
		File.ReadAllText(path).Should().Be(winner,
			because: "the winner's content must survive intact - the loser must not have overwritten it");
		Directory.GetFiles(_sandbox, "*.tmp").Should().BeEmpty(
			because: "the loser must clean up its own temporary sibling");
	}

	[Test]
	[Category("Integration")]
	[Description("Narrows the sibling temporary file to owner-only BEFORE any payload byte is written, which is the guarantee the published file's mode cannot prove.")]
	public void WriteNew_ShouldRestrictTheTemporarySibling_BeforeWritingAnyByte() {
		// Arrange
		if (OperatingSystem.IsWindows()) {
			Assert.Ignore("Unix file modes have no meaning on Windows.");
		}
		string path = Path.Combine(_sandbox, "creation-time-mode.json");
		UnixFileMode? observedMode = null;
		long observedLength = -1;
		UnixConfinedFileAccess.NotifyTemporaryFileRestricted = temporaryName => {
			string temporaryPath = Path.Combine(_sandbox, temporaryName);
			observedMode = File.GetUnixFileMode(temporaryPath);
			observedLength = new FileInfo(temporaryPath).Length;
		};

		try {
			// Act
			_access.WriteNew(path, Encoding.UTF8.GetBytes("{\"a\":1}"));
		} finally {
			UnixConfinedFileAccess.NotifyTemporaryFileRestricted = null;
		}

		// Assert
		observedMode.Should().NotBeNull(
			because: "the writer must reach the point where the temporary sibling exists and its mode is settled");
		observedMode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite,
			because: "the sibling holds the same raw service response and lives under the shared OS temp root, so it must never be readable by other local users while the write is in progress");
		observedLength.Should().Be(0,
			because: "the ordering is the whole point - a mode narrowed after the write leaves the payload exposed for the length of the transfer, and the published inode is owner-only either way, so nothing else can tell the two apart");
		File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
		Directory.GetFiles(_sandbox, "*.tmp").Should().BeEmpty(
			because: "the temporary entry is unlinked once the content is published under its final name");
	}

	[Test]
	[Category("Integration")]
	[Description("Publishes the file owner-readable and owner-writable only, so a raw service response under the shared OS temp root is not exposed to other local users - and so a file nobody can read is caught too.")]
	public void WriteNew_ShouldPublish_AnOwnerOnlyFile() {
		// Arrange
		if (OperatingSystem.IsWindows()) {
			Assert.Ignore("Unix file modes have no meaning on Windows.");
		}
		string path = Path.Combine(_sandbox, "owner-only.json");

		// Act
		_access.WriteNew(path, Encoding.UTF8.GetBytes("{\"a\":1}"));

		// Assert
		// This assertion exists because the mode CANNOT be passed to the create call: openat is variadic and
		// its mode argument is not reachable through an ordinary P/Invoke on every platform. A regression
		// there does not throw - it produces a file with arbitrary permissions, which is either a
		// confidentiality hole or a file the owner cannot read, and only this check tells the difference.
		File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite,
			because: "the payload is a raw service response and must not be readable by other local users");
		File.ReadAllText(path).Should().Be("{\"a\":1}",
			because: "the owner must still be able to read the file that was just written for them");
	}

	[TestCase(Architecture.X64)]
	[TestCase(Architecture.X86)]
	[TestCase(Architecture.Arm64)]
	[Category("Unit")]
	[Description("Resolves O_NOFOLLOW and O_DIRECTORY to the asm-generic values on every 64-bit and x86 Linux architecture, including arm64, where only 32-bit ARM has its own constants.")]
	public void Flags_ShouldUseTheGenericLinuxConstants_ForEveryArchitectureButArm32(Architecture architecture) {
		// Act
		int noFollow = UnixConfinedFileAccess.Flags.SelectFlag(
			isDarwin: false, architecture, darwin: 0x0100, linuxArm32: 0x8000, linux: 0x20000);
		int directory = UnixConfinedFileAccess.Flags.SelectFlag(
			isDarwin: false, architecture, darwin: 0x100000, linuxArm32: 0x4000, linux: 0x10000);

		// Assert
		noFollow.Should().Be(0x20000,
			because: "arm64 Linux uses the asm-generic O_NOFOLLOW, so folding it into the arch/arm column passed 0x8000 - O_LARGEFILE, a no-op on 64-bit - and the open ran without no-follow at all");
		directory.Should().Be(0x10000,
			because: "the arch/arm O_DIRECTORY value 0x4000 is O_DIRECT on asm-generic, so the descent stopped enforcing that a component is a directory");
		// because: no CI leg runs on Linux arm64, and this mapping fails silently in the dangerous direction
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the arch/arm constants for 32-bit ARM Linux, the one architecture whose values genuinely differ.")]
	public void Flags_ShouldKeepTheArmConstants_ForThirtyTwoBitArmLinux() {
		// Act
		int noFollow = UnixConfinedFileAccess.Flags.SelectFlag(
			isDarwin: false, Architecture.Arm, darwin: 0x0100, linuxArm32: 0x8000, linux: 0x20000);

		// Assert
		noFollow.Should().Be(0x8000, because: "arch/arm defines its own O_NOFOLLOW and is why the override exists");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the Darwin constants on Darwin whatever the architecture, so Apple silicon is not routed through an ARM Linux column.")]
	public void Flags_ShouldUseTheDarwinConstants_OnDarwin() {
		// Act
		int noFollow = UnixConfinedFileAccess.Flags.SelectFlag(
			isDarwin: true, Architecture.Arm64, darwin: 0x0100, linuxArm32: 0x8000, linux: 0x20000);

		// Assert
		noFollow.Should().Be(0x0100, because: "Darwin's O_NOFOLLOW is architecture-independent and is checked first");
	}

	[Test]
	[Category("Integration")]
	[Description("Refuses to write through a parent directory replaced by a symbolic link after approval, and leaves nothing behind at the swap target.")]
	public void WriteNew_ShouldRefuse_AParentDirectoryReplacedByALinkAfterApproval() {
		// Arrange
		string approvedDirectory = Path.Combine(_sandbox, "approved");
		Directory.CreateDirectory(approvedDirectory);
		string outsideDirectory = Path.Combine(_sandbox, "outside");
		Directory.CreateDirectory(outsideDirectory);
		string canonical = Path.Combine(_sandbox, "approved", "out.json");

		// Act
		Directory.Delete(approvedDirectory, recursive: true);
		try {
			Directory.CreateSymbolicLink(approvedDirectory, "outside");
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}
		Action write = () => _access.WriteNew(canonical, Encoding.UTF8.GetBytes("{}"));

		// Assert
		write.Should().Throw<IOException>(
			because: "the approved parent is no longer the directory that was checked, so the write must fail closed");
		File.Exists(Path.Combine(outsideDirectory, "out.json")).Should().BeFalse(
			because: "nothing may be created at the swap target");
	}

	[Test]
	[Category("Integration")]
	[Description("Creates missing parents relative to the pinned descent, so a symbolic link at the OUTER missing segment cannot make the inner one land outside the allowed roots.")]
	public void WriteNew_ShouldNotCreateADirectory_OutsideTheRoots_WhenAnOuterMissingSegmentIsALink() {
		// Arrange - two missing segments, the outer one a symbolic link pointing elsewhere. This is the case
		// a single pre-descent Directory.CreateDirectory got wrong: it ran on the mutable absolute path and
		// followed the link, creating the inner directory at the target. The later descent then refused the
		// response file, but the out-of-root directory was already there.
		string outsideDirectory = Path.Combine(_sandbox, "outside");
		Directory.CreateDirectory(outsideDirectory);
		string linked = Path.Combine(_sandbox, "linked");
		try {
			Directory.CreateSymbolicLink(linked, "outside");
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}

		// Act
		Action write = () => _access.WriteNew(
			Path.Combine(linked, "inner", "out.json"), Encoding.UTF8.GetBytes("{}"));

		// Assert
		write.Should().Throw<IOException>(
			because: "a symbolic link in the path is refused whether the components past it exist or not");
		Directory.Exists(Path.Combine(outsideDirectory, "inner")).Should().BeFalse(
			because: "a refused write must not leave a directory it created outside the allowed roots, and a directory created through a link cannot be taken back");
	}

	[Test]
	[Category("Integration")]
	[Description("Leaves no file and no temporary sibling when the content cannot be written, so a failed write is retryable rather than blocked by its own wreckage.")]
	public void WriteNew_ShouldLeaveNothing_WhenTheTargetDirectoryDisappears() {
		// Arrange
		string missing = Path.Combine(_sandbox, "gone", "out.json");
		Directory.CreateDirectory(Path.Combine(_sandbox, "gone"));
		Directory.Delete(Path.Combine(_sandbox, "gone"));

		// Act
		_access.WriteNew(missing, Encoding.UTF8.GetBytes("{\"a\":1}"));

		// Assert
		File.Exists(missing).Should().BeTrue(
			because: "a missing parent directory is created, not treated as a failure");
		Directory.GetFiles(Path.Combine(_sandbox, "gone"), "*.tmp").Should().BeEmpty(
			because: "the sibling temporary file must never survive a completed write");
	}

	[Test]
	[Category("Integration")]
	[Description("Swaps the FINAL component for a link to other content while reads run against it, and requires that no read ever returns that other content - the property a pathname check followed by a reopen cannot provide.")]
	public void OpenRead_ShouldNeverReturnSwappedContent_WhenTheFinalComponentIsReplacedDuringTheRead() {
		// Arrange
		const string approvedContent = "{\"approved\":true}";
		const string secretContent = "{\"secret\":true}";
		string approved = Path.Combine(_sandbox, "approved.json");
		string secret = Path.Combine(_sandbox, "secret.json");
		File.WriteAllText(secret, secretContent);
		File.WriteAllText(approved, approvedContent);
		if (!SymbolicLinksAvailable(Path.Combine(_sandbox, "probe.json"))) {
			Assert.Ignore("Symbolic-link creation is unavailable in this environment.");
		}
		using CancellationTokenSource stop = new(TimeSpan.FromSeconds(3));
		string swappedContentSeen = null;
		int successfulReads = 0;

		// Act
		// The swap is what makes this a regression rather than a restatement of the pre-planted-link case: the
		// pathname is a REAL FILE when it is checked and a LINK when it is opened. Only a handle that both the
		// check and the read share can rule that out, so a version that checks the name and reopens it by name
		// fails here while passing every static case.
		Task swapper = Task.Run(() => {
			while (!stop.IsCancellationRequested) {
				TryQuietly(() => File.Delete(approved));
				TryQuietly(() => File.CreateSymbolicLink(approved, "secret.json"));
				TryQuietly(() => File.Delete(approved));
				TryQuietly(() => File.WriteAllText(approved, approvedContent));
			}
		});
		while (!stop.IsCancellationRequested) {
			try {
				using Stream stream = _access.OpenRead(approved, TestCeiling);
				using StreamReader reader = new(stream, Encoding.UTF8);
				string content = reader.ReadToEnd();
				successfulReads++;
				if (content.Contains("secret", StringComparison.Ordinal)) {
					swappedContentSeen = content;
					break;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				// Refusing the read is the correct outcome for a swapped component, and an ordinary sharing
				// conflict against the swapper is equally uninteresting - only returning the OTHER file's
				// content is a defect.
			}
		}
		stop.Cancel();
		swapper.Wait(TimeSpan.FromSeconds(5));

		// Assert
		swappedContentSeen.Should().BeNull(
			because: "a read approved for one file must never deliver the content of another, whatever the "
				+ "final component was replaced with in between");
		successfulReads.Should().BeGreaterThan(0,
			because: "a run in which every single read failed would prove nothing about what a successful one "
				+ "returns");
	}

	[Test]
	[Category("Integration")]
	[Description("Swaps an INTERMEDIATE component for a link to another directory while confined writes run against it, and requires that nothing is ever created at the swap target - the property a pathname check followed by an open-by-name cannot provide.")]
	public void WriteNew_ShouldNeverCreateAnythingOutsideTheRoot_WhenAnIntermediateComponentIsSwappedDuringTheDescent() {
		// Arrange
		string approvedDirectory = Path.Combine(_sandbox, "approved");
		string outsideDirectory = Path.Combine(_sandbox, "outside");
		Directory.CreateDirectory(approvedDirectory);
		Directory.CreateDirectory(outsideDirectory);
		if (!DirectorySymbolicLinksAvailable(Path.Combine(_sandbox, "probe-dir"))) {
			Assert.Ignore("Directory symbolic-link creation is unavailable in this environment.");
		}
		using CancellationTokenSource stop = new(TimeSpan.FromSeconds(3));
		int successfulWrites = 0;
		int attempt = 0;

		// Act
		// The swap is what makes this a race regression rather than a restatement of the pre-planted-link
		// case: the component is a REAL DIRECTORY when its name is checked and a LINK by the time the descent
		// opens and pins it. A descent that judges only the pathname pins the link, then creates the missing
		// inner segment and the payload underneath it - outside the allowed root, and Reverify only notices
		// once that directory already exists and cannot be taken back. Only inspecting the OPENED HANDLE
		// closes the interval, so a version without that check fails here while passing every static case.
		Task swapper = Task.Run(() => {
			while (!stop.IsCancellationRequested) {
				TryQuietly(() => Directory.Delete(approvedDirectory, recursive: true));
				TryQuietly(() => Directory.CreateSymbolicLink(approvedDirectory, "outside"));
				TryQuietly(() => Directory.Delete(approvedDirectory));
				TryQuietly(() => Directory.CreateDirectory(approvedDirectory));
			}
		});
		while (!stop.IsCancellationRequested) {
			string target = Path.Combine(approvedDirectory, "nested", $"out-{attempt++}.json");
			try {
				_access.WriteNew(target, Encoding.UTF8.GetBytes("{\"ok\":true}"));
				successfulWrites++;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				// Refusing the write is the correct outcome for a swapped component, and losing a race to the
				// swapper over the directory itself is equally uninteresting - only a side effect at the swap
				// target is a defect.
			}
		}
		stop.Cancel();
		swapper.Wait(TimeSpan.FromSeconds(5));
		// The link is dropped before the assertions so the swap target is inspected as itself, not through it.
		TryQuietly(() => Directory.Delete(approvedDirectory));

		// Assert
		Directory.GetFileSystemEntries(outsideDirectory).Should().BeEmpty(
			because: "a write approved for one directory must never create a directory or a file at whatever "
				+ "an intermediate component was replaced with in between");
		successfulWrites.Should().BeGreaterThan(0,
			because: "a run in which every single write failed would prove nothing about where a successful "
				+ "one lands");
	}

	private static bool DirectorySymbolicLinksAvailable(string probePath) {
		try {
			Directory.CreateSymbolicLink(probePath, "outside");
			Directory.Delete(probePath);
			return true;
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			return false;
		}
	}

	private static bool SymbolicLinksAvailable(string probePath) {
		try {
			File.CreateSymbolicLink(probePath, "secret.json");
			File.Delete(probePath);
			return true;
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			return false;
		}
	}

	private static void TryQuietly(Action action) {
		try {
			action();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// The reader holds the entry for part of every iteration; a failed swap attempt simply means the
			// next iteration tries again.
		}
	}

	// Resolves a directory to its real location by following a link at EVERY component, parent first - the
	// same shape the confinement layer uses, so the tests address paths the way production hands them over.
	private static string CanonicalDirectory(string directory) {
		string parent = Path.GetDirectoryName(directory);
		if (string.IsNullOrEmpty(parent) || string.Equals(parent, directory, StringComparison.Ordinal)) {
			return directory;
		}
		string realParent = CanonicalDirectory(parent);
		string combined = Path.Combine(realParent, Path.GetFileName(directory));
		string target = ReadLinkTargetOrNull(combined);
		if (target is null) {
			return combined;
		}
		string resolved = Path.IsPathRooted(target) ? target : Path.Combine(realParent, target);
		return CanonicalDirectory(Path.GetFullPath(resolved));
	}

	private static string ReadLinkTargetOrNull(string path) {
		try {
			return new DirectoryInfo(path).LinkTarget ?? new FileInfo(path).LinkTarget;
		}
		catch (Exception) {
			return null;
		}
	}
}
