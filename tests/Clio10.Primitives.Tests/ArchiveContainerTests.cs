using Clio10.PrimitiveContracts;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using Clio10.Contracts;
using Clio10.Primitives;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Verifies real ZIP containers, independent gzip fixtures, shared bounds and whole-batch validation.</summary>
public sealed class ArchiveContainerTests {
    private string _root = null!;
    private ServiceProvider _services = null!;
    private IArchivePrimitive Primitive => _services.GetRequiredService<IArchivePrimitive>();
    [SetUp] public void Setup() {
        _root = Directory.CreateTempSubdirectory("clio10-container-").FullName;
        _services = new ServiceCollection().AddSingleton<IArchivePrimitive, ArchivePrimitive>().BuildServiceProvider();
    }
    [TearDown] public void Cleanup() { _services.Dispose(); Directory.Delete(_root, true); }

    [Test]
    [Description("Batch compression publishes root gzip entries readable independently of the new archive implementation.")]
    public async Task Pack_creates_legacy_zip_container() {
        // Arrange
        string source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "payload");
        string destination = Path.Combine(_root, "packages.zip");
        // Act
        var result = await Primitive.PackContainerAsync([new("First", source, ["data.txt"]), new("Second", source, ["data.txt"])], destination, new(new()), default);
        // Assert
        using var zip = ZipFile.OpenRead(destination);
        zip.Entries.Select(x => x.FullName).Should().Equal(["First.gz", "Second.gz"], "each requested package is represented by a root gzip entry");
        foreach (var entry in zip.Entries) {
            using var gzip = new GZipStream(entry.Open(), CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, Encoding.Unicode);
            Encoding.Unicode.GetString(reader.ReadBytes(reader.ReadInt32() * 2)).Should().Be("data.txt", "inner records retain the legacy UTF16 format");
            Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())).Should().Be("payload", "the ZIP container must preserve actual file contents");
        }
        result.Files.Should().Be(2, "the result counts package content files rather than ZIP entries");
    }

    [Test]
    [Description("ZIP extraction selects root gzip entries and ignores unrelated safe metadata or nested archives, matching legacy selection.")]
    public async Task Extracts_root_archives_only() {
        // Arrange
        string zip = Container(("First.gz", Gzip("Files/a.txt", "one")), ("Second.gz", Gzip("data.bin", "two")),
            ("metadata.txt", Encoding.UTF8.GetBytes("metadata")), ("nested/ignored.gz", Gzip("ignored.txt", "ignored")));
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        // Act
        var result = await Primitive.ExtractContainerAsync(zip, destination, new(new()), default);
        // Assert
        result.FailedDestination.Should().BeNull("all selected archives are valid");
        result.Completed.Should().HaveCount(2, "only the two root gzip entries are package inputs");
        (await File.ReadAllTextAsync(Path.Combine(destination, "First", "Files", "a.txt"))).Should().Be("one", "independent gzip bytes decode into the first package");
        (await File.ReadAllTextAsync(Path.Combine(destination, "Second", "data.bin"))).Should().Be("two", "independent gzip bytes decode into the second package");
        Directory.GetFiles(destination).Should().BeEmpty("temporary gzip files and unrelated ZIP entries must not be published");
    }

    [Test]
    [Description("A corrupt later package prevents publication of an earlier valid replacement.")]
    public async Task Entire_batch_validates_before_overwrite() {
        // Arrange
        string zip = Container(("First.gz", Gzip("new.txt", "new")), ("Second.gz", Gzip("../escape", "bad")));
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        string original = Directory.CreateDirectory(Path.Combine(destination, "First")).FullName;
        await File.WriteAllTextAsync(Path.Combine(original, "old.txt"), "keep");
        // Act
        Func<Task> act = () => Primitive.ExtractContainerAsync(zip, destination, new(new()), default, overwrite: true);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("all selected gzip streams must validate before any publication");
        (await File.ReadAllTextAsync(Path.Combine(original, "old.txt"))).Should().Be("keep", "a later invalid package must not destroy an earlier destination");
        Directory.GetDirectories(destination).Should().Equal([original], "failed validation cleans staging and publishes no new package");
    }

    [TestCase("../Escape.gz")]
    [TestCase("A.gz")]
    [TestCase("aux.txt")]
    [TestCase("nested\\")]
    [Description("Unsafe ZIP entry paths and case-insensitive package collisions fail before output publication.")]
    public async Task Invalid_container_names(string secondName) {
        // Arrange
        string zip = Container(("a.gz", Gzip("one", "one")), (secondName, Gzip("two", "two")));
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        // Act
        Func<Task> act = () => Primitive.ExtractContainerAsync(zip, destination, new(new()), default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("container names must not escape or alias a package destination");
        Directory.GetFileSystemEntries(destination).Should().BeEmpty("rejected container metadata cannot publish extracted files");
    }

    [TestCase("archives")]
    [TestCase("bytes")]
    [TestCase("metadata")]
    [TestCase("content")]
    [TestCase("files")]
    [Description("Archive count, container bytes, metadata and aggregate content bounds are enforced across the entire batch.")]
    public async Task Batch_limits_are_global(string kind) {
        // Arrange
        string zip = Container(("First.gz", Gzip("one", "one")), ("Second.gz", Gzip("two", "two")));
        var limits = new ArchiveContainerLimits(new());
        limits = kind switch {
            "archives" => limits with { MaxArchives = 1 }, "bytes" => limits with { MaxContainerBytes = 20 },
            "metadata" => limits with { MaxMetadataBytes = 1 }, "content" => limits with { Content = new(MaxExpandedBytes: 4) },
            _ => limits with { Content = new(MaxEntries: 1) }
        };
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        // Act
        Func<Task> act = () => Primitive.ExtractContainerAsync(zip, destination, limits, default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("a bounded container cannot reset its allowance for each package");
        Directory.GetFileSystemEntries(destination).Should().BeEmpty("limits are checked before publication");
    }

    [Test]
    [Description("Cancelled batch compression preserves an existing ZIP rather than deleting it before starting work.")]
    public async Task Cancelled_pack_preserves_existing_zip() {
        // Arrange
        string source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "file"), "data");
        string destination = Path.Combine(_root, "packages.zip");
        await File.WriteAllTextAsync(destination, "original");
        // Act
        Func<Task> act = () => Primitive.PackContainerAsync([new("Package", source, ["file"])], destination, new(new()), new CancellationToken(true));
        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>("cancellation must stop packing before publication");
        (await File.ReadAllTextAsync(destination)).Should().Be("original", "the existing ZIP is retained until its complete replacement is ready");
    }

    [Test]
    [Platform("Win")]
    [Description("A Windows file lock during later publication preserves earlier completed receipts and the blocked original package.")]
    public async Task Later_publication_failure_preserves_completed_receipt() {
        // Arrange
        string firstArchive = Path.Combine(_root, "First.gz"), secondArchive = Path.Combine(_root, "Second.gz");
        await File.WriteAllBytesAsync(firstArchive, Gzip("new.txt", "first new"));
        await File.WriteAllBytesAsync(secondArchive, Gzip("new.txt", "second new"));
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        foreach (string name in new[] { "First", "Second" }) {
            Directory.CreateDirectory(Path.Combine(destination, name));
            await File.WriteAllTextAsync(Path.Combine(destination, name, "old.txt"), "original");
        }
        ArchiveBatchResult result;
        // Act
        using (var locked = new FileStream(Path.Combine(destination, "Second", "old.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = await Primitive.ExtractManyAsync([new("First", firstArchive), new("Second", secondArchive)], destination, new(new()), default, overwrite: true);
        // Assert
        result.Completed.Select(x => Path.GetFileName(x.Destination)).Should().Equal(["First"], "the first successful publication must survive a later filesystem error");
        result.FailedDestination.Should().Be(Path.Combine(destination, "Second"), "the caller needs to know where publication stopped");
        (await File.ReadAllTextAsync(Path.Combine(destination, "First", "new.txt"))).Should().Be("first new", "the first receipt corresponds to real published data");
        (await File.ReadAllTextAsync(Path.Combine(destination, "Second", "old.txt"))).Should().Be("original", "the blocked package's original tree must survive");
    }

    [Test]
    [Description("A large valid central directory exceeds its metadata budget before entry selection, independently of the entry-count limit.")]
    public async Task Metadata_budget_bounds_entry_materialization() {
        // Arrange
        string zip = Container(Enumerable.Range(0, 300).Select(i => ($"metadata-{i}-" + new string('x', 180) + ".txt", Array.Empty<byte>())).ToArray());
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        // Act
        Func<Task> act = () => Primitive.ExtractContainerAsync(zip, destination, new(new(), MaxMetadataBytes: 8192), default);
        // Assert
        var failure = await act.Should().ThrowAsync<InvalidDataException>("a valid large directory must be bounded before its names are all allocated");
        failure.Which.Message.Should().Contain("metadata", "the read budget, rather than empty package selection or count, must cause this failure");
        Directory.GetFileSystemEntries(destination).Should().BeEmpty("metadata failure cannot publish output");
    }

    [Test]
    [Description("Directory batches share a cumulative compressed-source limit before publishing any prepared package.")]
    public async Task Directory_batch_shares_source_limit() {
        // Arrange
        byte[] content = Gzip("file", "data");
        string first = Path.Combine(_root, "First.gz"), second = Path.Combine(_root, "Second.gz");
        await File.WriteAllBytesAsync(first, content);
        await File.WriteAllBytesAsync(second, content);
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        // Act
        Func<Task> act = () => Primitive.ExtractManyAsync([new("First", first), new("Second", second)], destination,
            new(new(), MaxContainerBytes: content.Length + 1), default);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>("two individually small files cannot each reset the shared compressed-byte budget");
        Directory.GetFileSystemEntries(destination).Should().BeEmpty("the previously prepared first package must not publish on later validation failure");
    }

    [TestCase(-1)]
    [TestCase(1)]
    [Description("Under- and over-declared ZIP entry lengths are rejected before gzip publication.")]
    public async Task Declared_zip_length_must_match_payload(int delta) {
        // Arrange
        string zip = Container(("Package.gz", Gzip("file", "data")));
        byte[] bytes = await File.ReadAllBytesAsync(zip);
        int central = bytes.AsSpan().IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
        int actual = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(central + 24, 4));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(central + 24, 4), actual + delta);
        await File.WriteAllBytesAsync(zip, bytes);
        string destination = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
        // Act
        Func<Task> act = () => Primitive.ExtractContainerAsync(zip, destination, new(new()), default);
        // Assert
        var failure = await act.Should().ThrowAsync<Exception>("a container cannot hide or invent payload bytes with its size metadata");
        (failure.Which is InvalidDataException or EndOfStreamException).Should().BeTrue("the failure must identify corrupt data or truncation");
        Directory.GetFileSystemEntries(destination).Should().BeEmpty("inconsistent containers cannot publish package contents");
    }

    private string Container(params (string Name, byte[] Bytes)[] entries) {
        string path = Path.Combine(_root, "input.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries) { using var output = zip.CreateEntry(name).Open(); output.Write(bytes); }
        return path;
    }
    private static byte[] Gzip(string name, string text) {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip, Encoding.Unicode)) {
            var content = Encoding.UTF8.GetBytes(text);
            writer.Write(name.Length); writer.Write(Encoding.Unicode.GetBytes(name)); writer.Write(content.Length); writer.Write(content);
        }
        return bytes.ToArray();
    }
}
