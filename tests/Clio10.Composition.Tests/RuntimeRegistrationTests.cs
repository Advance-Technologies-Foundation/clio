using Clio10.Composition;
using Clio10.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Dynamic mode must never silently discard host-registered workflows.</summary>
public sealed class RuntimeRegistrationTests {
    [TestCase(false)]
    [TestCase(true)]
    [Description("Host workflow metadata is rejected whether registered before or after dynamic Composition setup.")]
    public async Task Host_workflows_are_rejected_in_either_registration_order(bool after) {
        // Arrange
        var services = new ServiceCollection();
        var registration = new WorkflowRegistration(new("partner.host", "Host-only workflow."), new(new(10, 0), new(11, 0)));
        if (!after) services.AddSingleton(registration);
        var options = new CompositionOptions(BundleDirectory: Path.Combine(Path.GetTempPath(), "unused-runtime-cache"), RuntimeComposition: true);
        // Act
        Action setup = () => services.AddClioComposition(options);
        // Assert
        if (!after) setup.Should().Throw<ArgumentException>("existing host workflows cannot be silently ignored in runtime mode");
        else {
            setup(); services.AddSingleton(registration);
            await using var provider = services.BuildServiceProvider();
            Action resolve = () => provider.GetRequiredService<IClioComposition>();
            resolve.Should().Throw<CoreResolutionException>("late registrations need the same boundary enforcement")
                .Which.Code.Should().Be("host-workflows-not-supported", "the caller needs an actionable configuration code");
        }
    }
}
