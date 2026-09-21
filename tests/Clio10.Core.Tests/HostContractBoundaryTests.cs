using Clio10.Contracts;
using Clio10.Core;
using Clio10.PrimitiveContracts;
using FluentAssertions;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Guards the resident host boundary against feature-specific API growth.</summary>
public sealed class HostContractBoundaryTests {
    [Test]
    [Description("Core and shared host contracts do not reference primitive feature APIs.")]
    public void Host_has_no_feature_contract_dependency() {
        // Arrange
        var host = typeof(IClioCore).Assembly;
        var shared = typeof(IPrimitiveBundle).Assembly;
        var feature = typeof(IArchivePrimitive).Assembly;
        // Act
        var references = host.GetReferencedAssemblies().Concat(shared.GetReferencedAssemblies()).Select(x => x.Name);
        // Assert
        references.Should().NotContain(feature.GetName().Name, "Core must accept new feature APIs without rebuilding");
        feature.Should().NotBeSameAs(shared, "feature contracts evolve with their runtime, not the resident host");
        shared.GetExportedTypes().Select(x => x.Name).Should().NotContain(
            [nameof(IArchivePrimitive), nameof(IClioPrimitive), nameof(IFileSystemPrimitive), nameof(IFileWriterPrimitive)],
            "the shared assembly must not grow when a feature capability is added");
    }
}
