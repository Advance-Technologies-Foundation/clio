using Clio10.Composition.Files;
using Clio10.Contracts;
using Clio10.PrimitiveContracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Checks comparison policy using controlled directory snapshots.</summary>
public sealed class CompareDirectoriesTests {
    private ServiceProvider _provider = null!;
    private IClioWorkflow _workflow = null!;
    private IWorkflowContext _context = null!;
    private IDirectorySnapshotPrimitive _primitive = null!;

    /// <summary>Resolves the workflow through DI with a borrowed controlled context.</summary>
    [SetUp]
    public void SetUp() {
        var services = new ServiceCollection();
        services.AddScoped<IClioWorkflow, CompareDirectoriesWorkflow>();
        _provider = services.BuildServiceProvider();
        _workflow = _provider.GetRequiredService<IClioWorkflow>();
        _primitive = Substitute.For<IDirectorySnapshotPrimitive>();
        _context = Substitute.For<IWorkflowContext>();
        _context.GetCapability("directory-snapshot").Returns(_primitive);
        _context.PrimitiveVersion.Returns(new Version(10, 10, 0));
        _context.Arguments.Returns(new Dictionary<string, object?> { ["left"] = " left ", ["right"] = "right" });
        _primitive.SnapshotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DirectoryFileSnapshot>());
        _context.ClearReceivedCalls();
    }

    /// <summary>Disposes services and clears observations between cases.</summary>
    [TearDown]
    public void TearDown() {
        _primitive.ClearReceivedCalls();
        _context.ClearReceivedCalls();
        _provider.Dispose();
    }

    [Test]
    [Description("Differences use ordinal case-sensitive matching and sorting and still complete successfully.")]
    public async Task Differences_are_sorted_and_accepted() {
        // Arrange
        _primitive.SnapshotAsync(" left ", Arg.Any<CancellationToken>()).Returns(new DirectoryFileSnapshot[] {
            new("z", "old"), new("Z", "old"), new("same", "same"), new("left", "x"), new("Case", "x"), new("nested/file", "old")
        });
        _primitive.SnapshotAsync("right", Arg.Any<CancellationToken>()).Returns(new DirectoryFileSnapshot[] {
            new("z", "new"), new("Z", "new"), new("same", "same"), new("right", "x"), new("case", "x"), new("nested/file", "new")
        });
        using var cancellation = new CancellationTokenSource();
        // Act
        var result = await _workflow.ExecuteAsync(_context, cancellation.Token);
        // Assert
        result.Accepted.Should().BeTrue("differences are a completed comparison");
        result.Code.Should().Be("completed", "differences do not indicate execution failure");
        result.PrimitiveVersion.Should().Be("10.10.0", "the receipt identifies the pinned runtime");
        var payload = result.Payload.Should().BeOfType<Dictionary<string, object?>>("results use lowercase dictionary keys").Subject;
        payload.Keys.Should().BeEquivalentTo(["added", "removed", "changed"], "the payload has exactly the three comparison arrays");
        ((string[])payload["added"]!).Should().Equal(["case", "right"], "right-only paths use ordinal sorting");
        ((string[])payload["removed"]!).Should().Equal(["Case", "left"], "case variants are distinct left-only paths");
        ((string[])payload["changed"]!).Should().Equal(["Z", "nested/file", "z"], "changed common paths use ordinal sorting");
        var calls = _primitive.ReceivedCalls().ToArray();
        calls.Length.Should().Be(2, "each tree is inspected once");
        calls.Select(call => call.GetArguments()[0]).Should().Equal([" left ", "right"], "paths pass through unchanged in left-to-right order");
        calls.Select(call => call.GetArguments()[1]).Should().OnlyContain(token => Equals(token, cancellation.Token), "both reads receive caller cancellation");
    }

    [Test]
    [Description("Empty or identical snapshots return three empty arrays, including when both paths are the same.")]
    public async Task Identical_trees_complete() {
        // Arrange
        _context.Arguments.Returns(new Dictionary<string, object?> { ["left"] = "same", ["right"] = "same" });
        // Act
        var result = await _workflow.ExecuteAsync(_context, CancellationToken.None);
        // Assert
        result.Accepted.Should().BeTrue("same-tree comparisons are allowed");
        result.Payload.Should().BeEquivalentTo(new Dictionary<string, object?> {
            ["added"] = Array.Empty<string>(), ["removed"] = Array.Empty<string>(), ["changed"] = Array.Empty<string>()
        }, "identical snapshots have no differences");
    }

    [TestCase("left", "missing")]
    [TestCase("left", "null")]
    [TestCase("left", "number")]
    [TestCase("left", "whitespace")]
    [TestCase("right", "missing")]
    [TestCase("right", "null")]
    [TestCase("right", "number")]
    [TestCase("right", "whitespace")]
    [Description("Both required arguments are validated before any capability is resolved or invoked.")]
    public async Task Invalid_input_does_not_read(string key, string scenario) {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["left"] = "left", ["right"] = "right" };
        if (scenario == "missing") arguments.Remove(key);
        else arguments[key] = scenario switch { "null" => null, "number" => 42, _ => " \t\r\n" };
        _context.Arguments.Returns(arguments);
        // Act
        var result = await _workflow.ExecuteAsync(_context, CancellationToken.None);
        // Assert
        result.Code.Should().Be("invalid-arguments", "both inputs require non-whitespace strings");
        result.Accepted.Should().BeFalse("invalid input cannot complete a comparison");
        _context.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "GetCapability").Should().BeEmpty("validation precedes capability resolution");
        _primitive.ReceivedCalls().Should().BeEmpty("invalid input must not inspect trees");
    }

    [TestCase(false, "missing", "directory-not-found")]
    [TestCase(true, "missing", "directory-not-found")]
    [TestCase(false, "denied", "directory-access-denied")]
    [TestCase(true, "denied", "directory-access-denied")]
    [TestCase(false, "read", "directory-read-failed")]
    [TestCase(true, "read", "directory-read-failed")]
    [TestCase(false, "link", "symbolic-link-not-supported")]
    [TestCase(true, "link", "symbolic-link-not-supported")]
    [TestCase(false, "argument", "invalid-arguments")]
    [TestCase(true, "argument", "invalid-arguments")]
    [TestCase(false, "unsupported", "invalid-arguments")]
    [TestCase(true, "unsupported", "invalid-arguments")]
    [Description("Either snapshot failure returns a stable category without a partial payload or exception details.")]
    public async Task Snapshot_failure(bool rightFails, string failure, string code) {
        // Arrange
        const string sensitive = "private filesystem detail";
        Exception exception = failure switch {
            "missing" => new DirectoryNotFoundException(sensitive), "denied" => new UnauthorizedAccessException(sensitive),
            "link" => new DirectoryLinkException(), "argument" => new ArgumentException(sensitive),
            "unsupported" => new NotSupportedException(sensitive), _ => new IOException(sensitive)
        };
        _primitive.SnapshotAsync(rightFails ? "right" : " left ", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<DirectoryFileSnapshot>>(exception));
        // Act
        var result = await _workflow.ExecuteAsync(_context, CancellationToken.None);
        // Assert
        result.Accepted.Should().BeFalse("a failed snapshot cannot complete a comparison");
        result.Code.Should().Be(code, "the primitive exception determines the public category");
        result.Payload.Should().BeNull("a partial tree must never appear as a complete diff");
        result.Response.Should().BeNull("exception details must stay private");
        result.ToString().Should().NotContain(sensitive, "results must not leak filesystem details");
        _primitive.ReceivedCalls().Count().Should().Be(rightFails ? 2 : 1, "left failure stops before the right read");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [Description("Cancellation before or after either primitive response prevents success and further reads.")]
    public async Task Cancellation_propagates(int cancelAfterRead) {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        int reads = 0;
        _primitive.SnapshotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => {
            if (++reads == cancelAfterRead) cancellation.Cancel();
            return Array.Empty<DirectoryFileSnapshot>();
        });
        if (cancelAfterRead == 0) cancellation.Cancel();
        // Act
        Func<Task> execute = () => _workflow.ExecuteAsync(_context, cancellation.Token);
        // Assert
        var thrown = await execute.Should().ThrowAsync<OperationCanceledException>("cancelled comparisons never return success");
        thrown.Which.CancellationToken.Should().Be(cancellation.Token, "the caller token is preserved");
        reads.Should().Be(cancelAfterRead, "cancellation stops subsequent snapshots");
    }

    [Test]
    [Description("The right snapshot cannot start while the left snapshot is incomplete.")]
    public async Task Snapshots_are_awaited_sequentially() {
        // Arrange
        var left = new TaskCompletionSource<IReadOnlyList<DirectoryFileSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _primitive.SnapshotAsync(" left ", Arg.Any<CancellationToken>()).Returns(left.Task);
        // Act
        var execution = _workflow.ExecuteAsync(_context, CancellationToken.None);
        int pendingCalls = _primitive.ReceivedCalls().Count();
        left.SetResult(Array.Empty<DirectoryFileSnapshot>());
        await execution;
        // Assert
        pendingCalls.Should().Be(1, "the right snapshot waits for left completion");
        _primitive.ReceivedCalls().Count().Should().Be(2, "the right snapshot runs after left completion");
    }
}
