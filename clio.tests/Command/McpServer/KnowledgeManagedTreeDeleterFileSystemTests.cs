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
			because: "a recursive delete unlinks the reparse point, so the managed root is still removable");
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
		// no reparse point at all - which is the silent no-op the fallback exists to prevent.
		(File.GetAttributes(path) & FileAttributes.ReparsePoint).Should().NotBe(default,
			because: "without the ReparsePoint bit this test asserts nothing, and the guard it covers is the "
				+ "one that keeps an untrusted checkout from being walked through");
	}

	[Test]
	[Description("Removes a knowledge cache that contains a directory junction, which a bare recursive delete cannot.")]
	public void Delete_ShouldRemoveTheTree_WhenItContainsADirectoryJunction() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("A junction is a Windows-only reparse tag; the symlink shape is covered above.");
		}
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
	[Description("Records that a bare recursive delete really does fail on a junction child, so a regression here is the platform's and not clio's.")]
	public void RecursiveDelete_ShouldFail_OnAJunctionChild_WhichIsWhyTheDeleterUnlinksFirst() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("A junction is a Windows-only reparse tag.");
		}
		Directory.CreateDirectory(_outside);
		Directory.CreateDirectory(_root);
		CreateJunction(Path.Combine(_root, "linked"), _outside);

		// Act
		Exception captured = null;
		try {
			Directory.Delete(_root, recursive: true);
		} catch (Exception exception) {
			captured = exception;
		}

		// Assert
		captured.Should().NotBeNull(
			because: "the deleter unlinks reparse points up front only because the framework cannot handle a "
				+ "junction child; if this ever starts succeeding the workaround can be reconsidered");
		Directory.Exists(_root).Should().BeTrue(
			because: "the framework removes the junction and then throws, so the tree is left behind - which "
				+ "is exactly the failure a user sees as an undeletable knowledge cache");
	}

	private static void ForceDelete(string path) {
		if (!Directory.Exists(path)) {
			return;
		}
		// TopDirectoryOnly with manual recursion, for the same reason the production deleter uses it: an
		// AllDirectories walk descends through a reparse point and would clear the very bit a failing test
		// is asserting on.
		foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)) {
			try {
				File.SetAttributes(file, FileAttributes.Normal);
			} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			}
		}
		foreach (string child in Directory.EnumerateDirectories(path)) {
			if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) {
				ForceDelete(child);
			}
		}
		try {
			Directory.Delete(path, recursive: true);
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
		}
	}
}
