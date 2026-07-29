using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="ClassicSectionSchemaResolver"/> — the metadata-first section lookup that reaches
/// sections whose schema name cannot be derived from the entity or page name (renamed sections, or names carrying a
/// UId/app infix such as <c>ASPContractDatac145c7efSection</c>).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ClassicSectionSchemaResolverTests {

	private const string EntityUId = "11111111-1111-1111-1111-111111111111";
	private const string SectionUId = "22222222-2222-2222-2222-222222222222";
	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

	private static string Rows(params string[] rows) =>
		"{\"rows\":[" + string.Join(",", rows) + "],\"success\":true}";

	// Routes each SelectQuery to a canned response by root schema and shape, so one substitute serves the
	// resolver's three-step lookup (entity UId -> SysModule bindings -> section schema names).
	private static IApplicationClient ClientReturning(
		string entityResponse, string moduleResponse, string schemaResponse) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(default, default).ReturnsForAnyArgs(ci => {
			JObject query = JObject.Parse(ci.ArgAt<string>(1));
			string root = query["rootSchemaName"]?.ToString();
			if (root == "SysModule") {
				return moduleResponse;
			}
			// Both SysSchema calls share a root; the entity lookup is the one selecting ExtendParent.
			bool isEntityLookup = query["columns"]?["items"]?["ExtendParent"] != null;
			return isEntityLookup ? entityResponse : schemaResponse;
		});
		return client;
	}

	private static ClassicSectionSchemaResolver Create(IApplicationClient client) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns("http://localhost/svc");
		return new ClassicSectionSchemaResolver(client, urlBuilder);
	}

	[Test]
	[Description("ResolveSectionSchemaNames returns the section schema name bound through SysModule even when the name is not derivable from the entity name.")]
	public void ResolveSectionSchemaNames_ShouldReturnBoundSection_WhenNameIsNotDerivable() {
		// Arrange
		IApplicationClient client = ClientReturning(
			Rows($"{{\"UId\":\"{EntityUId}\",\"ExtendParent\":false}}"),
			Rows($"{{\"SectionSchemaUId\":\"{SectionUId}\"}}"),
			Rows($"{{\"UId\":\"{SectionUId}\",\"Name\":\"ASPContractDatac145c7efSection\"}}"));
		ClassicSectionSchemaResolver resolver = Create(client);

		// Act
		ClassicSectionLookup result = resolver.ResolveSectionSchemaNames("ASPContractData");

		// Assert
		result.Error.Should().BeNull(because: "a complete three-step lookup is not a failure");
		result.SectionSchemaNames.Should().ContainSingle(
				because: "one SysModule row bound one section schema to the entity")
			.Which.Should().Be("ASPContractDatac145c7efSection",
				because: "the resolved name comes from metadata, not from a naming convention");
	}

	[Test]
	[Description("ResolveSectionSchemaNames returns an empty result without an error when the entity has no SysModule section.")]
	public void ResolveSectionSchemaNames_ShouldReturnEmptyWithoutError_WhenEntityHasNoSection() {
		// Arrange
		IApplicationClient client = ClientReturning(
			Rows($"{{\"UId\":\"{EntityUId}\",\"ExtendParent\":false}}"), Rows(), Rows());
		ClassicSectionSchemaResolver resolver = Create(client);

		// Act
		ClassicSectionLookup result = resolver.ResolveSectionSchemaNames("UsrNoSection");

		// Assert
		result.Error.Should().BeNull(
			because: "an entity with no Classic section is a legitimate outcome, not a lookup failure");
		result.SectionSchemaNames.Should().BeEmpty(because: "no SysModule row bound a section");
	}

	[Test]
	[Description("ResolveSectionSchemaNames skips SysModule rows whose SectionSchemaUId is the empty Guid, so a card-only module does not produce a bogus candidate.")]
	public void ResolveSectionSchemaNames_ShouldSkipEmptyGuidBindings() {
		// Arrange
		IApplicationClient client = ClientReturning(
			Rows($"{{\"UId\":\"{EntityUId}\",\"ExtendParent\":false}}"),
			Rows($"{{\"SectionSchemaUId\":\"{EmptyGuid}\"}}"),
			Rows($"{{\"UId\":\"{SectionUId}\",\"Name\":\"UsrCaseSection\"}}"));
		ClassicSectionSchemaResolver resolver = Create(client);

		// Act
		ClassicSectionLookup result = resolver.ResolveSectionSchemaNames("UsrCase");

		// Assert
		result.SectionSchemaNames.Should().BeEmpty(
			because: "an empty-Guid SectionSchemaUId binds no section and must not reach the name lookup");
		result.Error.Should().BeNull(because: "skipping an unbound row is not an error");
	}

	[Test]
	[Description("ResolveSectionSchemaNames reports an error instead of guessing when the entity name matches no base SysSchema row.")]
	public void ResolveSectionSchemaNames_ShouldReportError_WhenEntityNotFound() {
		// Arrange
		IApplicationClient client = ClientReturning(Rows(), Rows(), Rows());
		ClassicSectionSchemaResolver resolver = Create(client);

		// Act
		ClassicSectionLookup result = resolver.ResolveSectionSchemaNames("UsrMissing");

		// Assert
		result.Error.Should().Contain("UsrMissing",
			because: "the caller degrades to name conventions and needs to know which entity failed to resolve");
		result.SectionSchemaNames.Should().BeEmpty(because: "no entity means no section binding");
	}

	[Test]
	[Description("ResolveSectionSchemaNames reports an error instead of throwing when the DataService call fails, so the bundle can degrade to name conventions.")]
	public void ResolveSectionSchemaNames_ShouldReportError_WhenDataServiceThrows() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(default, default)
			.ReturnsForAnyArgs<string>(_ => throw new System.Net.WebException("connection refused"));
		ClassicSectionSchemaResolver resolver = Create(client);

		// Act
		ClassicSectionLookup result = resolver.ResolveSectionSchemaNames("UsrCase");

		// Assert
		result.Error.Should().Contain("connection refused",
			because: "a transport failure must be reported, never thrown into the bundle assembly");
		result.SectionSchemaNames.Should().BeEmpty(because: "a failed lookup resolves nothing");
	}

	[Test]
	[Description("ResolveSectionSchemaNames rejects a blank entity name without issuing any DataService call.")]
	public void ResolveSectionSchemaNames_ShouldReportError_WhenEntityNameIsBlank() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ClassicSectionSchemaResolver resolver = Create(client);

		// Act
		ClassicSectionLookup result = resolver.ResolveSectionSchemaNames("   ");

		// Assert
		result.Error.Should().NotBeNull(because: "there is nothing to resolve without an entity name");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("ResolveSectionSchemaNames preserves SysModule row order so the first module bound to the entity is the first candidate.")]
	public void ResolveSectionSchemaNames_ShouldPreserveModuleRowOrder_WhenSeveralSectionsAreBound() {
		// Arrange — the SysSchema name lookup deliberately returns the two rows in the opposite order.
		const string secondUId = "33333333-3333-3333-3333-333333333333";
		IApplicationClient client = ClientReturning(
			Rows($"{{\"UId\":\"{EntityUId}\",\"ExtendParent\":false}}"),
			Rows($"{{\"SectionSchemaUId\":\"{SectionUId}\"}}", $"{{\"SectionSchemaUId\":\"{secondUId}\"}}"),
			Rows($"{{\"UId\":\"{secondUId}\",\"Name\":\"UsrSecondSection\"}}",
				$"{{\"UId\":\"{SectionUId}\",\"Name\":\"UsrFirstSection\"}}"));
		ClassicSectionSchemaResolver resolver = Create(client);

		// Act
		ClassicSectionLookup result = resolver.ResolveSectionSchemaNames("UsrCase");

		// Assert
		result.SectionSchemaNames.Should().Equal(new List<string> { "UsrFirstSection", "UsrSecondSection" },
			because: "candidates follow the SysModule binding order, not the SysSchema name-lookup order");
	}
}
