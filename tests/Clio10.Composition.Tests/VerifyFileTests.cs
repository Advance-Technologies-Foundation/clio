using Clio10.Composition.Files;
using Clio10.Contracts;
using Clio10.PrimitiveContracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Verifies input validation and result policy with controlled file-hash responses.</summary>
public sealed class VerifyFileTests {
    private const string Digest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private ServiceProvider _provider = null!;
    private IClioWorkflow _workflow = null!;
    private IWorkflowContext _context = null!;
    private IFileHashPrimitive _primitive = null!;

    /// <summary>Resolves the workflow through DI with a controlled borrowed context.</summary>
    [SetUp]
    public void SetUp() {
        var services = new ServiceCollection();
        services.AddScoped<IClioWorkflow, VerifyFileWorkflow>();
        _provider = services.BuildServiceProvider();
        _workflow = _provider.GetRequiredService<IClioWorkflow>();
        _primitive = Substitute.For<IFileHashPrimitive>();
        _context = Substitute.For<IWorkflowContext>();
        _context.GetCapability("file-hash").Returns(_primitive);
        _context.PrimitiveVersion.Returns(new Version(10, 10, 0));
        _context.Arguments.Returns(new Dictionary<string, object?> { ["path"] = " input.bin ", ["sha256"] = Digest });
        _context.ClearReceivedCalls();
    }

    /// <summary>Disposes test-owned services and clears observations between cases.</summary>
    [TearDown]
    public void TearDown() {
        _primitive.ClearReceivedCalls();
        _context.ClearReceivedCalls();
        _provider.Dispose();
    }

    [TestCase(Digest, 3L, false)]
    [TestCase(Digest, 3L, true)]
    [TestCase(EmptyDigest, 0L, false)]
    [Description("Matching hashes accept lowercase or uppercase expected digests and preserve actual digest, bytes and pinned version.")]
    public async Task Matching_digest(string digest, long bytes, bool uppercase) {
        // Arrange
        _context.Arguments.Returns(new Dictionary<string, object?> {
            ["path"] = " input.bin ", ["sha256"] = uppercase ? digest.ToUpperInvariant() : digest
        });
        _primitive.HashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new FileHashResult(digest, bytes));
        using var cancellation = new CancellationTokenSource();
        // Act
        var result = await _workflow.ExecuteAsync(_context, cancellation.Token);
        // Assert
        result.Accepted.Should().BeTrue("a digest match verifies the file");
        result.Code.Should().Be("completed", "a successful verification has the stable completion code");
        result.PrimitiveVersion.Should().Be("10.10.0", "the result identifies the pinned runtime");
        result.Payload.Should().BeEquivalentTo(new Dictionary<string, object?> {
            ["sha256"] = digest, ["bytes"] = bytes, ["matches"] = true
        }, "the receipt describes the actual primitive result, including an empty file");
        _primitive.ReceivedCalls().Should().ContainSingle("verification must read the file once");
        var arguments = _primitive.ReceivedCalls().Single().GetArguments();
        arguments[0].Should().Be(" input.bin ", "valid paths must pass through without trimming");
        arguments[1].Should().Be(cancellation.Token, "the primitive must receive caller cancellation");
    }

    [Test]
    [Description("A mismatching digest is nonaccepted but retains the actual hash and byte receipt.")]
    public async Task Mismatching_digest() {
        // Arrange
        _primitive.HashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new FileHashResult(EmptyDigest, 0));
        // Act
        var result = await _workflow.ExecuteAsync(_context, CancellationToken.None);
        // Assert
        result.Accepted.Should().BeFalse("a different digest cannot verify the file");
        result.Code.Should().Be("hash-mismatch", "mismatch must be distinguishable from a read failure");
        result.PrimitiveVersion.Should().Be("10.10.0", "mismatch receipts still identify the pinned runtime");
        result.Payload.Should().BeEquivalentTo(new Dictionary<string, object?> {
            ["sha256"] = EmptyDigest, ["bytes"] = 0L, ["matches"] = false
        }, "callers need the actual result to diagnose a mismatch");
    }

    [TestCase("missing-digest")]
    [TestCase("null-digest")]
    [TestCase("nonstring-digest")]
    [TestCase("empty-digest")]
    [TestCase("short-digest")]
    [TestCase("long-digest")]
    [TestCase("space-digest")]
    [TestCase("nonhex-digest")]
    [TestCase("unicode-digest")]
    [TestCase("missing-path")]
    [TestCase("null-path")]
    [TestCase("nonstring-path")]
    [TestCase("empty-path")]
    [TestCase("whitespace-path")]
    [Description("Missing or malformed input returns invalid-arguments before resolving any primitive capability.")]
    public async Task Invalid_input_does_not_access_primitive(string scenario) {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["path"] = "input.bin", ["sha256"] = Digest };
        switch (scenario) {
            case "missing-digest": arguments.Remove("sha256"); break;
            case "null-digest": arguments["sha256"] = null; break;
            case "nonstring-digest": arguments["sha256"] = 42; break;
            case "empty-digest": arguments["sha256"] = ""; break;
            case "short-digest": arguments["sha256"] = Digest[..63]; break;
            case "long-digest": arguments["sha256"] = Digest + "0"; break;
            case "space-digest": arguments["sha256"] = " " + Digest; break;
            case "nonhex-digest": arguments["sha256"] = "g" + Digest[1..]; break;
            case "unicode-digest": arguments["sha256"] = "ｂ" + Digest[1..]; break;
            case "missing-path": arguments.Remove("path"); break;
            case "null-path": arguments["path"] = null; break;
            case "nonstring-path": arguments["path"] = 42; break;
            case "empty-path": arguments["path"] = ""; break;
            case "whitespace-path": arguments["path"] = " \t\r\n"; break;
        }
        _context.Arguments.Returns(arguments);
        // Act
        var result = await _workflow.ExecuteAsync(_context, CancellationToken.None);
        // Assert
        result.Accepted.Should().BeFalse("invalid input cannot verify a file");
        result.Code.Should().Be("invalid-arguments", "input failures must have a stable category");
        _context.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "GetCapability").Should().BeEmpty(
            "validation must finish before resolving any primitive");
        _primitive.ReceivedCalls().Should().BeEmpty("invalid input must never cause a file read");
    }

    [TestCase("file", "file-not-found")]
    [TestCase("directory", "file-not-found")]
    [TestCase("denied", "file-access-denied")]
    [TestCase("read", "file-read-failed")]
    [Description("Filesystem failures return stable nonaccepted results without leaking exception text or partial digests.")]
    public async Task File_failure(string failure, string code) {
        // Arrange
        const string sensitive = "private filesystem details";
        Exception error = failure switch {
            "file" => new FileNotFoundException(sensitive),
            "directory" => new DirectoryNotFoundException(sensitive),
            "denied" => new UnauthorizedAccessException(sensitive),
            _ => new IOException(sensitive)
        };
        _primitive.HashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<FileHashResult>(error));
        // Act
        var result = await _workflow.ExecuteAsync(_context, CancellationToken.None);
        // Assert
        result.Accepted.Should().BeFalse("a failed read cannot establish a digest match");
        result.Code.Should().Be(code, "the exception category determines the stable failure code");
        result.Response.Should().BeNull("raw exception details must not leave the workflow");
        result.Payload.Should().BeNull("a failed read has no complete hash receipt");
        result.ToString().Should().NotContain(sensitive, "results must not expose filesystem exception text");
    }

    [Test]
    [Description("Cancellation from an active primitive propagates unchanged rather than becoming a mismatch or read failure.")]
    public async Task Primitive_cancellation_propagates() {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _primitive.HashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => {
            cancellation.Cancel();
            return Task.FromCanceled<FileHashResult>(call.Arg<CancellationToken>());
        });
        // Act
        Func<Task> execute = () => _workflow.ExecuteAsync(_context, cancellation.Token);
        // Assert
        var thrown = await execute.Should().ThrowAsync<OperationCanceledException>("cancellation must remain cancellation");
        thrown.Which.CancellationToken.Should().Be(cancellation.Token, "the originating cancellation token must be retained");
    }

    [Test]
    [Description("An already-cancelled request does not resolve or invoke the file-hash capability.")]
    public async Task Precancelled_request_does_not_read() {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // Act
        Func<Task> execute = () => _workflow.ExecuteAsync(_context, cancellation.Token);
        // Assert
        await execute.Should().ThrowAsync<OperationCanceledException>("cancelled callers must not start a file read");
        _context.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "GetCapability").Should().BeEmpty(
            "a cancelled request must not acquire a primitive");
        _primitive.ReceivedCalls().Should().BeEmpty("a cancelled request must not invoke hashing");
    }
}
