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

	// A symbolic link needs SeCreateSymbolicLinkPrivilege on Windows, which a non-elevated CI agent without
	// Developer Mode does not have. A JUNCTION sets the same ReparsePoint bit and needs no privilege, so the
	// invariant stays covered there instead of being silently ignored.
	private static void CreateDirectoryReparsePoint(string path, string target) {
		try {
			Directory.CreateSymbolicLink(path, target);
			return;
		} catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
				or PlatformNotSupportedException) {
			if (!OperatingSystem.IsWindows()) {
				Assert.Fail($"Creating a directory symbolic link failed: {exception.Message}");
			}
		}
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
