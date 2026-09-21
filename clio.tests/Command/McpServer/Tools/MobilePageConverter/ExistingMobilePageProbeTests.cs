using System;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ExistingMobilePageProbeTests {

	private const string PackageUId = "11111111-1111-1111-1111-111111111111";
	private const string PageSchemaUId = "33333333-3333-3333-3333-333333333333";
	private const string EntitySchemaUId = "44444444-4444-4444-4444-444444444444";
	private const string MobileRoot = "BaseMobilePageTemplate";

	/// <summary>
	/// A stand-in environment whose DataService SelectQuery answers come from <paramref name="select"/>,
	/// keyed on the serialized query so a test can answer the page and object lookups differently.
	/// </summary>
	private sealed record EnvironmentStub {

		public IToolCommandResolver Resolver { get; init; }

		public IApplicationClient Client { get; init; }

		public IAddonSchemaDesignerClient AddonClient { get; init; }
	}

	private static EnvironmentStub Environment(Func<string, string> select, string addonMetaData = null) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => select(callInfo.ArgAt<string>(1)));

		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("/DataService/json/SyncReply/SelectQuery");

		IAddonSchemaDesignerClient addonClient = Substitute.For<IAddonSchemaDesignerClient>();
		addonClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(_ => new AddonSchemaDto { MetaData = addonMetaData ?? string.Empty });

		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		resolver.Resolve<IAddonSchemaDesignerClient>(Arg.Any<EnvironmentOptions>()).Returns(addonClient);

		return new EnvironmentStub { Resolver = resolver, Client = client, AddonClient = addonClient };
	}

	/// <summary>A successful SelectQuery envelope carrying <paramref name="rows"/> (raw JSON objects).</summary>
	private static string Rows(params string[] rows) => $"{{\"success\":true,\"rows\":[{string.Join(",", rows)}]}}";

	private static string PageRow(string name, string parentName, string uId = PageSchemaUId) =>
		$"{{\"Name\":\"{name}\",\"UId\":\"{uId}\",\"ParentName\":{(parentName is null ? "null" : $"\"{parentName}\"")}}}";

	private static string EntityRow(string name, bool extendParent = false, string uId = EntitySchemaUId) =>
		$"{{\"Name\":\"{name}\",\"UId\":\"{uId}\",\"ExtendParent\":{(extendParent ? "true" : "false")}}}";

	/// <summary>Routes a serialized SelectQuery to the page-schema or object-schema answer.</summary>
	private static Func<string, string> Route(string pageRows, string entityRows) =>
		query => query.Contains("EntitySchemaManager") ? entityRows : pageRows;

	[Test]
	[Description("The source page's own bound entity already has a default mobile edit page: ProbeSourceEntityDefaultMobilePage resolves its schema name for the reuse-vs-convert check.")]
	public void ProbeSourceEntityDefaultMobilePage_EntityHasDefault_ResolvesItsSchemaName() {
		// Arrange — entity name -> UId (EntitySchemaManager branch), mobile add-on declares an untyped default,
		// and the reverse UId -> name lookup (page branch) resolves that default page's name.
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("LeadProduct_MobileFormPage", MobileRoot, uId: PageSchemaUId)), Rows(EntityRow("LeadProduct"))),
			addonMetaData: $"{{\"Pages\":[{{\"PageSchemaUId\":\"{PageSchemaUId}\",\"IsDefault\":true}}]}}");

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSourceEntityDefaultMobilePage(
			environment.Resolver, "env", null, null, null, "LeadProduct", PackageUId);

		// Assert
		result.Should().NotBeNull(because: "the entity's MobileRelatedPage add-on declares an untyped default");
		result.SchemaName.Should().Be("LeadProduct_MobileFormPage");
		result.SchemaUId.Should().Be(PageSchemaUId);
		result.Source.Should().Be(MobileActionTargetProbe.KindEntityDefaultMobilePage);
	}

	[Test]
	[Description("The source page's own bound entity has no default mobile edit page: ProbeSourceEntityDefaultMobilePage returns null rather than guessing one.")]
	public void ProbeSourceEntityDefaultMobilePage_EntityHasNoDefault_ReturnsNull() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(EntityRow("LeadProduct"))), addonMetaData: "{\"Pages\":[]}");

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSourceEntityDefaultMobilePage(
			environment.Resolver, "env", null, null, null, "LeadProduct", PackageUId);

		// Assert
		result.Should().BeNull(because: "the object has no default mobile page to reuse");
	}

	[Test]
	[Description("The entity name cannot be resolved to a schema UId at all: ProbeSourceEntityDefaultMobilePage returns null and never reads the add-on.")]
	public void ProbeSourceEntityDefaultMobilePage_EntityUnresolvable_ReturnsNullWithoutReadingAddon() {
		// Arrange — no SysSchema row for the object at all.
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSourceEntityDefaultMobilePage(
			environment.Resolver, "env", null, null, null, "UnknownObject", PackageUId);

		// Assert
		result.Should().BeNull();
		environment.AddonClient.ReceivedCalls().Should().BeEmpty(
			because: "there is no entity UId to address the add-on read with");
	}

	[Test]
	[Description("A missing/unparseable package UId means the add-on read can never be addressed: ProbeSourceEntityDefaultMobilePage returns null without touching the environment.")]
	public void ProbeSourceEntityDefaultMobilePage_UnparseablePackageUId_ReturnsNullWithoutReadingAnything() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows(EntityRow("LeadProduct"))));

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSourceEntityDefaultMobilePage(
			environment.Resolver, "env", null, null, null, "LeadProduct", "not-a-guid");

		// Assert
		result.Should().BeNull();
		environment.Client.ReceivedCalls().Should().BeEmpty(because: "no read can be addressed without a package UId");
	}

	[Test]
	[Description("A THROW anywhere in the resolve-entity/read-addon/resolve-name chain degrades to null rather than propagating — this probe never throws.")]
	public void ProbeSourceEntityDefaultMobilePage_ThrowingRead_DegradesToNull() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows(EntityRow("LeadProduct"))));
		environment.AddonClient.GetSchema(Arg.Any<AddonGetRequestDto>()).Returns(_ => throw new InvalidOperationException("boom"));

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSourceEntityDefaultMobilePage(
			environment.Resolver, "env", null, null, null, "LeadProduct", PackageUId);

		// Assert
		result.Should().BeNull();
	}

	[Test]
	[Description("An already-known section mobile page UId resolves to its schema name for the reuse-vs-convert check.")]
	public void ProbeSectionMobilePage_ResolvesSchemaName() {
		// Arrange — the reverse UId -> name lookup is the same non-EntitySchemaManager query family the
		// candidate batch (SchemaNameResolver.ResolveNames) uses, so it is routed through the page-rows side of Route.
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("UsrApp_MobileListPage", MobileRoot, uId: PageSchemaUId)), Rows()));

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSectionMobilePage(
			environment.Resolver, "env", null, null, null, PageSchemaUId);

		// Assert
		result.Should().NotBeNull();
		result.SchemaName.Should().Be("UsrApp_MobileListPage");
		result.SchemaUId.Should().Be(PageSchemaUId);
		result.Source.Should().Be(ExistingMobilePageProbe.KindSection);
	}

	[Test]
	[Description("No row resolves for the given UId: ProbeSectionMobilePage returns null rather than fabricating a name.")]
	public void ProbeSectionMobilePage_UnresolvableUId_ReturnsNull() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSectionMobilePage(
			environment.Resolver, "env", null, null, null, PageSchemaUId);

		// Assert
		result.Should().BeNull();
	}

	[Test]
	[Description("A blank UId is rejected before any read is attempted.")]
	public void ProbeSectionMobilePage_BlankUId_ReturnsNullWithoutReading() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		ExistingMobilePageInfo result = ExistingMobilePageProbe.ProbeSectionMobilePage(
			environment.Resolver, "env", null, null, null, "  ");

		// Assert
		result.Should().BeNull();
		environment.Client.ReceivedCalls().Should().BeEmpty();
	}
}
