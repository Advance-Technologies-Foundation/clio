using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Substituted-file-system coverage, and the ONLY tier that runs on every push: <c>build.yml</c> filters
/// the push/PR job to <c>TestCategory!=Integration</c> and gates the Integration job on <c>pull_request</c>, so
/// an invariant pinned only in <see cref="KnowledgeManagedTreeDeleterFileSystemTests"/> first executes in
/// the unfiltered release lane. Anything load-bearing belongs here as well as there — a junction cost a
/// release exactly that way.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeManagedTreeDeleterTests {
	private const string Root = "/managed/root";
	private const string Parent = "/managed";
	private const string LinkPath = "/managed/root/linked";

	[Test]
	[Description("Stops the read-only walk at a directory reparse point instead of descending through it.")]
	public void Delete_ShouldNotDescend_WhenAChildDirectoryIsAReparsePoint() {
		// Arrange
		IDirectoryInfo link = Directory(LinkPath, FileAttributes.Directory | FileAttributes.ReparsePoint);
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory, children: [link]);
		IFileSystem fileSystem = FileSystemFor(root, link);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(Root);

		// Assert
		// Its attributes ARE read - that is how the walk decides to stop. What must never happen is the
		// enumeration behind them.
		link.ReceivedCalls()
			.Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDirectoryInfo.EnumerateFileSystemInfos),
				because: "Directory.Delete unlinks a reparse point instead of emptying it, so descending would "
					+ "clear read-only bits on files outside the managed root that are never deleted - including "
					+ "on a checkout Clio has just rejected as untrusted");
	}

	[Test]
	[Description("Unlinks a directory reparse point during the walk instead of leaving it for the recursive delete.")]
	public void Delete_ShouldUnlinkAReparsePointChild_BeforeTheRecursiveDelete() {
		// Arrange
		IDirectoryInfo link = Directory(LinkPath, FileAttributes.Directory | FileAttributes.ReparsePoint);
		IDirectoryInfo plain = Directory(Root + "/plain", FileAttributes.Directory);
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory, children: [link, plain]);
		IFileSystem fileSystem = FileSystemFor(root, link, plain);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(Root);

		// Assert
		link.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == nameof(IDirectoryInfo.Delete)
					&& (bool)call.GetArguments()[0] == false,
				because: "a recursive delete that meets a junction child unlinks it and then throws anyway, "
					+ "leaving the tree - so the link has to be unlinked here, non-recursively, before the "
					+ "recursive delete ever sees it");
		plain.ReceivedCalls()
			.Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDirectoryInfo.Delete),
				because: "only reparse points are unlinked individually; an ordinary subdirectory is left for "
					+ "the recursive delete, which handles it and is far cheaper");
	}

	[Test]
	[Description("Descends into a reparse point that is not a link, so read-only files behind it are still cleared.")]
	public void Delete_ShouldDescend_WhenAReparsePointIsNotALink() {
		// Arrange
		// The third tag class: a OneDrive Files-On-Demand placeholder, a ProjFS/Scalar root, WCI, DFS. The
		// ReparsePoint bit is set but there is no link target, and the framework's recursive delete descends
		// into it - so this walk must too, or a read-only *.pack inside keeps its attribute and the cache
		// becomes undeletable for a different reason.
		IFileInfo behind = Substitute.For<IFileInfo>();
		behind.Attributes.Returns(FileAttributes.ReadOnly);
		IDirectoryInfo placeholder = Directory(
			LinkPath, FileAttributes.Directory | FileAttributes.ReparsePoint, files: [behind]);
		placeholder.ResolveLinkTarget(Arg.Any<bool>()).Returns((IFileSystemInfo)null);
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory, children: [placeholder]);
		IFileSystem fileSystem = FileSystemFor(root, placeholder);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(Root);

		// Assert
		placeholder.ReceivedCalls()
			.Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDirectoryInfo.Delete),
				because: "a non-name-surrogate tag is not a link: unlinking it fails as not-empty, and the "
					+ "framework descends into it rather than refusing it");
		behind.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == "set_IsReadOnly",
				because: "the recursive delete will reach this file, so its read-only bit has to be cleared - "
					+ "skipping the whole directory is what leaves an undeletable cache behind a placeholder");
	}

	[Test]
	[Description("Still attempts the recursive delete when unlinking a reparse point fails.")]
	public void Delete_ShouldStillDelete_WhenUnlinkingAReparsePointThrows() {
		// Arrange
		IDirectoryInfo link = Directory(LinkPath, FileAttributes.Directory | FileAttributes.ReparsePoint);
		link.When(info => info.Delete(false))
			.Do(_ => throw new UnauthorizedAccessException("Access to the path 'linked' is denied."));
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory, children: [link]);
		IFileSystem fileSystem = FileSystemFor(root, link);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(Root);

		// Assert
		fileSystem.Directory.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == nameof(IDirectory.Delete),
				because: "clearing and unlinking are best effort: a link that cannot be removed is left for "
					+ "the delete itself to report, exactly as an unresettable read-only file is");
	}

	[TestCase(FileAttributes.ReadOnly, true, TestName = "A plain read-only file is cleared")]
	[TestCase(FileAttributes.ReadOnly | FileAttributes.ReparsePoint, false, TestName = "A read-only symlink is skipped")]
	[TestCase(FileAttributes.Normal, false, TestName = "A writable file is left alone")]
	[Description("Clears the read-only attribute on a real file only, never through a file symlink.")]
	public void Delete_ShouldClearReadOnly_OnlyOnFilesThatAreNotReparsePoints(
		FileAttributes attributes, bool expectCleared) {
		// Arrange
		IFileInfo file = Substitute.For<IFileInfo>();
		file.Attributes.Returns(attributes);
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory, files: [file]);
		IFileSystem fileSystem = FileSystemFor(root);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(Root);

		// Assert
		bool cleared = file.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "set_IsReadOnly");
		cleared.Should().Be(expectCleared,
			because: "both Windows SetFileAttributesW and the Unix chmod behind IsReadOnly follow a symlink, "
				+ "so clearing one would reach a target the delete never touches");
	}

	[Test]
	[Description("Clears the read-only attribute on a directory before deleting its children.")]
	public void Delete_ShouldClearReadOnly_OnDirectories() {
		// Arrange
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory | FileAttributes.ReadOnly);
		IFileSystem fileSystem = FileSystemFor(root);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(Root);

		// Assert
		root.ReceivedCalls().Should().Contain(call => call.GetMethodInfo().Name == "set_Attributes",
			because: "Unix requires write permission on a directory before its entries can be removed");
	}

	[Test]
	[Description("Renames the tree before emptying it so a partial delete cannot strip the ownership marker.")]
	public void Delete_ShouldMoveTheTreeAside_BeforeRemovingIt() {
		// Arrange
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory);
		IFileSystem fileSystem = FileSystemFor(root);
		fileSystem.Directory.Exists(Arg.Any<string>()).Returns(true);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.DeleteRecoverably(Root);

		// Assert
		fileSystem.Directory.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name is nameof(IDirectory.Move) or nameof(IDirectory.Delete))
			.Select(call => call.GetMethodInfo().Name)
			.Should().Equal([nameof(IDirectory.Move), nameof(IDirectory.Delete)],
				because: "a recursive delete is not atomic and '.clio-knowledge-source' sorts first, so a "
					+ "delete that fails half way used to leave a source root with no ownership marker - after "
					+ "which every command is refused with 'not owned by Clio' and nothing can rewrite it");
	}

	[Test]
	[Description("Removes a scratch tree left behind by an earlier delete that failed after its rename.")]
	public void Delete_ShouldSweepAnAbandonedQuarantine_BeforeDoingAnythingElse() {
		// Arrange
		string abandoned = Parent + "/" + KnowledgeManagedTreeDeleter.QuarantinePrefix + "root-deadbeef";
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory);
		IDirectoryInfo scratch = Directory(abandoned, FileAttributes.Directory);
		IFileSystem fileSystem = FileSystemFor(root, scratch);
		fileSystem.Directory
			.EnumerateDirectories(Parent, KnowledgeManagedTreeDeleter.QuarantinePrefix + "root-*")
			.Returns([abandoned]);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.DeleteRecoverably(Root);

		// Assert
		fileSystem.Directory.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == nameof(IDirectory.Delete)
					&& (string)call.GetArguments()[0] == abandoned,
				because: "nothing else in the knowledge subsystem enumerates a scratch tree, so unless the "
					+ "next delete in the same directory reclaims it, a whole extracted generation is "
					+ "stranded on disk permanently");
	}

	[Test]
	[Description("Does not sweep an active quarantine belonging to a different source root.")]
	public void DeleteRecoverably_ShouldNotSweepQuarantine_ForAnotherRoot() {
		// Arrange
		string other = Parent + "/" + KnowledgeManagedTreeDeleter.QuarantinePrefix + "other-live";
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory);
		IDirectoryInfo active = Directory(other, FileAttributes.Directory);
		IFileSystem fileSystem = FileSystemFor(root, active);
		fileSystem.Directory
			.EnumerateDirectories(Parent, KnowledgeManagedTreeDeleter.QuarantinePrefix + "root-*")
			.Returns([]);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.DeleteRecoverably(Root);

		// Assert
		fileSystem.Directory.ReceivedCalls().Should().NotContain(call =>
			call.GetMethodInfo().Name == nameof(IDirectory.Delete)
			&& (string)call.GetArguments()[0] == other,
			because: "parallel source operations use different locks and must not delete each other's live quarantine");
	}

	[Test]
	[Description("Empties a scratch tree where it stands instead of nesting another scratch name inside it.")]
	public void Delete_ShouldNotRequarantine_WhenTheRootIsItselfAScratchTree() {
		// Arrange
		string scratchPath = Parent + "/" + KnowledgeManagedTreeDeleter.QuarantinePrefix + "abc123";
		IDirectoryInfo scratch = Directory(scratchPath, FileAttributes.Directory);
		IFileSystem fileSystem = FileSystemFor(scratch);
		fileSystem.Directory.Exists(scratchPath).Returns(true);
		fileSystem.Path.GetDirectoryName(scratchPath).Returns(Parent);
		fileSystem.Path.GetFileName(scratchPath)
			.Returns(KnowledgeManagedTreeDeleter.QuarantinePrefix + "abc123");
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(scratchPath);

		// Assert
		fileSystem.Directory.ReceivedCalls()
			.Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDirectory.Move),
				because: "KnowledgeSourceInstallationStore.Prune hands a leftover scratch tree back as if it "
					+ "were a generation, and renaming it again would nest one scratch name inside another");
		fileSystem.Directory.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == nameof(IDirectory.Delete)
					&& (string)call.GetArguments()[0] == scratchPath,
				because: "it still has to be emptied - in place");
	}

	[Test]
	[Description("Completes the requested delete even when sweeping an abandoned scratch tree fails.")]
	public void Delete_ShouldStillDelete_WhenSweepingAnAbandonedQuarantineThrows() {
		// Arrange
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory);
		IFileSystem fileSystem = FileSystemFor(root);
		fileSystem.Directory
			.EnumerateDirectories(Parent, KnowledgeManagedTreeDeleter.QuarantinePrefix + "root-*")
			.Returns(_ => throw new UnauthorizedAccessException("Access to the path is denied."));
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.DeleteRecoverably(Root);

		// Assert
		fileSystem.Directory.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == nameof(IDirectory.Move),
				because: "reclaiming somebody else's leftovers is best effort and must never fail the delete "
					+ "the caller actually asked for");
	}

	[Test]
	[Description("Propagates a failed rename without deleting anything, so the tree stays intact and retryable.")]
	public void Delete_ShouldNotDelete_WhenTheTreeCannotBeMovedAside() {
		// Arrange
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory);
		IFileSystem fileSystem = FileSystemFor(root);
		fileSystem.Directory
			.When(directory => directory.Move(Arg.Any<string>(), Arg.Any<string>()))
			.Do(_ => throw new IOException("The process cannot access the file because it is in use."));
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		Action act = () => deleter.DeleteRecoverably(Root);

		// Assert
		act.Should().Throw<IOException>(
			because: "the caller must see a retryable failure rather than a half-emptied source root");
		fileSystem.Directory.ReceivedCalls()
			.Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDirectory.Delete)
					&& (string)call.GetArguments()[0] == Root,
				because: "renaming first is only worth doing if a failed rename STOPS the delete - otherwise "
					+ "'.clio-knowledge-source' can still be stripped by a partial recursive delete");
	}

	[Test]
	[Description("Renames to a sibling scratch name and empties that, never the original path.")]
	public void Delete_ShouldRenameToASibling_AndEmptyTheScratchName() {
		// Arrange
		IDirectoryInfo root = Directory(Root, FileAttributes.Directory);
		IFileSystem fileSystem = FileSystemFor(root);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.DeleteRecoverably(Root);

		// Assert
		ICall move = fileSystem.Directory.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IDirectory.Move));
		move.GetArguments()[0].Should().Be(Root,
			because: "the managed root is what moves aside");
		string quarantine = (string)move.GetArguments()[1];
		quarantine.Should().StartWith(Parent + "/" + KnowledgeManagedTreeDeleter.QuarantinePrefix + "root-",
			because: "the scratch name must be a SIBLING so the rename stays on one volume and is a metadata "
				+ "operation, while the root-specific segment keeps parallel source deletes isolated");
		fileSystem.Directory.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == nameof(IDirectory.Delete)
					&& (string)call.GetArguments()[0] == quarantine,
				because: "the scratch name is what gets emptied, so a delete aimed at the original path would "
					+ "mean the rename bought nothing");
	}

	[Test]
	[Description("Still removes the tree when the read-only walk cannot enumerate a directory it reaches.")]
	public void Delete_ShouldStillRemoveTheTree_WhenTheAttributeWalkCannotEnumerate() {
		// Arrange
		IDirectoryInfo root = Substitute.For<IDirectoryInfo>();
		root.FullName.Returns(Root);
		root.Exists.Returns(true);
		root.Attributes.Returns(FileAttributes.Directory);
		root.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
			.Returns(_ => throw new UnauthorizedAccessException("Access to the path is denied."));
		IFileSystem fileSystem = FileSystemFor(root);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.DeleteRecoverably(Root);

		// Assert
		fileSystem.Directory.ReceivedCalls()
			.Should().Contain(call => call.GetMethodInfo().Name == nameof(IDirectory.Delete),
				because: "an enumeration failure escaping from MoveNext used to abort before the delete was "
					+ "reached, turning a delete that previously succeeded into the 'not owned by Clio' dead end");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[Description("Rejects a blank root rather than silently treating it as nothing to do.")]
	public void Delete_ShouldThrowArgumentException_WhenTheRootIsBlank(string root) {
		// Arrange
		IKnowledgeManagedTreeDeleter deleter = Resolve(Substitute.For<IFileSystem>());

		// Act
		Action act = () => deleter.Delete(root);

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "DeleteTransportStaging catches exactly this type from a finally block, so the guard's "
				+ "type is a contract between the two and not an implementation detail");
	}

	[Test]
	[Description("Ignores a root that is already gone instead of throwing at the caller.")]
	public void Delete_ShouldDoNothing_WhenTheRootDoesNotExist() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.Directory.Exists(Root).Returns(false);
		IKnowledgeManagedTreeDeleter deleter = Resolve(fileSystem);

		// Act
		deleter.Delete(Root);

		// Assert
		fileSystem.Directory.ReceivedCalls()
			.Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDirectory.Move),
				because: "every call site already treats an absent tree as success");
	}

	private static IKnowledgeManagedTreeDeleter Resolve(IFileSystem fileSystem) {
		ServiceCollection services = new();
		services.AddSingleton(fileSystem);
		services.AddSingleton<IKnowledgeManagedTreeDeleter, KnowledgeManagedTreeDeleter>();
		return services.BuildServiceProvider().GetRequiredService<IKnowledgeManagedTreeDeleter>();
	}

	private static IDirectoryInfo Directory(
		string fullName,
		FileAttributes attributes,
		IFileInfo[] files = null,
		IDirectoryInfo[] children = null) {
		IDirectoryInfo directory = Substitute.For<IDirectoryInfo>();
		directory.FullName.Returns(fullName);
		directory.Exists.Returns(true);
		directory.Attributes.Returns(attributes);
		directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
			.Returns([.. (files ?? []).Cast<IFileSystemInfo>(), .. (children ?? []).Cast<IFileSystemInfo>()]);
		return directory;
	}

	private static IFileSystem FileSystemFor(IDirectoryInfo root, params IDirectoryInfo[] others) {
		// Every substitute-backed value is read into a local BEFORE anything is configured: NSubstitute
		// treats a call made inside a Returns() argument as the call being configured, which fails with
		// "Can not return value of type ObjectProxy".
		(string Name, IDirectoryInfo Info)[] entries =
			[(root.FullName, root), .. others.Select(other => (other.FullName, other))];

		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.Path.DirectorySeparatorChar.Returns('/');
		fileSystem.Path.AltDirectorySeparatorChar.Returns('/');
		// Without these the sweep short-circuits on a null parent and NO test can reach it.
		fileSystem.Path.GetDirectoryName(Root).Returns(Parent);
		fileSystem.Path.GetFileName(Root).Returns("root");
		fileSystem.Path.Combine(Parent, Arg.Any<string>())
			.Returns(call => Parent + "/" + call.ArgAt<string>(1));
		fileSystem.Directory.Exists(Root).Returns(true);
		fileSystem.Directory.Exists(Parent).Returns(true);
		fileSystem.Directory.EnumerateDirectories(Parent, Arg.Any<string>()).Returns([]);
		foreach ((string name, IDirectoryInfo info) in entries) {
			fileSystem.DirectoryInfo.New(name).Returns(info);
		}
		// The tree is renamed before it is emptied, so the walk runs against the quarantine name.
		fileSystem.DirectoryInfo
			.New(Arg.Is<string>(path => path.StartsWith(
				Parent + "/" + KnowledgeManagedTreeDeleter.QuarantinePrefix, StringComparison.Ordinal)))
			.Returns(root);
		return fileSystem;
	}
}
