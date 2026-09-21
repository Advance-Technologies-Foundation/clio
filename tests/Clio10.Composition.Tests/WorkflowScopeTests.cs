using Clio10.Contracts;
using Clio10.Core;
using Clio10.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
namespace Clio10.Tests;

/// <summary>Proves scoped dependencies belong to one complete root, including children.</summary>
public sealed class WorkflowScopeTests {
    [TestCase(false)]
    [TestCase(true)]
    [Description("Each root owns one DI scope shared by its children and disposed before the root completes.")]
    public async Task Root_owns_scope_and_children_borrow_it(bool cleanupFails) {
        // Arrange
        var services = new ServiceCollection(); services.AddSingleton<Observations>();
        services.AddScoped<Probe>();
        services.AddKeyedScoped<IClioWorkflow, Parent>("parent");
        services.AddKeyedScoped<IClioWorkflow, Child>("child");
        services.AddSingleton(new WorkflowRegistration(new("parent", "parent"), new(new(10, 0), new(11, 0))));
        services.AddSingleton(new WorkflowRegistration(new("child", "child"), new(new(10, 0), new(11, 0))));
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var observations = provider.GetRequiredService<Observations>();
        observations.CleanupFails = cleanupFails;
        var composition = provider.GetRequiredService<IClioComposition>();
        // Act
        var first = await composition.ExecuteAsync(new("parent"));
        var second = await composition.ExecuteAsync(new("parent"));
        // Assert
        first.Accepted.Should().BeTrue("the root and child must share the same scoped dependency");
        second.Accepted.Should().BeTrue("a second root must get a functioning fresh scope");
        observations.Seen.Should().HaveCount(4, "each root and its child record their scope identity");
        observations.Seen[0].Should().Be(observations.Seen[1], "the child borrows the root scope");
        observations.Seen[2].Should().Be(observations.Seen[3], "the second child borrows its own root scope");
        observations.Seen[0].Should().NotBe(observations.Seen[2], "different roots must not share invocation state");
        observations.Disposed.Should().HaveCount(2, "both scopes must be disposed before their roots return");
    }
    /// <summary>Test-only scope observations.</summary>
    public sealed class Observations {
        /// <summary>Execution identities.</summary>
        public List<Guid> Seen { get; } = [];
        /// <summary>Whether this test injects a cleanup failure.</summary>
        public bool CleanupFails { get; set; }
        /// <summary>Disposed identities.</summary>
        public List<Guid> Disposed { get; } = [];
    }
    /// <summary>Test-only scoped resource.</summary>
    public sealed class Probe(Observations observations) : IAsyncDisposable {
        /// <summary>Identity of this scope.</summary>
        public Guid Id { get; } = Guid.NewGuid();
        /// <inheritdoc />
        public ValueTask DisposeAsync() { observations.Disposed.Add(Id); return observations.CleanupFails ? ValueTask.FromException(new IOException("fixture cleanup failure")) : ValueTask.CompletedTask; }
    }
    /// <summary>Test parent that calls a child in the same execution scope.</summary>
    public sealed class Parent(Probe probe, Observations observations) : IClioWorkflow {
        /// <inheritdoc />
        public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
            observations.Seen.Add(probe.Id); return context.InvokeAsync("child", cancellationToken);
        }
    }
    /// <summary>Test child that records the borrowed scope.</summary>
    public sealed class Child(Probe probe, Observations observations) : IClioWorkflow {
        /// <inheritdoc />
        public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
            observations.Seen.Add(probe.Id); return Task.FromResult(new OperationResult(true, "completed"));
        }
    }
}
