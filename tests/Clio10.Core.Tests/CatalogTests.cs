using Clio10.Contracts;
using System.Reflection;
using System.Text.Json;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Exercises on-disk metadata rejection and fallback with an actual loadable assembly.</summary>
public sealed class CatalogTests {
    [TestCase(99, "incompatible-runtime-contract")]
    [TestCase(1, "bundle-activation-failed")]
    [Description("Runtime metadata and the actual loaded factory must both satisfy the runtime contract.")]
    public void Runtime_mode_validates_manifest_and_factory(int contract, string diagnostic) {
        // Arrange
        string root = Path.Combine(Path.GetTempPath(), "clio10-invalid-runtime-" + Guid.NewGuid());
        string directory = Path.Combine(root, "release"); Directory.CreateDirectory(directory);
        string bundles = typeof(CoreTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "BundleRoot").Value!;
        foreach (string file in Directory.GetFiles(Path.Combine(bundles, "10.0.0.0"))) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        string manifestPath = Path.Combine(directory, "bundle.json");
        var manifest = JsonSerializer.Deserialize<PrimitiveManifest>(File.ReadAllText(manifestPath))!;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { RuntimeContractVersion = contract }));
        var services = new ServiceCollection(); services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), root, RuntimeComposition: true));
        using var provider = services.BuildServiceProvider(); var catalog = provider.GetRequiredService<IPrimitiveCatalog>();
        // Act
        Action select = () => catalog.Select(new(new(10, 0), new(11, 0)));
        // Assert
        select.Should().Throw<CoreResolutionException>("metadata cannot turn a primitive-only DLL into a complete runtime");
        catalog.Diagnostics.Should().Contain(diagnostic, "the rejection stage should be visible without leaking exception details");
    }

    [Test]
    [Description("A configured nonexistent bundle directory produces a visible source failure.")]
    public void Missing_directory_has_specific_code() {
        // Arrange
        var services = new ServiceCollection();
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), Path.Combine(Path.GetTempPath(), "clio10-missing-" + Guid.NewGuid())));
        using var provider = services.BuildServiceProvider(); var catalog = provider.GetRequiredService<IPrimitiveCatalog>();
        // Act
        Action select = () => catalog.Select(new(new(10, 0), new(11, 0)));
        // Assert
        select.Should().Throw<CoreResolutionException>("a missing source is distinct from no matching versions")
            .Which.Code.Should().Be("bundle-directory-unavailable", "hosts need an actionable configuration diagnostic");
        catalog.Diagnostics.Should().Contain("bundle-directory-unavailable", "diagnostics must be accessible through the interface");
    }

    [TestCase("malformed", false)]
    [TestCase("missing-assembly", false)]
    [TestCase("mismatch", false)]
    [TestCase("duplicate", true)]
    [Description("Invalid local bundles cannot silently replace a healthy provider or make selection ambiguous.")]
    public void Invalid_candidates_are_handled(string mode, bool ambiguous) {
        // Arrange
        string root = Path.Combine(Path.GetTempPath(), "clio10-" + Guid.NewGuid());
        string bundleRoot = typeof(CoreTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "BundleRoot").Value!;
        string healthy = Path.Combine(root, "healthy");
        string other = Path.Combine(root, "other");
        Directory.CreateDirectory(healthy);
        Directory.CreateDirectory(other);
        foreach (var file in Directory.GetFiles(Path.Combine(bundleRoot, "10.0.0.0"))) File.Copy(file, Path.Combine(healthy, Path.GetFileName(file)));
        if (mode is "mismatch" or "duplicate")
            foreach (var file in Directory.GetFiles(healthy)) File.Copy(file, Path.Combine(other, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(other, "bundle.json"), mode == "malformed" ? "{" :
            JsonSerializer.Serialize(new PrimitiveManifest(mode == "duplicate" ? "10.0.0.0" : "10.9.0.0", 2, ["http"],
                "Clio10.Primitives.dll", "Clio10.Primitives.HttpPrimitiveBundle")));
        var services = new ServiceCollection();
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), root));
        try {
            using var provider = services.BuildServiceProvider();
            var catalog = provider.GetRequiredService<IPrimitiveCatalog>();
            var requirement = new PrimitiveRequirement(new(10, 0, 0, 0), new(11, 0, 0, 0));
            // Act
            Action select = () => catalog.Select(requirement);
            // Assert
            if (ambiguous) select.Should().Throw<CoreResolutionException>("duplicate versions must not be chosen by directory order")
                .Which.Code.Should().Be("ambiguous-bundle", "ambiguity must be explicit");
            else {
                catalog.Select(requirement).Version.Should().Be(new Version(10, 0, 0, 0), "the healthy older bundle remains usable");
                catalog.Select(requirement).Version.Should().Be(new Version(10, 0, 0, 0), "repeat selection must preserve the healthy fallback");
            }
        }
        finally {
            // Loaded managed assemblies may retain Windows file handles until collection.
            // This unique test directory is retained for the OS temp cleanup policy.
        }
    }

    [TestCase("{\"default\":null}", "invalid-environment")]
    [TestCase("{", "invalid-settings")]
    [TestCase("{}", "unknown-environment")]
    [Description("User-controlled settings errors become safe resolution failures.")]
    public async Task Invalid_settings_have_safe_codes(string json, string expected) {
        // Arrange
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, json);
        var services = new ServiceCollection();
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment>(), SettingsPath: file));
        using var provider = services.BuildServiceProvider();
        try {
            // Act
            Func<Task> act = () => provider.GetRequiredService<IEnvironmentResolver>().ResolveAsync("default", default);
            // Assert
            (await act.Should().ThrowAsync<CoreResolutionException>("invalid settings must not escape as implementation exceptions"))
                .Which.Code.Should().Be(expected, "the caller needs an actionable safe error code");
        }
        finally { File.Delete(file); }
    }
}
