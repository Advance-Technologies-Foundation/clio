using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>Guards the SsoSecurityEventsSettingsService routes on both Creatio deployment layouts.</summary>
[TestFixture, Category("Unit"), Property("Module", "Common")]
public sealed class SsoSecurityEventsRouteTests : BaseClioModuleTests {

	[TestCase(ServiceUrlBuilder.KnownRoute.SsoSecurityEventsGetProviderState, "GetProviderState")]
	[TestCase(ServiceUrlBuilder.KnownRoute.SsoSecurityEventsEnableEvents, "EnableEvents")]
	[TestCase(ServiceUrlBuilder.KnownRoute.SsoSecurityEventsDisableEvents, "DisableEvents")]
	[TestCase(ServiceUrlBuilder.KnownRoute.SsoSecurityEventsSendTestEvent, "SendTestEvent")]
	[TestCase(ServiceUrlBuilder.KnownRoute.SsoSecurityEventsSetSiteAddress, "SetSiteAddress")]
	[TestCase(ServiceUrlBuilder.KnownRoute.SsoSecurityEventsUpsertProvider, "UpsertProvider")]
	[TestCase(ServiceUrlBuilder.KnownRoute.SsoSecurityEventsRemoveProvider, "RemoveProvider")]
	[Description("Each settings-service method is served under rest/SsoSecurityEventsSettingsService, with the 0/ workspace prefix on .NET Framework only.")]
	public void Build_ShouldTargetTheSettingsServiceMethod_WhenBuildingAnSsoSecurityEventsRoute(
		ServiceUrlBuilder.KnownRoute route, string method) {
		// Arrange
		IServiceUrlBuilder builder = Container.GetRequiredService<IServiceUrlBuilder>();
		EnvironmentSettings netCore = new() { Uri = "https://localhost/site", IsNetCore = true };
		EnvironmentSettings framework = new() { Uri = "https://localhost/site", IsNetCore = false };

		// Act
		string coreUrl = builder.Build(route, netCore);
		string frameworkUrl = builder.Build(route, framework);

		// Assert
		coreUrl.Should().Be("https://localhost/site/rest/SsoSecurityEventsSettingsService/" + method,
			because: ".NET Core serves the configuration service at the application root");
		frameworkUrl.Should().Be("https://localhost/site/0/rest/SsoSecurityEventsSettingsService/" + method,
			because: ".NET Framework serves the same service under the 0/ workspace prefix");
	}

}
