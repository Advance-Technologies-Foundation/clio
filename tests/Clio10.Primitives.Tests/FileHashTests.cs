using System.Text;
using Clio10.PrimitiveContracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Primitives.Tests;

/// <summary>Exercises SHA-256 hashing against real temporary files.</summary>
public sealed class FileHashTests {
    private DirectoryInfo _directory = null!;
    private ServiceProvider _provider = null!;
    private IFileHashPrimitive _primitive = null!;

    /// <summary>Creates isolated filesystem and DI resources for each test.</summary>
    [SetUp]
    public void SetUp() {
        _directory = Directory.CreateTempSubdirectory("clio10-file-hash-");
        var services = new ServiceCollection();
        services.AddScoped<IFileHashPrimitive, FileHashPrimitive>();
        _provider = services.BuildServiceProvider();
        _primitive = _provider.GetRequiredService<IFileHashPrimitive>();
    }

    /// <summary>Disposes services and removes only this test's temporary directory.</summary>
    [TearDown]
    public async Task TearDown() {
        await _provider.DisposeAsync();
        _directory.Delete(true);
    }

    /// <summary>Checks a known vector and verifies stream ownership.</summary>
    [Test]
    [Description("A real file containing abc returns the published SHA-256 vector and releases its file handle.")]
    public async Task Hashes_known_content_and_releases_file() {
        // Arrange
        var path = Path.Combine(_directory.FullName, "known.txt");
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("abc"));

        // Act
        var result = await _primitive.HashAsync(path, CancellationToken.None);

        // Assert
        result.Sha256.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "abc has a known SHA-256 digest, encoded in lowercase");
        result.Bytes.Should().Be(3, "the result counts the three bytes read from the file");
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        exclusive.Length.Should().Be(3, "hashing must release the file handle and preserve its contents");
    }

    /// <summary>Checks the empty input vector.</summary>
    [Test]
    [Description("An empty real file returns the SHA-256 empty-input digest and zero bytes.")]
    public async Task Hashes_empty_file() {
        // Arrange
        var path = Path.Combine(_directory.FullName, "empty.bin");
        await File.WriteAllBytesAsync(path, []);

        // Act
        var result = await _primitive.HashAsync(path, CancellationToken.None);

        // Assert
        result.Sha256.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "zero bytes still have a defined SHA-256 digest");
        result.Bytes.Should().Be(0, "the stream contains no bytes");
    }

    /// <summary>Checks a vector spanning multiple reads with a final partial buffer.</summary>
    [Test]
    [Description("One million ASCII a bytes are hashed completely across several fixed-size buffers.")]
    public async Task Hashes_multiple_buffers_and_partial_final_read() {
        // Arrange
        var path = Path.Combine(_directory.FullName, "large.bin");
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes(new string('a', 1_000_000)));

        // Act
        var result = await _primitive.HashAsync(path, CancellationToken.None);

        // Assert
        result.Sha256.Should().Be("cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0",
            "the published million-a vector covers every read including the partial final buffer");
        result.Bytes.Should().Be(1_000_000, "only actual bytes read contribute to the count");
    }

    /// <summary>Preserves filesystem failures for Composition to classify.</summary>
    [Test]
    [Description("A missing file propagates FileNotFoundException without creating a file.")]
    public async Task Missing_file_propagates_filesystem_failure() {
        // Arrange
        var path = Path.Combine(_directory.FullName, "missing.bin");

        // Act
        Func<Task> act = () => _primitive.HashAsync(path, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>("Composition owns the stable failure code mapping");
        File.Exists(path).Should().BeFalse("hashing is a read-only capability");
    }

    /// <summary>Preserves cancellation before any filesystem operation.</summary>
    [TestCase(true)]
    [TestCase(false)]
    [Description("A pre-cancelled request propagates cancellation whether or not the target file exists.")]
    public async Task Pre_cancelled_request_propagates_cancellation(bool fileExists) {
        // Arrange
        var path = Path.Combine(_directory.FullName, "cancelled.bin");
        if (fileExists) {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("abc"));
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        Func<Task> act = () => _primitive.HashAsync(path, cancellation.Token);

        // Assert
        var exception = await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation is checked before the file is opened and must not become a filesystem failure");
        exception.Which.CancellationToken.Should().Be(cancellation.Token, "the caller's cancellation token is preserved");
    }
}
