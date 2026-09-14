using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.Administration;

/// <summary>Guards every new administration route on both Creatio deployment layouts.</summary>
[TestFixture, Property("Module", "Common")]
public sealed class AdministrationRouteTests : BaseClioModuleTests {
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationSaveRole, "AdministrationService/SaveRole")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationSaveChiefsRole, "AdministrationService/SaveChiefsRole")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationSaveUser, "AdministrationService/UpdateOrCreateUser")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationDeleteUser, "AdministrationService/DeleteUser")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationGetIsUserBlocked, "AdministrationService/GetIsUserBlocked")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationUnblockUser, "AdministrationService/UnblockUser")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationAddUserRoles, "AdministrationService/AddUserRoles")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationRemoveUsersInRoles, "AdministrationService/RemoveUsersInRoles")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationAddFunctionalRoles, "AdministrationService/AddFuncRolesInOrgRole")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationActualize, "AdministrationService/ActualizeAdminUnitInRole")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationGetLicenses, "AdministrationService/GetAvailableLicPackages")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationUpdateLicenses, "AdministrationService/UpdateLicenseInfo")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationDeleteRecords, "GridUtilitiesService/DeleteRecords")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationAddDelegation, "AdministrationService/AddSysAdminUnitGrantedRights")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationRemoveDelegation, "AdministrationService/RemoveSysAdminUnitGrantedRights")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationRemoveFunctionalRole, "CreatioApiGateway/RemoveFunctionalRoleAssociation")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationRedistributeRoleLicenses, "CreatioApiGateway/ScheduleRoleLicenseRedistribution")]
	[TestCase(ServiceUrlBuilder.KnownRoute.RightsSetOperationGrantee, "RightsService/SetAdminOperationGrantee")]
	[TestCase(ServiceUrlBuilder.KnownRoute.RightsSetOperationPosition, "RightsService/SetAdminOperationGranteePosition")]
	[TestCase(ServiceUrlBuilder.KnownRoute.RightsDeleteOperationGrantee, "RightsService/DeleteAdminOperationGrantee")]
	[TestCase(ServiceUrlBuilder.KnownRoute.AdministrationInvalidateRightsCache, "CreatioApiGateway/InvalidateAdministrationRightsCache")]
	[Description("Administration endpoints retain their native route and the deployment-specific workspace prefix.")]
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
}
