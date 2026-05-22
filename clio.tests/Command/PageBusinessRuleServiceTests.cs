using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PageBusinessRuleServiceTests {
	[Test]
	[Category("Unit")]
	[Description("Orchestrates page business-rule creation through schema, attribute, element, converter, and add-on services.")]
	public void Create_Should_Create_Page_Rule_Through_Providers() {
		// Arrange
		IBusinessRulePackageResolver packageResolver = Substitute.For<IBusinessRulePackageResolver>();
		IPageBusinessRuleSchemaProvider schemaProvider = Substitute.For<IPageBusinessRuleSchemaProvider>();
		IPageBusinessRuleAttributeProvider attributeProvider = Substitute.For<IPageBusinessRuleAttributeProvider>();
		IPageBusinessRuleElementProvider elementProvider = Substitute.For<IPageBusinessRuleElementProvider>();
		IBusinessRuleAddonService addonService = Substitute.For<IBusinessRuleAddonService>();
		Guid packageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		PageBundleInfo bundle = new();
		BusinessRule rule = CreatePageRule();
		IReadOnlyList<BusinessRuleMetadataDto>? capturedMetadata = null;
		packageResolver.ResolveUId("UsrPkg").Returns(packageUId);
		schemaProvider.GetSchema("UsrPage", packageUId).Returns(new PageBusinessRuleSchemaContext(
			"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
			Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
			bundle));
		attributeProvider.GetAttributes(bundle, packageUId).Returns(new Dictionary<string, BusinessRuleAttributeDescriptor> {
			["PDS_Text"] = new("PDS_Text", "Text", null)
		});
		elementProvider.GetElementNames(bundle).Returns(new HashSet<string>(StringComparer.Ordinal) {
			"Input_One"
		});
		addonService.AppendRule(
				Arg.Any<AddonGetRequestDto>(),
				rule,
				Arg.Do<IReadOnlyList<BusinessRuleMetadataDto>>(metadata => capturedMetadata = metadata))
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		PageBusinessRuleService service = new(
			packageResolver,
			schemaProvider,
			attributeProvider,
			elementProvider,
			addonService);

		// Act
		BusinessRuleCreateResult result = service.Create(new PageBusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

		// Assert
		result.RuleName.Should().Be("BusinessRule_1234567",
			because: "the service should return the add-on service result after successful creation");
		addonService.Received(1).AppendRule(
			Arg.Is<AddonGetRequestDto>(request =>
				request.AddonName == "BusinessRule"
				&& request.TargetSchemaUId == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
				&& request.TargetParentSchemaUId == Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
				&& request.TargetPackageUId == packageUId
				&& request.TargetSchemaManagerName == "ClientUnitSchemaManager"
				&& request.UseFullHierarchy),
			rule,
			Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>());
		capturedMetadata.Should().NotBeNull(
			because: "the service should convert the validated page rule before saving the add-on");
		capturedMetadata!.Should().ContainSingle(
			because: "page business-rule creation should still emit one saved metadata rule");
		capturedMetadata[0].Cases.Single().Actions.Single().TypeName.Should().Be(BusinessRuleConstants.BusinessRuleShowElementTypeName,
			because: "page show-element actions should still be converted into the Creatio show element action type");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops before add-on mutation when page business-rule validation fails.")]
	public void Create_Should_Not_Append_Addon_When_Page_Rule_Validation_Fails() {
		// Arrange
		IBusinessRulePackageResolver packageResolver = Substitute.For<IBusinessRulePackageResolver>();
		IPageBusinessRuleSchemaProvider schemaProvider = Substitute.For<IPageBusinessRuleSchemaProvider>();
		IPageBusinessRuleAttributeProvider attributeProvider = Substitute.For<IPageBusinessRuleAttributeProvider>();
		IPageBusinessRuleElementProvider elementProvider = Substitute.For<IPageBusinessRuleElementProvider>();
		IBusinessRuleAddonService addonService = Substitute.For<IBusinessRuleAddonService>();
		Guid packageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		PageBundleInfo bundle = new();
		BusinessRule rule = CreatePageRule(actionElementName: "MissingInput");
		packageResolver.ResolveUId("UsrPkg").Returns(packageUId);
		schemaProvider.GetSchema("UsrPage", packageUId).Returns(new PageBusinessRuleSchemaContext(
			"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
			Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
			bundle));
		attributeProvider.GetAttributes(bundle, packageUId).Returns(new Dictionary<string, BusinessRuleAttributeDescriptor> {
			["PDS_Text"] = new("PDS_Text", "Text", null)
		});
		elementProvider.GetElementNames(bundle).Returns(new HashSet<string>(StringComparer.Ordinal) {
			"Input_One"
		});
		PageBusinessRuleService service = new(
			packageResolver,
			schemaProvider,
			attributeProvider,
			elementProvider,
			addonService);

		// Act
		Action act = () => service.Create(new PageBusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown page element 'MissingInput' in rule.actions[*].items. Available page elements: Input_One.",
				because: "invalid page element targets should be rejected before destructive add-on writes");
		addonService.DidNotReceiveWithAnyArgs().AppendRule(default!, default!, default!);
	}

	private static BusinessRule CreatePageRule(string actionElementName = "Input_One") =>
		new(
			"Show input",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_Text", null),
						"equal",
						new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("\"Ready\"")))
				]),
			[
				new ShowElementBusinessRuleAction([actionElementName])
			]);
}
