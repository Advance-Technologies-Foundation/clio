using Clio10.Contracts;
using Clio10.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Partner.Composition;
namespace Clio10.Tests;

/// <summary>Proves reusable plugin references and ordinary typed library results.</summary>
public sealed class CapabilityExtensionTests {
    [Test]
    [Description("The partner assembly references only contracts and DI, not Core, Composition, Primitives or SDKs.")]
    public void Partner_contract_dependency_is_isolated() {
        // Arrange
        var references = typeof(CompareTexts).Assembly.GetReferencedAssemblies().Select(x => x.Name);
        // Act
        var forbidden = references.Where(x => x is "Clio10.Core" or "Clio10.Composition" or "Clio10.Primitives" || x!.Contains("Creatio.Client") || x.Contains("ModelContextProtocol"));
        // Assert
        forbidden.Should().BeEmpty("extension authors need the supported contracts, not the vendor implementation graph");
    }
    [Test]
    [Description("An embedded host receives a typed partner result while the filesystem capability performs the I/O.")]
    public async Task Managed_host_receives_typed_payload() {
        // Arrange
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "hello");
        var services = new ServiceCollection();
        services.AddClioComposition(new Clio10.Core.CoreOptions(new Dictionary<string, ClioEnvironment> { ["default"] = new() }));
        services.AddPartnerWorkflows();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        try {
            // Act
            var result = await scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new("partner.inspect-text",
                Arguments: new Dictionary<string, object?> { ["path"] = path, ["label"] = "sample" }));
            // Assert
            result.Payload.Should().BeOfType<TextSummary>("library callers should consume typed results without parsing JSON");
            ((TextSummary)result.Payload!).Characters.Should().Be(5, "the actual filesystem implementation read the data");
        }
        finally { File.Delete(path); }
    }
}
