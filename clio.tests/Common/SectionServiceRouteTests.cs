using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>Guards the SectionService route on both Creatio deployment layouts.</summary>
[TestFixture, Property("Module", "Common")]
public sealed class SectionServiceRouteTests : BaseClioModuleTests {

	[TestCase(ServiceUrlBuilder.KnownRoute.SetConnectedEntitiesAdministratedByEntity,
		"SectionService/SetConnectedEntitiesAdministratedByEntity")]
	[Description("The SectionService endpoint retains its native REST route and the deployment-specific workspace prefix.")]
	public void Build_PreservesNativeRoute(ServiceUrlBuilder.KnownRoute route, string endpoint) {
		// Arrange
		IServiceUrlBuilder builder = Container.GetRequiredService<IServiceUrlBuilder>();
		EnvironmentSettings netCore = new() { Uri = "https://localhost/site", IsNetCore = true };
		EnvironmentSettings framework = new() { Uri = "https://localhost/site", IsNetCore = false };

		// Act
		string coreUrl = builder.Build(route, netCore);
		string frameworkUrl = builder.Build(route, framework);

		// Assert
		coreUrl.Should().Be("https://localhost/site/rest/" + endpoint,
			because: ".NET Core exposes the native REST service at the application root");
		frameworkUrl.Should().Be("https://localhost/site/0/rest/" + endpoint,
			because: ".NET Framework requires the workspace prefix before the same native endpoint");
	}

	[TestCase(ServiceUrlBuilder.KnownRoute.GetAdministratedObject,
		"ServiceModel/RightManagementService.svc/GetAdministratedObject")]
	[Description("The RightManagementService .svc endpoint retains its native ServiceModel route and the deployment-specific workspace prefix.")]
	public void Build_PreservesRightManagementRoute(ServiceUrlBuilder.KnownRoute route, string endpoint) {
		// Arrange
		IServiceUrlBuilder builder = Container.GetRequiredService<IServiceUrlBuilder>();
		EnvironmentSettings netCore = new() { Uri = "https://localhost/site", IsNetCore = true };
		EnvironmentSettings framework = new() { Uri = "https://localhost/site", IsNetCore = false };

		// Act
		string coreUrl = builder.Build(route, netCore);
		string frameworkUrl = builder.Build(route, framework);

		// Assert
		coreUrl.Should().Be("https://localhost/site/" + endpoint,
			because: ".NET Core exposes the native ServiceModel .svc service at the application root");
		frameworkUrl.Should().Be("https://localhost/site/0/" + endpoint,
			because: ".NET Framework requires the workspace prefix before the same ServiceModel .svc endpoint");
	}
}
