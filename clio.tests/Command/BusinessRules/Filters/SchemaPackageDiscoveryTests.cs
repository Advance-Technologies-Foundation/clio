using System;
using Clio.Command.BusinessRules.Filters;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class SchemaPackageDiscoveryTests {

	[Test]
	[Category("Unit")]
	[Description("When SysSchema returns multiple rows for one name (e.g. City exists in CrtCoreBase as root plus SSP/CrtGoogleAnalytics extensions), the discovery picks the row whose parent schema name differs from its own — the root definition — and returns that package UId.")]
	public void TryFindRootPackageUId_Should_Prefer_Row_Whose_Parent_Differs_From_Self() {
		Guid coreBaseUId = Guid.Parse("11111111-1111-1111-1111-111111111111");
		Guid sspUId = Guid.Parse("22222222-2222-2222-2222-222222222222");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://test/select");
		// Order: extension row first, root row second — verifies we don't just take rows[0].
		string responseJson = $$"""
		{
			"success": true,
			"rows": [
				{
					"Name": "City",
					"PackageUId": "{{sspUId}}",
					"PackageName": "SSP",
					"ParentSchemaName": "City"
				},
				{
					"Name": "City",
					"PackageUId": "{{coreBaseUId}}",
					"PackageName": "CrtCoreBase",
					"ParentSchemaName": "BaseLookup"
				}
			]
		}
		""";
		client.ExecutePostRequest("http://test/select", Arg.Any<string>()).Returns(responseJson);
		SchemaPackageDiscovery discovery = new(client, urlBuilder);

		Guid? result = discovery.TryFindRootPackageUId("City");

		result.Should().Be(coreBaseUId);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns null when SysSchema yields no rows for the given name, so the caller can surface the original PathUnknown error to the user.")]
	public void TryFindRootPackageUId_Should_Return_Null_When_No_Rows() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://test/select");
		client.ExecutePostRequest("http://test/select", Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		SchemaPackageDiscovery discovery = new(client, urlBuilder);

		Guid? result = discovery.TryFindRootPackageUId("NoSuchSchema");

		result.Should().BeNull();
	}
}
