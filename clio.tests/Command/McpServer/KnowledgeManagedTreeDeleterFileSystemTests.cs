using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Real-filesystem half of the deleter's coverage. Separate from
/// <see cref="KnowledgeManagedTreeDeleterTests"/> because <c>Category=Unit</c> means no I/O, and this fixture
/// creates directories, read-only files and reparse points.
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "McpServer")]
public sealed class KnowledgeManagedTreeDeleterFileSystemTests {
	private string _root = null!;
	private string _outside = null!;
	private ServiceProvider _container = null!;
	private IKnowledgeManagedTreeDeleter _deleter = null!;

	[SetUp]
	public void SetUp() {
		_root = Path.Combine(Path.GetTempPath(), $"clio-managed-tree-{Guid.NewGuid():N}");
		_outside = Path.Combine(Path.GetTempPath(), $"clio-outside-tree-{Guid.NewGuid():N}");
		ServiceCollection services = new();
		services.AddSingleton<IFileSystem, FileSystem>();
		services.AddSingleton<IKnowledgeManagedTreeDeleter, KnowledgeManagedTreeDeleter>();
		_container = services.BuildServiceProvider();
		_deleter = _container.GetRequiredService<IKnowledgeManagedTreeDeleter>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
		ForceDelete(_root);
		ForceDelete(_outside);
	}

	[Test]
	[Description("Deletes a knowledge checkout whose files are read-only, as every Git pack file is.")]
	public void Delete_ShouldRemoveTheTree_WhenItContainsReadOnlyFiles() {
		// Arrange
		string packDirectory = Path.Combine(_root, "repository", ".git", "objects", "pack");
		Directory.CreateDirectory(packDirectory);
		string packFile = Path.Combine(packDirectory, "pack-abc.pack");
		File.WriteAllText(packFile, "pack");
		File.SetAttributes(packFile, FileAttributes.ReadOnly);

		// Act
		_deleter.Delete(_root);

		// Assert
		Directory.Exists(_root).Should().BeFalse(
			because: "a source whose cache survives its own deletion can never be re-added: the next attempt "
				+ "is refused with 'not owned by Clio', a state no command can clear");
	}

	[Test]
	[Description("Deletes a knowledge checkout whose nested directory is read-only or non-writable.")]
	public void Delete_ShouldRemoveTheTree_WhenItContainsAReadOnlyDirectory() {
		// Arrange
		string nested = Path.Combine(_root, "repository", "objects");
		Directory.CreateDirectory(nested);
		File.WriteAllText(Path.Combine(nested, "object.bin"), "content");
		File.SetAttributes(nested, File.GetAttributes(nested) | FileAttributes.ReadOnly);

		// Act
		_deleter.Delete(_root);

		// Assert
		Directory.Exists(_root).Should().BeFalse(
			because: "Unix requires directory write permission to unlink children, while Windows carries the attribute too");
	}

	[Test]
	[Description("Leaves read-only files beyond a directory reparse point untouched, because the delete never reaches them.")]
	public void Delete_ShouldNotClearReadOnlyBeyondADirectoryReparsePoint() {
		// Arrange
		Directory.CreateDirectory(_outside);
		string outsideFile = Path.Combine(_outside, "protected.txt");
		File.WriteAllText(outsideFile, "protected");
		File.SetAttributes(outsideFile, FileAttributes.ReadOnly);
		Directory.CreateDirectory(_root);
		CreateDirectoryReparsePoint(Path.Combine(_root, "linked"), _outside);

		// Act
		_deleter.Delete(_root);

		// Assert
		Directory.Exists(_root).Should().BeFalse(
			because: "the deleter unlinks the reparse point itself; a bare recursive delete cannot remove a "
				+ "tree containing a junction, which is the whole reason the unlink exists");
		File.Exists(outsideFile).Should().BeTrue(
			because: "Directory.Delete unlinks a reparse point instead of emptying its target, so nothing "
				+ "beyond the link is ever deleted");
		File.GetAttributes(outsideFile).HasFlag(FileAttributes.ReadOnly).Should().BeTrue(
			because: "the attribute walk must stop where the delete stops; clearing read-only on files it "
				+ "will never delete mutates state outside the managed root - including a checkout Clio has "
				+ "just rejected as untrusted");
	}

	// DETERMINISTIC per platform, deliberately not "try the nicer one first". Choosing a symbolic link when
	// the privilege happens to be available made this a lottery: on an elevated or Developer-Mode host the
	// symlink branch ran, and a symlink is the shape recursive delete handles natively. The junction branch -
	// the shape that actually breaks it - therefore first executed on a release runner, in a release.
	// A junction needs no privilege, so Windows always gets one and always exercises the hard case.
	private static void CreateDirectoryReparsePoint(string path, string target) {
		if (!OperatingSystem.IsWindows()) {
			Directory.CreateSymbolicLink(path, target);
			return;
		}
		CreateJunction(path, target);
	}

	private static void CreateJunction(string path, string target) {
		// No output redirection: nothing drains those pipes, and mklink's output is not needed.
		using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"") {
			UseShellExecute = false,
			CreateNoWindow = true
		})!;
		process.WaitForExit(TimeSpan.FromSeconds(30)).Should().BeTrue(
			because: "a junction creation that never returns must fail this test rather than hang the run");
		process.ExitCode.Should().Be(0,
			because: "mklink reports a failure only through its exit code");
		// Directory.Exists is true for a PLAIN directory too, so checking it would let this test pass with
		// no reparse point at all - a silent no-op in the exact test that has to fail loudly.
		(File.GetAttributes(path) & FileAttributes.ReparsePoint).Should().NotBe(default,
			because: "without the ReparsePoint bit this test asserts nothing, and it covers two guards at "
				+ "once: the walk must not descend through the link, and the link must be unlinked so the "
				+ "recursive delete never meets it");
	}

	[Test]
	[Platform(Include = "Win", Reason = "A junction is a Windows-only reparse tag; the symlink shape is covered above.")]
	[Description("Removes a knowledge cache that contains a directory junction, which a bare recursive delete cannot.")]
	public void Delete_ShouldRemoveTheTree_WhenItContainsADirectoryJunction() {
		// Arrange
		Directory.CreateDirectory(_outside);
		string outsideFile = Path.Combine(_outside, "protected.txt");
		File.WriteAllText(outsideFile, "protected");
		File.SetAttributes(outsideFile, FileAttributes.ReadOnly);
		string nested = Path.Combine(_root, "repository", ".git", "modules");
		Directory.CreateDirectory(nested);
		CreateJunction(Path.Combine(nested, "linked"), _outside);

		// Act
		_deleter.Delete(_root);

		// Assert
		Directory.Exists(_root).Should().BeFalse(
			because: "a user whose knowledge checkout contains a junction must still be able to delete the "
				+ "cache; a bare recursive delete removes the junction and then throws, leaving the tree and "
				+ "with it the 'not owned by Clio' dead end");
		File.Exists(outsideFile).Should().BeTrue(
			because: "unlinking a junction must never reach through to what it points at");
		File.GetAttributes(outsideFile).HasFlag(FileAttributes.ReadOnly).Should().BeTrue(
			because: "nothing behind the link is deleted, so nothing behind it may be modified either");
	}

	[Test]
	[Platform(Include = "Win", Reason = "Off Windows the shared reparse-point test already uses a symlink.")]
	[Description("Removes a knowledge cache containing a directory symbolic link, the Windows tag recursive delete handles natively.")]
	public void Delete_ShouldRemoveTheTree_WhenItContainsADirectorySymbolicLink() {
		// Arrange
		Directory.CreateDirectory(_outside);
		string outsideFile = Path.Combine(_outside, "protected.txt");
		File.WriteAllText(outsideFile, "protected");
		File.SetAttributes(outsideFile, FileAttributes.ReadOnly);
		Directory.CreateDirectory(_root);
		try {
			Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), _outside);
		} catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) {
			// Deliberate, and narrow: this is the EASY tag, the one recursive delete copes with natively.
			// The hard tag has an unconditional junction test, so skipping here cannot hide the regression
			// that cost a release.
			Assert.Ignore($"SeCreateSymbolicLinkPrivilege is unavailable: {exception.Message}");
		}

		// Act
		_deleter.Delete(_root);

		// Assert
		Directory.Exists(_root).Should().BeFalse(
			because: "making the helper always use a junction on Windows removed every Windows assertion "
				+ "about the symlink tag, and the PR claims one rule covers both shapes");
		File.Exists(outsideFile).Should().BeTrue(
			because: "unlinking a symbolic link must not reach through to its target either");
		File.GetAttributes(outsideFile).HasFlag(FileAttributes.ReadOnly).Should().BeTrue(
			because: "nothing behind the link is deleted, so nothing behind it may be modified");
	}

	[Test]
	[Platform(Include = "Win", Reason = "A junction is a Windows-only reparse tag.")]
	[Description("Removes a knowledge source cache containing a junction through the recoverable path the store actually uses.")]
	public void DeleteRecoverably_ShouldRemoveTheTree_WhenItContainsADirectoryJunction() {
		// Arrange
		Directory.CreateDirectory(_outside);
		string outsideFile = Path.Combine(_outside, "protected.txt");
		File.WriteAllText(outsideFile, "protected");
		Directory.CreateDirectory(Path.Combine(_root, "repository"));
		CreateJunction(Path.Combine(_root, "repository", "linked"), _outside);

		// Act
		_deleter.DeleteRecoverably(_root);

		// Assert
		Directory.Exists(_root).Should().BeFalse(
			because: "KnowledgeSourceInstallationStore deletes a source cache through this path, not through "
				+ "Delete - so a junction that breaks it is what a real user hits");
		Directory.EnumerateDirectories(Path.GetTempPath(), QuarantineGlob())
			.Should().BeEmpty(
				because: "renaming aside is a step, not an outcome: a rename that succeeds while the empty "
					+ "fails leaves the tree complete under a scratch name, which Directory.Exists cannot see");
		File.Exists(outsideFile).Should().BeTrue(
			because: "the rename moves the link but must never reach through to what it points at");
	}

	[Test]
	[Platform(Include = "Win", Reason = "A junction is a Windows-only reparse tag.")]
	[Description("Reclaims an abandoned scratch tree that contains a junction, which the swallowing sweep used to leak forever.")]
	public void DeleteRecoverably_ShouldReclaimAnAbandonedQuarantine_WhenItContainsADirectoryJunction() {
		// Arrange
		Directory.CreateDirectory(_outside);
		File.WriteAllText(Path.Combine(_outside, "protected.txt"), "protected");
		string abandoned = Path.Combine(
			Path.GetTempPath(),
			$"{KnowledgeManagedTreeDeleter.QuarantinePrefix}{Path.GetFileName(_root)}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(abandoned);
		CreateJunction(Path.Combine(abandoned, "linked"), _outside);
		Directory.CreateDirectory(_root);

		// Act
		_deleter.DeleteRecoverably(_root);

		// Assert
		Directory.Exists(abandoned).Should().BeFalse(
			because: "the sweep swallows IOException, so before the unlink a junction inside an abandoned "
				+ "scratch tree made it unreclaimable and silently permanent - nothing else enumerates it");
	}

	[Test]
	[Platform(Include = "Win", Reason = "A junction is a Windows-only reparse tag.")]
	[Description("Records whether a bare recursive delete still fails on a junction child; inconclusive when the platform is fixed, because removing the workaround is a decision and not a clio regression.")]
	public void Delete_ShouldRemainNecessary_WhileTheFrameworkFailsOnAJunctionChild() {
		// Arrange
		Directory.CreateDirectory(_outside);
		Directory.CreateDirectory(_root);
		string link = Path.Combine(_root, "linked");
		CreateJunction(link, _outside);
		Action bareRecursiveDelete = () => Directory.Delete(_root, recursive: true);

		// Act
		Exception observed = Record(bareRecursiveDelete);

		// Assert
		// Inconclusive, NOT a failure. Clio is correct either way - unlinking a reparse point is harmless
		// when the framework could also have handled it - so a platform fix must not turn red in the
		// unfiltered release lane and read as a clio regression.
		if (observed is null) {
			Assert.Inconclusive(
				"Directory.Delete(recursive: true) now handles a junction child. "
				+ "KnowledgeManagedTreeDeleter's unlink may be removable - but only once the LOWEST shipped "
				+ "target framework also copes (clio ships net8.0 and net10.0; clio.tests runs net10.0 only). "
				+ "Decide deliberately and update "
				+ "docs/knowledge/platform/recursive-directory-delete-throws-on-a-junction-child.md. "
				+ "This is NOT a clio regression.");
		}
		Directory.Exists(_root).Should().BeTrue(
			because: "the framework removes the junction and then throws, so the tree is left behind - which "
				+ "is exactly the failure a user sees as an undeletable knowledge cache");
		Directory.Exists(link).Should().BeFalse(
			because: "the junction is already gone when it throws, which is why a bare retry appears to work "
				+ "and must not be used: a retry after a partial delete is the non-atomic behaviour "
				+ "DeleteRecoverably renames the tree to avoid");
	}

	// No bare catch(Exception) in a test body - project-context.md forbids it, and it straddles Act/Assert.
	private static Exception Record(Action action) {
		try {
			action();
			return null;
		} catch (Exception exception) {
			return exception;
		}
	}

	// The production shape is <parent>/.clio-deleting-<rootName>-<guid>. Derived from the constant so a
	// hand-written glob cannot silently match nothing and turn an assertion into a no-op.
	private string QuarantineGlob() =>
		$"{KnowledgeManagedTreeDeleter.QuarantinePrefix}{Path.GetFileName(_root)}-*";

	private static void ForceDelete(string path) {
		if (!Directory.Exists(path)) {
			return;
		}
		// TopDirectoryOnly with manual recursion, and unlink rather than skip - both for the same reasons the
		// production deleter does it: an AllDirectories walk descends through a reparse point and would clear
		// the very bit a failing test asserts on, and a junction left in place defeats the recursive delete.
		foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)) {
			try {
				File.SetAttributes(file, FileAttributes.Normal);
			} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			}
		}
		foreach (string child in Directory.EnumerateDirectories(path)) {
			if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) {
				// UNLINK, not skip. Skipping is what the production deleter used to do, and it hands a
				// junction-bearing tree to the recursive delete below, which throws and is swallowed here -
				// leaking the tree on exactly the runs where a test failed and left one behind.
				try {
					Directory.Delete(child, recursive: false);
				} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
				}
				continue;
			}
			ForceDelete(child);
		}
		try {
			Directory.Delete(path, recursive: true);
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
		}
	}
}
