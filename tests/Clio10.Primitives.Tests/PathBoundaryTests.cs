using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Clio10.Primitives;
using Creatio.Client;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
namespace Clio10.Tests;

/// <summary>Protects the application boundary before constructing an external client.</summary>
public sealed class PathBoundaryTests {
    [TestCase("../outside")]
    [TestCase("%2e%2e/outside")]
    [TestCase("https://elsewhere.example/")]
    [Description("Escaping application paths fail before SDK creation or network I/O.")]
    public async Task Rejects_escaping_path(string path) {
        // Arrange
        bool clientCreated = false;
        var services = new ServiceCollection();
        services.AddScoped<Func<IAsyncCreatioClient>>(_ => () => { clientCreated = true; throw new InvalidOperationException("No network expected"); });
        services.AddClioPrimitives(new(new Uri("https://example.invalid/app/"), "test", "test"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var primitive = scope.ServiceProvider.GetRequiredService<IClioPrimitive>();
        // Act
        Func<Task> call = () => primitive.ExecuteAsync(new("GET", path), default);
        // Assert
        await call.Should().ThrowAsync<ArgumentException>("operation paths must remain inside the configured application");
        clientCreated.Should().BeFalse("invalid paths must fail before authentication or external execution");
    }
}
