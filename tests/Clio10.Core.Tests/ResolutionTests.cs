using Clio10.PrimitiveContracts;
using System.Text.Json;
using Clio10.Contracts;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Exercises configuration and compatibility failures before execution starts.</summary>
public sealed class ResolutionTests {
    [Test]
    [Description("A settings change affects the next workflow but cannot mutate an existing environment snapshot.")]
    public async Task Settings_are_snapshots_per_workflow() {
        // Arrange
        string file = Path.GetTempFileName();
        var original = new ClioEnvironment(new Uri("http://localhost/one/"), "test", "test");
        var services = new ServiceCollection();
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), SettingsPath: file));
        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IEnvironmentResolver>();
        try {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new Dictionary<string, ClioEnvironment> { ["lab"] = original }));
            // Act
            var first = await resolver.ResolveAsync("lab", default);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new Dictionary<string, ClioEnvironment> { ["lab"] = original with { BaseUri = new("http://localhost/two/") } }));
            var second = await resolver.ResolveAsync("lab", default);
            // Assert
            first.BaseUri!.AbsolutePath.Should().Be("/one/", "a running workflow retains its snapshot");
            second.BaseUri!.AbsolutePath.Should().Be("/two/", "the next workflow resolves current settings");
        }
        finally { File.Delete(file); }
    }

    [TestCase(3, "http", "no-compatible-bundle")]
    [TestCase(2, "other", "no-compatible-bundle")]
    [Description("Contract versions and capability names are enforced independently of assembly version.")]
    public void Incompatible_provider_is_not_selected(int contract, string capability, string expectedCode) {
        // Arrange
        var bundle = Substitute.For<IPrimitiveBundle>();
        bundle.Version.Returns(new Version(10, 0, 0, 0));
        bundle.ContractVersion.Returns(contract);
        bundle.Capabilities.Returns(new[] { capability });
        var services = new ServiceCollection();
        services.AddSingleton(bundle);
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>()));
        using var provider = services.BuildServiceProvider();
        // Act
        Action act = () => provider.GetRequiredService<IPrimitiveCatalog>().Select(new(new(10, 0, 0, 0), new(11, 0, 0, 0), Capabilities: ["http"]));
        // Assert
        act.Should().Throw<CoreResolutionException>("numeric version alone does not establish compatibility")
            .Which.Code.Should().Be(expectedCode, "callers need a structured compatibility failure");
        bundle.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "OpenSession").Should().Be(0, "selection cannot start external work");
    }

    [Test]
    [Description("Credential carriers redact secrets when formatted for diagnostics.")]
    public void Diagnostic_formatting_redacts_credentials() {
        // Arrange
        const string secret = "test-secret-never-log";
        var request = new PrimitiveRequest("POST", "auth", secret);
        var session = new PrimitiveSessionOptions(new("http://localhost/"), "user", secret);
        // Act
        string formatted = request + " " + session;
        // Assert
        formatted.Should().NotContain(secret, "routine diagnostic formatting must not leak credentials");
    }
}
