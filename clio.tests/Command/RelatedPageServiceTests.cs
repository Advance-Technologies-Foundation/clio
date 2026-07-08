using System;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RelatedPageServiceTests {

	private IBusinessRulePackageResolver _packageResolver;
	private IEntityBusinessRuleAttributeProvider _attributeProvider;
	private IRelatedPageAddonService _addonService;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private RelatedPageService _service;

	[SetUp]
	public void SetUp() {
		_packageResolver = Substitute.For<IBusinessRulePackageResolver>();
		_attributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		_addonService = Substitute.For<IRelatedPageAddonService>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();

		_packageResolver.ResolveUId(Arg.Any<string>()).Returns(Guid.NewGuid());
		_attributeProvider.GetAttributes(Arg.Any<string>(), Arg.Any<Guid>()).Returns(
			new EntityBusinessRuleAttributeContext(
				new EntityDesignSchemaDto { UId = Guid.NewGuid(), Name = "UsrEntity" },
				new System.Collections.Generic.Dictionary<string, BusinessRuleAttributeDescriptor>()));
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"value\":[{{\"UId\":\"{Guid.NewGuid()}\"}}]}}");

		_service = new RelatedPageService(
			_packageResolver, _attributeProvider, _addonService, _applicationClient, _serviceUrlBuilder);
	}

	private static RelatedPageRegistration Request(
		RelatedPageSchemaType schemaType, string pageSchemaName = "UsrEntity_FormPage") =>
		new("UsrPackage", "UsrEntity", pageSchemaName, schemaType, IsDefault: true);

	[Test]
	[Description("A mobile registration targets the MobileRelatedPage add-on.")]
	public void Register_ShouldUseMobileAddon_WhenSchemaTypeIsMobile() {
		// Arrange
		RelatedPageRegistration request = Request(RelatedPageSchemaType.Mobile);

		// Act
		RelatedPageResult result = _service.Register(request);

		// Assert
		result.AddonName.Should().Be("MobileRelatedPage",
			because: "a mobile related page must be written to the MobileRelatedPage add-on");
		_addonService.Received(1).UpsertRelatedPage(
			Arg.Is<AddonGetRequestDto>(r => r.AddonName == "MobileRelatedPage"), Arg.Any<Guid>(), true);
	}

	[Test]
	[Description("A web registration targets the RelatedPage add-on.")]
	public void Register_ShouldUseWebAddon_WhenSchemaTypeIsWeb() {
		// Arrange
		RelatedPageRegistration request = Request(RelatedPageSchemaType.Web);

		// Act
		RelatedPageResult result = _service.Register(request);

		// Assert
		result.AddonName.Should().Be("RelatedPage",
			because: "a web related page must be written to the RelatedPage add-on");
		_addonService.Received(1).UpsertRelatedPage(
			Arg.Is<AddonGetRequestDto>(r => r.AddonName == "RelatedPage"), Arg.Any<Guid>(), true);
	}

	[Test]
	[Description("A page name containing a single quote is escaped (quote-doubled) in the OData $filter, preventing filter injection and resolving names with apostrophes.")]
	public void Register_ShouldEscapeSingleQuotesInODataFilter_WhenPageNameContainsQuote() {
		// Arrange
		string capturedEndpoint = null;
		_serviceUrlBuilder.Build(Arg.Do<string>(endpoint => capturedEndpoint = endpoint))
			.Returns(callInfo => callInfo.Arg<string>());
		RelatedPageRegistration request = Request(RelatedPageSchemaType.Mobile, pageSchemaName: "Usr'X");

		// Act
		_service.Register(request);

		// Assert
		capturedEndpoint.Should().NotBeNull(
			because: "the schema UId is resolved through a built OData endpoint");
		Uri.UnescapeDataString(capturedEndpoint).Should().Contain("Name eq 'Usr''X'",
			because: "the single quote must be doubled so it is treated as data, not as OData filter syntax");
	}

	[Test]
	[Description("An unresolved page schema surfaces a clear error rather than binding a wrong or empty UId.")]
	public void Register_ShouldThrow_WhenPageSchemaCannotBeResolved() {
		// Arrange
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		RelatedPageRegistration request = Request(RelatedPageSchemaType.Mobile, pageSchemaName: "UsrMissing_FormPage");

		// Act
		Action act = () => _service.Register(request);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an unresolved page schema must fail loudly instead of registering an empty UId")
			.WithMessage("*UsrMissing_FormPage*");
		_addonService.DidNotReceive().UpsertRelatedPage(Arg.Any<AddonGetRequestDto>(), Arg.Any<Guid>(), Arg.Any<bool>());
	}
}
