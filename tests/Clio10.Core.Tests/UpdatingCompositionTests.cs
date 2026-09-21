using Clio10.Contracts;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Checks complete-runtime activation and ownership independently of transport behavior.</summary>
public sealed class UpdatingCompositionTests {
    [TestCase(false)]
    [TestCase(true)]
    [Description("A pinned invalid runtime never falls back to a different release or reaches execution.")]
    public async Task Exact_pin_rejects_invalid_factory(bool wrongContract) {
        // Arrange
        var release = Substitute.For<IRuntimeBundle>();
        release.Version.Returns(new Version(10, 1)); release.RuntimeContractVersion.Returns(wrongContract ? 99 : 1);
        release.OpenComposition(Arg.Any<ICompositionHost>()).Returns(_ => throw new InvalidOperationException("broken"));
        var catalog = Substitute.For<IPrimitiveCatalog>();
        catalog.Select(Arg.Any<PrimitiveRequirement>()).Returns(call => call.Arg<PrimitiveRequirement>()!.Matches(release.Version, [])
            ? release : throw new CoreResolutionException("no-compatible-bundle", "The exact pinned release failed."));
        var services = new ServiceCollection(); services.AddSingleton(catalog);
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), ExactVersion: new(10, 1), RuntimeComposition: true));
        await using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<IRuntimeComposition>().ExecuteAsync(new("any"));
        // Assert
        result.Code.Should().Be("no-compatible-bundle", "an explicit pin must never silently select another release");
        release.ReceivedCalls().Count(x => x.GetMethodInfo().Name == nameof(IRuntimeBundle.OpenComposition)).Should().Be(wrongContract ? 0 : 1, "ABI rejection must precede factory invocation");
    }

    [Test]
    [Description("A primitive-only provider cannot masquerade as a complete runtime through a custom catalog.")]
    public async Task Primitive_only_provider_is_rejected() {
        // Arrange
        var release = Substitute.For<IPrimitiveBundle>(); release.Version.Returns(new Version(10, 1));
        var catalog = Substitute.For<IPrimitiveCatalog>();
        catalog.Select(Arg.Any<PrimitiveRequirement>()).Returns(call => call.Arg<PrimitiveRequirement>()!.Matches(release.Version, [])
            ? release : throw new CoreResolutionException("no-compatible-bundle", "No runtime."));
        var services = new ServiceCollection(); services.AddSingleton(catalog);
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), RuntimeComposition: true));
        await using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<IRuntimeComposition>().ExecuteAsync(new("any"));
        // Assert
        result.Code.Should().Be("no-compatible-bundle", "custom catalogs cannot bypass the loaded runtime factory check");
    }

    [Test]
    [Description("A composition factory failure falls back before execution and is not retried on every call.")]
    public async Task Failed_factory_retains_previous_composition() {
        // Arrange
        var bad = Substitute.For<IRuntimeBundle>();
        bad.Version.Returns(new Version(10, 1)); bad.RuntimeContractVersion.Returns(1);
        bad.OpenComposition(Arg.Any<ICompositionHost>()).Returns(_ => throw new InvalidOperationException("broken release"));
        var good = Substitute.For<IRuntimeBundle>();
        good.Version.Returns(new Version(10, 0)); good.RuntimeContractVersion.Returns(1);
        var composition = Substitute.For<IRuntimeComposition>();
        composition.ExecuteAsync(Arg.Any<CompositionRequest>(), Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, "previous-release"));
        good.OpenComposition(Arg.Any<ICompositionHost>()).Returns(composition);
        var catalog = Substitute.For<IPrimitiveCatalog>();
        catalog.Select(Arg.Any<PrimitiveRequirement>()).Returns(call =>
            call.Arg<PrimitiveRequirement>()!.Matches(bad.Version, []) ? bad : good);
        var services = new ServiceCollection(); services.AddSingleton(catalog);
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), RuntimeComposition: true));
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IRuntimeComposition>();
        // Act
        var first = await dispatcher.ExecuteAsync(new("any"));
        var second = await dispatcher.ExecuteAsync(new("any"));
        // Assert
        first.Code.Should().Be("previous-release", "activation failure occurs before any command effects and permits fallback");
        second.Code.Should().Be("previous-release", "the working release remains callable after rejection");
        bad.ReceivedCalls().Count(x => x.GetMethodInfo().Name == nameof(IRuntimeBundle.OpenComposition)).Should().Be(1, "a deterministically broken factory must not activate on every root");
        good.ReceivedCalls().Count(x => x.GetMethodInfo().Name == nameof(IRuntimeBundle.OpenComposition)).Should().Be(1, "a release owns one retained composition container");
    }

    [Test]
    [Description("An execution failure never falls back or repeats side effects on an older runtime.")]
    public async Task Execution_failure_is_not_retried_and_disposal_is_once() {
        // Arrange
        var release = Substitute.For<IRuntimeBundle>();
        release.Version.Returns(new Version(10, 1)); release.RuntimeContractVersion.Returns(1);
        var composition = Substitute.For<IRuntimeComposition>();
        composition.ExecuteAsync(Arg.Any<CompositionRequest>(), Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(false, "outcome-unknown", AcceptedSteps: ["sent"]));
        release.OpenComposition(Arg.Any<ICompositionHost>()).Returns(composition);
        var catalog = Substitute.For<IPrimitiveCatalog>(); catalog.Select(Arg.Any<PrimitiveRequirement>()).Returns(release);
        var services = new ServiceCollection(); services.AddSingleton(catalog);
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), RuntimeComposition: true));
        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IRuntimeComposition>();
        // Act
        var result = await dispatcher.ExecuteAsync(new("deploy"));
        await dispatcher.DisposeAsync(); await provider.DisposeAsync();
        // Assert
        result.Code.Should().Be("outcome-unknown", "a possibly applied operation cannot be retried during fallback");
        result.AcceptedSteps.Should().Equal(["sent"], "dispatch must retain the original partial receipt");
        composition.ReceivedCalls().Count(x => x.GetMethodInfo().Name == nameof(IClioComposition.ExecuteAsync)).Should().Be(1, "only the selected runtime may execute the call");
        composition.ReceivedCalls().Count(x => x.GetMethodInfo().Name == nameof(IAsyncDisposable.DisposeAsync)).Should().Be(1, "multiple owning aliases must not dispose a runtime twice");
    }
}
