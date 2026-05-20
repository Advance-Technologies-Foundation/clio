using System;
using Clio.Command;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using DesignerResponse = Clio.Command.EntitySchemaDesigner.DesignerResponse<Clio.Command.EntitySchemaDesigner.EntityDesignSchemaDto>;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class FilterSchemaProviderFallbackTests {

	[Test]
	[Category("Unit")]
	[Description("Happy path: when GetSchemaDesignItem returns a schema on the first (empty PackageUId) call, package discovery is never invoked.")]
	public void GetSchemaColumns_Should_Not_Discover_Package_When_Empty_PackageUId_Returns_Schema() {
		IRemoteEntitySchemaDesignerClient client = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		ISchemaPackageDiscovery discovery = Substitute.For<ISchemaPackageDiscovery>();
		client.TryGetSchemaDesignItem(
				Arg.Is<GetSchemaDesignItemRequestDto>(req => req.Name == "Contact" && req.PackageUId == Guid.Empty),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new DesignerResponse {
				Success = true,
				Schema = new EntityDesignSchemaDto { Name = "Contact" }
			});
		FilterSchemaProvider provider = new(client, discovery);

		System.Action act = () => provider.GetSchemaColumns("Contact");

		act.Should().NotThrow();
		discovery.DidNotReceiveWithAnyArgs().TryFindRootPackageUId(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Fallback path: when the empty-PackageUId call returns null, discovery resolves the owning package UId and the retry call succeeds.")]
	public void GetSchemaColumns_Should_Retry_With_Discovered_PackageUId_When_Empty_Call_Returns_Null() {
		Guid discoveredPackage = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		IRemoteEntitySchemaDesignerClient client = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		ISchemaPackageDiscovery discovery = Substitute.For<ISchemaPackageDiscovery>();
		client.TryGetSchemaDesignItem(
				Arg.Is<GetSchemaDesignItemRequestDto>(req => req.Name == "City" && req.PackageUId == Guid.Empty),
				Arg.Any<RemoteCommandOptions>())
			.Returns((DesignerResponse?)null);
		client.TryGetSchemaDesignItem(
				Arg.Is<GetSchemaDesignItemRequestDto>(req => req.Name == "City" && req.PackageUId == discoveredPackage),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new DesignerResponse {
				Success = true,
				Schema = new EntityDesignSchemaDto { Name = "City" }
			});
		discovery.TryFindRootPackageUId("City").Returns(discoveredPackage);
		FilterSchemaProvider provider = new(client, discovery);

		System.Action act = () => provider.GetSchemaColumns("City");

		act.Should().NotThrow();
		discovery.Received(1).TryFindRootPackageUId("City");
	}

	[Test]
	[Category("Unit")]
	[Description("Negative path: when both the empty-PackageUId call and discovery yield no result, the original PathUnknown error is preserved.")]
	public void GetSchemaColumns_Should_Throw_PathUnknown_When_Discovery_Returns_Null() {
		IRemoteEntitySchemaDesignerClient client = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		ISchemaPackageDiscovery discovery = Substitute.For<ISchemaPackageDiscovery>();
		client.TryGetSchemaDesignItem(
				Arg.Any<GetSchemaDesignItemRequestDto>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns((DesignerResponse?)null);
		discovery.TryFindRootPackageUId("NoSuchSchema").Returns((Guid?)null);
		FilterSchemaProvider provider = new(client, discovery);

		System.Action act = () => provider.GetSchemaColumns("NoSuchSchema");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*NoSuchSchema*could not be resolved*");
	}

	[Test]
	[Category("Unit")]
	[Description("Cache invariant: a second call for the same schema name reuses the cached fetch (no retry path) even when the first call needed the discovery fallback.")]
	public void GetSchemaColumns_Should_Cache_Resolved_Schema_Across_Multiple_Calls() {
		Guid discoveredPackage = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		IRemoteEntitySchemaDesignerClient client = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		ISchemaPackageDiscovery discovery = Substitute.For<ISchemaPackageDiscovery>();
		client.TryGetSchemaDesignItem(
				Arg.Is<GetSchemaDesignItemRequestDto>(req => req.PackageUId == Guid.Empty),
				Arg.Any<RemoteCommandOptions>())
			.Returns((DesignerResponse?)null);
		client.TryGetSchemaDesignItem(
				Arg.Is<GetSchemaDesignItemRequestDto>(req => req.PackageUId == discoveredPackage),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new DesignerResponse {
				Success = true,
				Schema = new EntityDesignSchemaDto { Name = "City" }
			});
		discovery.TryFindRootPackageUId("City").Returns(discoveredPackage);
		FilterSchemaProvider provider = new(client, discovery);

		provider.GetSchemaColumns("City");
		provider.GetSchemaColumns("City");

		discovery.Received(1).TryFindRootPackageUId("City");
		client.Received(1).TryGetSchemaDesignItem(
			Arg.Is<GetSchemaDesignItemRequestDto>(req => req.PackageUId == discoveredPackage),
			Arg.Any<RemoteCommandOptions>());
	}
}
