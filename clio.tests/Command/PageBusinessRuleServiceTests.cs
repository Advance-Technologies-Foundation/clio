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
		IBusinessRuleLookupReferenceValidator lookupReferenceValidator = Substitute.For<IBusinessRuleLookupReferenceValidator>();
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
			addonService,
			new PageBusinessRuleValidator(new BusinessRuleValidator(lookupReferenceValidator)));

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
		lookupReferenceValidator.Received(1).Validate(
			rule,
			Arg.Any<IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>>());
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
		IBusinessRuleLookupReferenceValidator lookupReferenceValidator = Substitute.For<IBusinessRuleLookupReferenceValidator>();
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
			addonService,
			new PageBusinessRuleValidator(new BusinessRuleValidator(lookupReferenceValidator)));

		// Act
		Action act = () => service.Create(new PageBusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown page element 'MissingInput' in rule.actions[*].items. Available page elements: Input_One.",
				because: "invalid page element targets should be rejected before destructive add-on writes");
		addonService.DidNotReceiveWithAnyArgs().AppendRule(default!, default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Stops before add-on mutation when a page lookup condition references a missing lookup record.")]
	public void Create_Should_Not_Append_Addon_When_Page_Lookup_Validation_Fails() {
		// Arrange
		IBusinessRulePackageResolver packageResolver = Substitute.For<IBusinessRulePackageResolver>();
		IPageBusinessRuleSchemaProvider schemaProvider = Substitute.For<IPageBusinessRuleSchemaProvider>();
		IPageBusinessRuleAttributeProvider attributeProvider = Substitute.For<IPageBusinessRuleAttributeProvider>();
		IPageBusinessRuleElementProvider elementProvider = Substitute.For<IPageBusinessRuleElementProvider>();
		IBusinessRuleAddonService addonService = Substitute.For<IBusinessRuleAddonService>();
		IBusinessRuleLookupReferenceValidator lookupReferenceValidator = Substitute.For<IBusinessRuleLookupReferenceValidator>();
		Guid packageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		PageBundleInfo bundle = new();
		BusinessRule rule = CreatePageRule();
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
		lookupReferenceValidator
			.When(validator => validator.Validate(
				Arg.Any<BusinessRule>(),
				Arg.Any<IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>>()))
			.Do(_ => throw new ArgumentException("Lookup record was not found."));
		PageBusinessRuleService service = new(
			packageResolver,
			schemaProvider,
			attributeProvider,
			elementProvider,
			addonService,
			new PageBusinessRuleValidator(new BusinessRuleValidator(lookupReferenceValidator)));

		// Act
		Action act = () => service.Create(new PageBusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Lookup record was not found.",
				because: "page lookup references should be validated before destructive add-on writes");
		addonService.DidNotReceiveWithAnyArgs().AppendRule(default!, default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Saves an entire batch of page rules with a single add-on round-trip and reports each created rule.")]
	public void Create_Batch_Should_Save_All_Rules_With_A_Single_Addon_RoundTrip() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		PageBusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrPage",
			[CreatePageRule(caption: "Rule A"), CreatePageRule(caption: "Rule B")]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = service.Create(request);

		// Assert
		results.Should().OnlyContain(result => result.Success, because: "both page rules are valid");
		results.Select(result => result.Name).Should().Equal(["Rule A", "Rule B"],
			because: "per-rule outcomes are returned in input order keyed by caption");
		addonService.Received(1).AppendRules(Arg.Any<AddonGetRequestDto>(), Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>());
		addonService.DidNotReceiveWithAnyArgs().AppendRule(default!, default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Isolates a per-rule validation failure: the bad page rule is excluded and reported while the remaining rules are still saved in the single batch write.")]
	public void Create_Batch_Should_Isolate_Per_Rule_Validation_Failure() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		PageBusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrPage",
			[
				CreatePageRule(caption: "Good 1"),
				CreatePageRule(caption: "Bad", actionElementName: "MissingInput"),
				CreatePageRule(caption: "Good 2")
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = service.Create(request);

		// Assert
		results.Should().HaveCount(3, because: "every input rule gets an outcome entry");
		results[0].Success.Should().BeTrue(because: "the first rule is valid");
		results[1].Success.Should().BeFalse(because: "the rule targets an unknown page element");
		results[1].Error.Should().Contain("MissingInput", because: "the failure reports the unknown element");
		results[2].Success.Should().BeTrue(because: "a failed rule must not abort the remaining rules");
		addonService.Received(1).AppendRules(Arg.Any<AddonGetRequestDto>(), Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>());
	}

	[Test]
	[Category("Unit")]
	[Description("Marks every converted page rule as failed with the save error when the single add-on save throws.")]
	public void Create_Batch_Should_Mark_All_Pending_Failed_When_Save_Throws() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		addonService
			.When(addon => addon.AppendRules(Arg.Any<AddonGetRequestDto>(), Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>()))
			.Do(_ => throw new InvalidOperationException("Add-on save failed."));
		PageBusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrPage",
			[CreatePageRule(caption: "Rule A"), CreatePageRule(caption: "Rule B")]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = service.Create(request);

		// Assert
		results.Should().HaveCount(2, because: "every input rule still gets an outcome");
		results.Should().OnlyContain(result => !result.Success,
			because: "a failed single add-on save fails the whole converted batch");
		results.Should().OnlyContain(result => result.Error!.Contains("Add-on save failed."),
			because: "all converted rules share the same save error");
	}

	[TestCase("", "UsrPage", "package-name is required.")]
	[TestCase("UsrPkg", "", "page-schema-name is required.")]
	[Category("Unit")]
	[Description("Rejects missing batch request-level fields before remote dependencies are invoked.")]
	public void Create_Batch_Should_Reject_Request_Level_Guards(
		string packageName,
		string pageSchemaName,
		string expectedMessage) {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out _);
		PageBusinessRulesBatchRequest request = new(packageName, pageSchemaName, [CreatePageRule()]);

		// Act
		Action act = () => service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>().WithMessage(expectedMessage,
			because: "request-level guards run before any remote call");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a batch with no rules before remote dependencies are invoked.")]
	public void Create_Batch_Should_Reject_When_Rules_Empty() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out _);
		PageBusinessRulesBatchRequest request = new("UsrPkg", "UsrPage", []);

		// Act
		Action act = () => service.Create(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rules is required and must contain at least one rule.",
				because: "an empty batch is a request-level error");
	}

	private static PageBusinessRuleService BuildBatchService(out IBusinessRuleAddonService addonService) {
		IBusinessRulePackageResolver packageResolver = Substitute.For<IBusinessRulePackageResolver>();
		IPageBusinessRuleSchemaProvider schemaProvider = Substitute.For<IPageBusinessRuleSchemaProvider>();
		IPageBusinessRuleAttributeProvider attributeProvider = Substitute.For<IPageBusinessRuleAttributeProvider>();
		IPageBusinessRuleElementProvider elementProvider = Substitute.For<IPageBusinessRuleElementProvider>();
		IBusinessRuleLookupReferenceValidator lookupReferenceValidator = Substitute.For<IBusinessRuleLookupReferenceValidator>();
		Guid packageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		PageBundleInfo bundle = new();
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
		addonService = Substitute.For<IBusinessRuleAddonService>();
		addonService.AppendRules(Arg.Any<AddonGetRequestDto>(), Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		return new PageBusinessRuleService(
			packageResolver,
			schemaProvider,
			attributeProvider,
			elementProvider,
			addonService,
			new PageBusinessRuleValidator(new BusinessRuleValidator(lookupReferenceValidator)));
	}

	private static BusinessRule CreatePageRule(string actionElementName = "Input_One", string caption = "Show input") =>
		new(
			caption,
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
