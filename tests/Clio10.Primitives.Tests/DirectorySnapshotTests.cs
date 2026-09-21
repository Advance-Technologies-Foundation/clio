using Clio10.PrimitiveContracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Primitives.Tests;

/// <summary>Exercises directory snapshots against isolated real trees and controlled hash failures.</summary>
public sealed class DirectorySnapshotTests {
    private DirectoryInfo _temporary = null!;
    private ServiceProvider _provider = null!;
    private IDirectorySnapshotPrimitive _primitive = null!;

    /// <summary>Creates a unique tree and resolves the capability through DI.</summary>
    [SetUp]
    public void SetUp() {
        _temporary = Directory.CreateDirectory(Path.Combine(ResolveDirectoryPath(Path.GetTempPath()),
            $"clio10-snapshot-{Guid.NewGuid():N}"));
        Configure();
    }

    private static string ResolveDirectoryPath(string path) {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (directory.Parent is null) return directory.FullName;
        var parent = ResolveDirectoryPath(directory.Parent.FullName);
        directory = new DirectoryInfo(Path.Combine(parent, directory.Name));
        return (directory.Attributes & FileAttributes.ReparsePoint) != 0
            ? ResolveDirectoryPath(directory.ResolveLinkTarget(true)!.FullName)
            : directory.FullName;
    }

    /// <summary>Checks fixture path resolution when the configured temporary parent contains a link.</summary>
    [Test, Description("Fixture path resolution follows a linked ancestor while retaining the requested child directory.")]
    public async Task Fixture_resolves_linked_parent() {
        // Arrange
        var target = Directory.CreateDirectory(Path.Combine(_temporary.FullName, "actual"));
        var child = Directory.CreateDirectory(Path.Combine(target.FullName, "child"));
        await File.WriteAllTextAsync(Path.Combine(child.FullName, "data"), "abc");
        var link = Path.Combine(_temporary.FullName, "linked-parent");
        try {
            Directory.CreateSymbolicLink(link, target.FullName);
        } catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException) {
            Assert.Ignore($"Symbolic link creation unavailable on this filesystem: {error.GetType().Name}: {error.Message}");
        }
        try {
            // Act
            var resolved = ResolveDirectoryPath(Path.Combine(link, "child"));
            var result = await _primitive.SnapshotAsync(resolved, CancellationToken.None);
            // Assert
            resolved.Should().Be(child.FullName, "the fixture must remove linked ancestors before normal snapshot tests");
            result.Select(file => file.RelativePath).Should().Equal(["data"], "the resolved fixture path must still name the same directory");
        } finally {
            Directory.Delete(link);
        }
    }
    private void Configure(IFileHashPrimitive? hash = null) {
        _provider?.Dispose();
        var services = new ServiceCollection();
        if (hash is null) {
            services.AddSingleton<IFileHashPrimitive, FileHashPrimitive>();
        } else {
            services.AddSingleton(hash);
        }
        services.AddSingleton<IDirectorySnapshotPrimitive, DirectorySnapshotPrimitive>();
        _provider = services.BuildServiceProvider();
        _primitive = _provider.GetRequiredService<IDirectorySnapshotPrimitive>();
    }

    /// <summary>Disposes services and removes the exclusively owned temporary tree.</summary>
    [TearDown]
    public async Task TearDown() {
        await _provider.DisposeAsync();
        _temporary.Delete(true);
    }

    /// <summary>Checks content hashes, ordering, nested paths and read-only behavior.</summary>
    [Test, Description("Real nested files return ordinal paths and known SHA-256 vectors without modifying content.")]
    public async Task Snapshots_real_tree() {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_temporary.FullName, "nested"));
        Directory.CreateDirectory(Path.Combine(_temporary.FullName, "empty"));
        var abc = Path.Combine(_temporary.FullName, "nested", "abc.txt");
        var empty = Path.Combine(_temporary.FullName, "Z.txt");
        await File.WriteAllTextAsync(abc, "abc");
        await File.WriteAllBytesAsync(empty, []);
        var modified = File.GetLastWriteTimeUtc(abc);

        // Act
        var result = await _primitive.SnapshotAsync(_temporary.FullName, CancellationToken.None);

        // Assert
        result.Should().Equal([
            new DirectoryFileSnapshot("Z.txt", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
            new DirectoryFileSnapshot("nested/abc.txt", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
        ], "only regular files are represented in ordinal order with normalized separators");
        (await File.ReadAllTextAsync(abc)).Should().Be("abc", "snapshotting cannot change content");
        File.GetLastWriteTimeUtc(abc).Should().Be(modified, "snapshotting cannot write the source file");
        using var exclusive = new FileStream(abc, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        exclusive.Length.Should().Be(3, "hash streams are disposed after the snapshot");
    }

    /// <summary>Checks empty tree handling.</summary>
    [Test, Description("A directory containing no files returns an empty snapshot.")]
    public async Task Empty_tree_is_empty() {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_temporary.FullName, "empty"));
        // Act
        var result = await _primitive.SnapshotAsync(_temporary.FullName, CancellationToken.None);
        // Assert
        result.Should().BeEmpty("empty directory metadata is excluded");
    }

    /// <summary>Checks missing and file roots.</summary>
    [TestCase(false), TestCase(true)]
    [Description("Missing roots and regular-file roots propagate DirectoryNotFoundException.")]
    public async Task Rejects_non_directory_root(bool exists) {
        // Arrange
        var path = Path.Combine(_temporary.FullName, "root");
        if (exists) await File.WriteAllTextAsync(path, "abc");
        // Act
        Func<Task> act = () => _primitive.SnapshotAsync(path, CancellationToken.None);
        // Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>("both roots violate the directory contract");
    }

    /// <summary>Checks controlled read and access failures.</summary>
    [TestCase(false), TestCase(true)]
    [Description("Injected file hash failures propagate unchanged so Composition can classify them.")]
    public async Task Propagates_hash_failure(bool denied) {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_temporary.FullName, "file"), "abc");
        Exception failure = denied ? new UnauthorizedAccessException("controlled") : new IOException("controlled");
        var hash = new ControlledHash(() => Task.FromException<FileHashResult>(failure));
        Configure(hash);
        // Act
        Func<Task> act = () => _primitive.SnapshotAsync(_temporary.FullName, CancellationToken.None);
        // Assert
        var thrown = await act.Should().ThrowAsync<Exception>("read failures cannot produce a partial successful snapshot");
        thrown.Which.Should().BeSameAs(failure, "the original filesystem failure must retain its classification");
    }

    /// <summary>Checks cancellation even when an injected hasher ignores it.</summary>
    [TestCase(false), TestCase(true)]
    [Description("Cancellation before traversal or during hashing propagates the caller token instead of a successful snapshot.")]
    public async Task Propagates_cancellation(bool before) {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_temporary.FullName, "file"), "abc");
        using var cancellation = new CancellationTokenSource();
        var hash = new ControlledHash(() => {
            cancellation.Cancel();
            return Task.FromResult(new FileHashResult("ignored", 3));
        });
        Configure(hash);
        if (before) cancellation.Cancel();
        // Act
        Func<Task> act = () => _primitive.SnapshotAsync(_temporary.FullName, cancellation.Token);
        // Assert
        var thrown = await act.Should().ThrowAsync<OperationCanceledException>("no completed snapshot may escape cancellation");
        thrown.Which.CancellationToken.Should().Be(cancellation.Token, "the caller owns cancellation");
    }

    /// <summary>Checks all link positions with explicit platform skips where links cannot be created.</summary>
    [TestCase("root"), TestCase("ancestor"), TestCase("file"), TestCase("directory")]
    [Description("Root, ancestor, file and directory symbolic links are rejected without hashing linked contents.")]
    public async Task Rejects_symbolic_links(string position) {
        // Arrange
        var target = Directory.CreateDirectory(Path.Combine(_temporary.FullName, "target"));
        Directory.CreateDirectory(Path.Combine(target.FullName, "child"));
        await File.WriteAllTextAsync(Path.Combine(target.FullName, "data"), "abc");
        var tree = Directory.CreateDirectory(Path.Combine(_temporary.FullName, "tree"));
        var link = Path.Combine(tree.FullName, "link");
        try {
            if (position == "file") File.CreateSymbolicLink(link, Path.Combine(target.FullName, "data"));
            else Directory.CreateSymbolicLink(link, target.FullName);
        } catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException) {
            Assert.Ignore($"Symbolic link creation unavailable on this filesystem: {error.GetType().Name}: {error.Message}");
        }
        var hash = new ControlledHash(() => Task.FromResult(new FileHashResult("unused", 0)));
        Configure(hash);
        var root = position switch { "root" => link, "ancestor" => Path.Combine(link, "child"), _ => tree.FullName };
        try {
            // Act
            Func<Task> act = () => _primitive.SnapshotAsync(root, CancellationToken.None);
            // Assert
            await act.Should().ThrowAsync<DirectoryLinkException>("every link position is excluded by the snapshot contract");
            hash.Calls.Should().Be(0, "linked content must never reach the hasher");
        } finally {
            if (position == "file") File.Delete(link);
            else Directory.Delete(link);
        }
    }

    /// <summary>Checks Unix filenames that contain a literal backslash.</summary>
    [Test, Description("Unix literal backslashes remain part of a filename instead of becoming directory separators.")]
    public async Task Preserves_unix_literal_backslash() {
        // Arrange
        if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("Backslash is a directory separator on this platform.");
        await File.WriteAllTextAsync(Path.Combine(_temporary.FullName, "literal\\name"), "abc");
        // Act
        var result = await _primitive.SnapshotAsync(_temporary.FullName, CancellationToken.None);
        // Assert
        result.Select(file => file.RelativePath).Should().Equal(["literal\\name"], "Unix permits backslashes inside a single filename");
    }
    private sealed class ControlledHash(Func<Task<FileHashResult>> action) : IFileHashPrimitive {
        public int Calls { get; private set; }
        public Task<FileHashResult> HashAsync(string path, CancellationToken cancellationToken) {
            Calls++;
            return action();
        }
    }
}
