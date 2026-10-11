using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>Guards the file design mode probe route on both Creatio deployment layouts.</summary>
[TestFixture, Property("Module", "Common")]
public sealed class FileDesignModeRouteTests : BaseClioModuleTests {

	[Test]
	[Description("GetIsFileDesignMode keeps its native WorkspaceExplorerService route and the deployment-specific workspace prefix.")]
	public void Build_ShouldPreserveTheNativeRoute_WhenBuildingTheFileDesignModeRoute() {
		// Arrange
		IServiceUrlBuilder builder = Container.GetRequiredService<IServiceUrlBuilder>();
		EnvironmentSettings netCore = new() { Uri = "https://localhost/site", IsNetCore = true };
		EnvironmentSettings framework = new() { Uri = "https://localhost/site", IsNetCore = false };
		const string endpoint = "ServiceModel/WorkspaceExplorerService.svc/GetIsFileDesignMode";

		// Act
		string coreUrl = builder.Build(ServiceUrlBuilder.KnownRoute.GetIsFileDesignMode, netCore);
		string frameworkUrl = builder.Build(ServiceUrlBuilder.KnownRoute.GetIsFileDesignMode, framework);

		// Assert
		coreUrl.Should().Be("https://localhost/site/" + endpoint,
			because: ".NET Core exposes the WorkspaceExplorerService at the application root");
		frameworkUrl.Should().Be("https://localhost/site/0/" + endpoint,
			because: ".NET Framework requires the workspace prefix before the same service");
	}
}
