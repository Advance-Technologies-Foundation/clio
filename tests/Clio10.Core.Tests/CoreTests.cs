using Clio10.PrimitiveContracts;
using System.Reflection;
using System.Runtime.Loader;
using Clio10.Contracts;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
namespace Clio10.Tests;

/// <summary>Tests real bundle selection and callback-owned lifetime rules.</summary>
public sealed class CoreTests {
    private static string BundleRoot => typeof(CoreTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "BundleRoot").Value!;
    private static PrimitiveRequirement Requirement(Version? exact = null) => new(new(10, 0, 0, 0), new(11, 0, 0, 0), exact);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestCase("10.0")]
    [TestCase("10.0.0")]
    [Description("Short numeric version pins resolve the same four-component assembly release.")]
    public async Task Short_version_pins_select_the_release(string version) {
        // Arrange
        await using var provider = Build();
        // Act
        var selected = await provider.GetRequiredService<IClioCore>().RunAsync("one", Requirement(Version.Parse(version)),
            (context, _) => Task.FromResult(context.PrimitiveVersion), default);
        // Assert
        selected.Should().Be(new Version(10, 0, 0, 0), "omitted build or revision components mean zero");
    }

    [Test]
    [Description("Real same-name assemblies coexist while concurrent roots pin their own complete bundle.")]
    public async Task Versions_coexist_and_are_pinned() {
        // Arrange
        await using var provider = Build();
        var core = provider.GetRequiredService<IClioCore>();
        var entered = Signal(); var finish = Signal();
        Assembly? firstAssembly = null;
        // Act
        var first = core.RunAsync("one", Requirement(new(10, 0, 0, 0)), async (context, _) => {
            firstAssembly = context.Get<IClioPrimitive>().GetType().Assembly;
            entered.SetResult(); await finish.Task;
            return context.PrimitiveVersion;
        }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = await core.RunAsync("two", Requirement(), (context, _) => Task.FromResult((context.PrimitiveVersion, Assembly: context.Get<IClioPrimitive>().GetType().Assembly)), default);
        finish.SetResult();
        var firstVersion = await first;
        // Assert
        firstVersion.Should().Be(new Version(10, 0, 0, 0), "the running workflow keeps its selected release");
        second.PrimitiveVersion.Should().Be(new Version(10, 1, 0, 0), "another root selects the newest compatible bundle");
        firstAssembly.Should().NotBeSameAs(second.Assembly, "these are independently loaded real assemblies");
        AssemblyLoadContext.GetLoadContext(firstAssembly!).Should().NotBeSameAs(AssemblyLoadContext.GetLoadContext(second.Assembly), "private dependencies belong to separate load contexts");
    }
    [Test]
    [Description("An unavailable exact version fails before invoking the callback.")]
    public async Task Unsupported_version_fails_fast() {
        // Arrange
        await using var provider = Build();
        bool invoked = false;
        // Act
        Func<Task> run = () => provider.GetRequiredService<IClioCore>().RunAsync("one", Requirement(new(10, 9, 0, 0)), (_, _) => { invoked = true; return Task.FromResult(0); }, default);
        // Assert
        await run.Should().ThrowAsync<CoreResolutionException>("an exact missing bundle cannot silently fall back");
        invoked.Should().BeFalse("selection must precede execution");
    }
    [Test]
    [Description("Independent same-target roots queue and run after the first callback releases ownership.")]
    public async Task Same_target_is_coordinated() {
        // Arrange
        await using var provider = Build(); var core = provider.GetRequiredService<IClioCore>();
        var entered = Signal(); var finish = Signal();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Act
        var first = core.RunAsync("one", Requirement(), async (_, _) => { entered.SetResult(); await finish.Task; return 1; }, timeout.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var next = core.RunAsync("alias", Requirement(), (_, _) => Task.FromResult(2), timeout.Token);
        bool waiting = !next.IsCompleted;
        finish.SetResult(); await first;
        var value = await next;
        // Assert
        waiting.Should().BeTrue("aliases of one target share the gate");
        value.Should().Be(2, "a separate top-level call is allowed after ownership ends");
    }
    [Test]
    [Description("Cancelling an already queued waiter does not release another root's gate.")]
    public async Task Cancelled_wait_preserves_owner() {
        // Arrange
        await using var provider = Build(); var core = provider.GetRequiredService<IClioCore>();
        var entered = Signal(); var finish = Signal();
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var first = core.RunAsync("one", Requirement(), async (_, _) => { entered.SetResult(); await finish.Task; return 1; }, timeout.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        // Act
        var waiting = core.RunAsync("one", Requirement(), (_, _) => Task.FromResult(2), cancellation.Token);
        waiting.IsCompleted.Should().BeFalse("the waiter must queue before it is cancelled");
        cancellation.Cancel();
        Func<Task> cancelled = () => waiting;
        await cancelled.Should().ThrowAsync<OperationCanceledException>("the caller cancelled while waiting");
        var third = core.RunAsync("alias", Requirement(), (_, _) => Task.FromResult(3), timeout.Token);
        bool thirdWaits = !third.IsCompleted;
        finish.SetResult(); await first; await third;
        // Assert
        thirdWaits.Should().BeTrue("a cancelled waiter must not release the owner's gate");
    }
    [Test]
    [Description("Filesystem needs select V2 and reject an exact HTTP-only V1 before execution.")]
    public async Task Capability_selection_differs_between_versions() {
        // Arrange
        await using var provider = Build(); var core = provider.GetRequiredService<IClioCore>();
        var requirement = Requirement() with { Capabilities = new[] { "filesystem" } };
        // Act
        var selected = await core.RunAsync("one", requirement, (context, _) => {
            context.Get<IFileSystemPrimitive>().Should().NotBeNull("the selected bundle must provide a typed filesystem capability");
            return Task.FromResult(context.PrimitiveVersion);
        }, default);
        Func<Task> old = () => core.RunAsync("two", requirement with { Exact = new(10, 0, 0, 0) }, (_, _) => Task.FromResult(0), default);
        // Assert
        selected.Should().Be(new Version(10, 1, 0, 0), "only V2 implements filesystem");
        await old.Should().ThrowAsync<CoreResolutionException>("V1 cannot satisfy filesystem work");
    }
    [TestCase("one")]
    [TestCase("alias")]
    [TestCase("two")]
    [Description("A root cannot open another root on its Core, even using aliases or a different target.")]
    public async Task Nested_root_is_rejected(string nestedEnvironment) {
        // Arrange
        await using var provider = Build(); var core = provider.GetRequiredService<IClioCore>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Act
        Func<Task> nested = () => core.RunAsync("one", Requirement(), (_, token) => core.RunAsync(nestedEnvironment, Requirement(), (_, _) => Task.FromResult(1), token), timeout.Token);
        var failure = await nested.Should().ThrowAsync<CoreResolutionException>("nested roots must fail before waiting on any gate");
        var next = await core.RunAsync("one", Requirement(), (_, _) => Task.FromResult(2), timeout.Token);
        // Assert
        failure.Which.Code.Should().Be("nested-root-not-supported", "the host must use separate roots for multi-target work");
        next.Should().Be(2, "the rejected nested call must leave the Core usable");
    }
    private static ServiceProvider Build() {
        var services = new ServiceCollection();
        var first = new ClioEnvironment(new Uri("http://127.0.0.1:1/"), "test", "test");
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment> { ["one"] = first, ["alias"] = first, ["two"] = first with { BaseUri = new Uri("http://127.0.0.1:2/") } }, BundleRoot) { SharedCapabilityAssemblies = [typeof(IClioPrimitive).Assembly] });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
