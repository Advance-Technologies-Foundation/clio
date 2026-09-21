using Clio10.PrimitiveContracts;
using System.IO.Compression;
using System.Text;
using Clio10.Contracts;
using Clio10.Primitives;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Checks binary compatibility and publication safety using real files and an independent format implementation.</summary>
public sealed class ArchiveTests {
    private string _root = null!;
    private ServiceProvider _services = null!;
    private IArchivePrimitive Primitive => _services.GetRequiredService<IArchivePrimitive>();
    [SetUp] public void Setup() {
        _root = Directory.CreateTempSubdirectory("clio10-archive-").FullName;
        _services = new ServiceCollection().AddSingleton<IArchivePrimitive, ArchivePrimitive>().BuildServiceProvider();
    }
    [TearDown] public void Cleanup() { _services.Dispose(); Directory.Delete(_root, true); }

    [Test]
    [Description("New archives use the legacy little-endian UTF16 record format, including Unicode names and empty files.")]
    public async Task Pack_matches_independent_reader() {
        // Arrange
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(source, "данные.bin"), [0, 1, 255]);
        await File.WriteAllTextAsync(Path.Combine(source, "empty.txt"), "");
        string archive = Path.Combine(_root, "package.gz");
        // Act
        var result = await Primitive.PackAsync(source, await Primitive.ListFilesAsync(source, default), archive, new(), default);
        // Assert
        using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip, Encoding.Unicode);
        var files = new Dictionary<string, byte[]>();
        for (int i = 0; i < result.Files; i++) {
            string name = Encoding.Unicode.GetString(reader.ReadBytes(reader.ReadInt32() * 2));
            files.Add(name, reader.ReadBytes(reader.ReadInt32()));
        }
        files["данные.bin"].Should().Equal([0, 1, 255], "the legacy reader must recover original binary content");
        files["empty.txt"].Should().BeEmpty("zero bytes is a valid record, not an archive terminator");
        result.Bytes.Should().Be(3, "content byte accounting excludes record headers");
    }

    [Test]
    [Description("An independent legacy writer's backslash paths extract safely as platform-relative directories.")]
    public async Task Extract_legacy_archive() {
        // Arrange
        string archive = LegacyArchive(("Schemas\\Example\\source.cs", new byte[] { 1, 2 }));
        string destination = Path.Combine(_root, "output");
        // Act
        var result = await Primitive.ExtractAsync(archive, destination, new(), default);
        // Assert
        (await File.ReadAllBytesAsync(Path.Combine(destination, "Schemas", "Example", "source.cs"))).Should().Equal([1, 2], "Windows-created archives must work on every supported OS");
        result.Files.Should().Be(1, "one complete record was published");
    }

    [Test]
    [Description("Explicit overwrite replaces the complete old package only after the new archive has been validated.")]
    public async Task Overwrite_replaces_complete_directory() {
        // Arrange
        string archive = LegacyArchive(("Files/new.bin", new byte[] { 7, 8 }));
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "obsolete.txt"), "old");
        // Act
        var result = await Primitive.ExtractAsync(archive, destination, new(), default, overwrite: true);
        // Assert
        (await File.ReadAllBytesAsync(Path.Combine(destination, "Files", "new.bin"))).Should().Equal([7, 8], "the fully validated replacement is now the package");
        File.Exists(Path.Combine(destination, "obsolete.txt")).Should().BeFalse("overwrite replaces the tree instead of retaining stale files");
        result.RetainedBackup.Should().BeNull("a successful ordinary replacement cleans its owned backup");
        Directory.GetDirectories(_root, ".clio-*").Should().BeEmpty("staging and backup directories are no longer needed after publication");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("Corrupt and cancelled overwrite attempts preserve every file in the existing destination.")]
    public async Task Failed_overwrite_preserves_original(bool cancelled) {
        // Arrange
        string archive = LegacyArchive(("Files/new.bin", new byte[] { 7, 8 }));
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "original.txt"), "retain");
        if (!cancelled) {
            byte[] bytes = await File.ReadAllBytesAsync(archive);
            bytes[^8] ^= 0xff;
            await File.WriteAllBytesAsync(archive, bytes);
        }
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, destination, new(), new CancellationToken(cancelled), overwrite: true);
        // Assert
        if (cancelled) await act.Should().ThrowAsync<OperationCanceledException>("cancellation before publication must stop the operation");
        else await act.Should().ThrowAsync<InvalidDataException>("invalid checksum must prevent replacement");
        (await File.ReadAllTextAsync(Path.Combine(destination, "original.txt"))).Should().Be("retain", "validation never changes the original tree");
        Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Should().HaveCount(1, "a failed attempt cannot leak new files into the old tree");
        Directory.GetDirectories(_root, ".clio-*").Should().BeEmpty("failed attempts clean their private staging directory");
    }

    [Test]
    [Description("An existing file cannot be replaced by an extracted directory even with explicit overwrite.")]
    public async Task Overwrite_cannot_replace_a_file() {
        // Arrange
        string archive = LegacyArchive(("file.txt", new byte[] { 7 }));
        string destination = Path.Combine(_root, "output");
        await File.WriteAllTextAsync(destination, "keep");
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, destination, new(), default, overwrite: true);
        // Assert
        await act.Should().ThrowAsync<IOException>("directory replacement does not authorize deleting an unrelated file");
        (await File.ReadAllTextAsync(destination)).Should().Be("keep", "refused replacement preserves the original file");
    }

    [Test]
    [Description("Trusted parent directory aliases work, but links within selected package content remain forbidden.")]
    public async Task Parent_directory_links_are_allowed_but_content_links_are_not() {
        // Arrange
        string real = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;
        Directory.CreateDirectory(Path.Combine(real, "package"));
        string alias = Path.Combine(_root, "alias");
        try { Directory.CreateSymbolicLink(alias, real); }
        catch (IOException) when (OperatingSystem.IsWindows()) { Assert.Ignore("Windows host does not permit creating symbolic links."); }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { Assert.Ignore("Windows host does not permit creating symbolic links."); }
        try {
            string package = Path.Combine(alias, "package");
            await File.WriteAllTextAsync(Path.Combine(package, "data.txt"), "content");
            string archive = Path.Combine(alias, "package.gz");
            // Act
            var packed = await Primitive.PackAsync(package, ["data.txt"], archive, new(), default);
            var extracted = await Primitive.ExtractAsync(archive, Path.Combine(alias, "output"), new(), default);
            Directory.CreateSymbolicLink(Path.Combine(real, "package", "linked"), Path.Combine(real, "output"));
            Func<Task> unsafePack = () => Primitive.PackAsync(package, ["linked/data.txt"], archive, new(), default);
            // Assert
            packed.Files.Should().Be(1, "trusted ancestors such as macOS /var must not invalidate an explicit source path");
            extracted.Files.Should().Be(1, "an explicit destination below a trusted parent alias is usable");
            await unsafePack.Should().ThrowAsync<IOException>("selected relative paths must not cross a link inside package contents");
        }
        finally { if (Directory.Exists(alias)) Directory.Delete(alias); }
    }

    [TestCase("../outside")]
    [TestCase("C:\\outside")]
    [TestCase("/outside")]
    [TestCase("a/../../outside")]
    [TestCase("a:stream")]
    [TestCase("NUL.txt")]
    [TestCase("a./file")]
    [Description("Untrusted paths are rejected before any extracted directory is published.")]
    public async Task Reject_unsafe_name(string name) {
        // Arrange
        string archive = LegacyArchive((name, new byte[] { 1 }));
        string destination = Path.Combine(_root, "output");
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, destination, new(), default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("archive entries cannot escape or alias platform paths");
        Directory.Exists(destination).Should().BeFalse("failed validation must not publish a partial tree");
    }

    [TestCase("a", "A")]
    [TestCase("a", "a/file")]
    [TestCase("a/file", "a")]
    [Description("Duplicate and ancestor collisions are rejected regardless of archive order.")]
    public async Task Reject_collisions(string first, string second) {
        // Arrange
        string archive = LegacyArchive((first, new byte[] { 1 }), (second, new byte[] { 2 }));
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, Path.Combine(_root, "output"), new(), default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("portable archives must not depend on overwrite order or filesystem case sensitivity");
        Directory.GetDirectories(_root).Should().BeEmpty("all failed staging data must be removed");
    }

    [TestCase(1)]
    [TestCase(8)]
    [Description("A missing gzip trailer is corruption even if every record has already decompressed.")]
    public async Task Truncated_gzip(int removed) {
        // Arrange
        string archive = LegacyArchive(("file", new byte[] { 1, 2, 3 }));
        using (var file = File.OpenWrite(archive)) file.SetLength(file.Length - removed);
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, Path.Combine(_root, "output"), new(), default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("truncated containers must not be mistaken for clean end of archive");
        Directory.GetDirectories(_root).Should().BeEmpty("truncation must leave no published or staged tree");
    }

    [Test]
    [Description("Oversized input cannot replace an existing archive, and extraction refuses an existing destination.")]
    public async Task Failure_preserves_destinations() {
        // Arrange
        string source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "file"), "large");
        string archive = LegacyArchive(("original", new byte[] { 7 }));
        byte[] before = await File.ReadAllBytesAsync(archive);
        // Act
        Func<Task> pack = () => Primitive.PackAsync(source, ["file"], archive, new(MaxExpandedBytes: 1), default);
        Func<Task> extract = () => Primitive.ExtractAsync(archive, source, new(), default);
        // Assert
        await pack.Should().ThrowAsync<InvalidDataException>("content size limits apply before archive publication");
        await extract.Should().ThrowAsync<IOException>("an existing tree must never be merged or erased implicitly");
        (await File.ReadAllBytesAsync(archive)).Should().Equal(before, "a failed pack must retain the previous usable archive");
        (await File.ReadAllTextAsync(Path.Combine(source, "file"))).Should().Be("large", "existing extracted content belongs to its owner");
    }

    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    [Description("A hostile path length is rejected before allocating storage proportional to the declared value.")]
    public async Task Invalid_name_length(int length) {
        // Arrange
        string archive = Path.Combine(_root, "hostile.gz");
        using (var gzip = new GZipStream(File.Create(archive), CompressionLevel.Optimal))
        using (var writer = new BinaryWriter(gzip)) writer.Write(length);
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, Path.Combine(_root, "output"), new(), default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("untrusted counts must not drive large allocations or reads");
        Directory.GetDirectories(_root).Should().BeEmpty("validation failure must remove the private staging directory");
    }

    [Test]
    [Description("Content bytes shorter than the declared record length cannot be padded or published as a successful extraction.")]
    public async Task Short_content() {
        // Arrange
        string archive = Path.Combine(_root, "short.gz");
        using (var gzip = new GZipStream(File.Create(archive), CompressionLevel.Optimal))
        using (var writer = new BinaryWriter(gzip, Encoding.Unicode)) {
            writer.Write(1); writer.Write(Encoding.Unicode.GetBytes("a")); writer.Write(50); writer.Write((byte)1);
        }
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, Path.Combine(_root, "output"), new(), default);
        // Assert
        await act.Should().ThrowAsync<EndOfStreamException>("the declared file content must be complete");
        Directory.GetDirectories(_root).Should().BeEmpty("partial content must never appear at the requested destination");
    }

    [Test]
    [Description("Gzip CRC corruption is rejected even when record lengths and the size trailer are intact.")]
    public async Task Invalid_checksum() {
        // Arrange
        string archive = LegacyArchive(("file", new byte[] { 1, 2, 3 }));
        byte[] bytes = await File.ReadAllBytesAsync(archive);
        bytes[^8] ^= 0xFF;
        await File.WriteAllBytesAsync(archive, bytes);
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, Path.Combine(_root, "output"), new(), default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("gzip integrity checks must run before publication");
        Directory.GetDirectories(_root).Should().BeEmpty("corrupted content cannot become a usable package");
    }

    [Test]
    [Description("Cancellation before extraction publishes nothing and removes its owned staging directory.")]
    public async Task Cancellation_cleans_staging() {
        // Arrange
        string archive = LegacyArchive(("file", new byte[] { 1 }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // Act
        Func<Task> act = () => Primitive.ExtractAsync(archive, Path.Combine(_root, "output"), new(), cancellation.Token);
        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>("cancellation must stop external work before publishing");
        Directory.GetDirectories(_root).Should().BeEmpty("a cancelled extraction does not own a final output tree");
    }

    private string LegacyArchive(params (string Name, byte[] Content)[] entries) {
        string archive = Path.Combine(_root, "legacy.gz");
        using var gzip = new GZipStream(File.Create(archive), CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzip, Encoding.Unicode);
        foreach (var entry in entries) {
            writer.Write(entry.Name.Length); writer.Write(Encoding.Unicode.GetBytes(entry.Name));
            writer.Write(entry.Content.Length); writer.Write(entry.Content);
        }
        return archive;
    }
}
