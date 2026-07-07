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

	[Test]
	[Category("Unit")]
	[Description("Reads the persisted page rules through the correct add-on schema request (BusinessRule addon, ClientUnitSchemaManager, full hierarchy).")]
	public void Read_Should_Return_Models_Through_Correct_Addon_Request() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		BusinessRuleReadModel model = new() { Name = "BusinessRule_pg", Enabled = true, Convertible = true };
		addonService.ReadRules(Arg.Any<AddonGetRequestDto>()).Returns([model]);

		// Act
		IReadOnlyList<BusinessRuleReadModel> models = service.Read(new PageBusinessRulesReadRequest("UsrPkg", "UsrPage"));

		// Assert
		models.Should().ContainSingle(because: "the add-on service returned one read model");
		models[0].Should().BeSameAs(model,
			because: "the page service passes the add-on read models through unchanged");
		addonService.Received(1).ReadRules(
			Arg.Is<AddonGetRequestDto>(request =>
				request.AddonName == "BusinessRule"
				&& request.TargetSchemaUId == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
				&& request.TargetParentSchemaUId == Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
				&& request.TargetPackageUId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
				&& request.TargetSchemaManagerName == "ClientUnitSchemaManager"
				&& request.UseFullHierarchy));
	}

	[TestCase("", "UsrPage", "package-name is required.")]
	[TestCase("UsrPkg", "", "page-schema-name is required.")]
	[Category("Unit")]
	[Description("Rejects missing read request-level fields before remote dependencies are invoked.")]
	public void Read_Should_Reject_Request_Level_Guards(
		string packageName,
		string pageSchemaName,
		string expectedMessage) {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);

		// Act
		Action act = () => service.Read(new PageBusinessRulesReadRequest(packageName, pageSchemaName));

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage(expectedMessage,
				because: "read request guards must run before any remote call");
		addonService.DidNotReceiveWithAnyArgs().ReadRules(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Fails a rule without a name as an isolated per-rule failure result instead of throwing, and skips the add-on update when it is the only rule.")]
	public void Update_Should_Fail_Per_Rule_When_Name_Is_Missing() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		PageBusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrPage",
			[CreatePageRule(caption: "No name")]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = service.Update(request);

		// Assert
		results.Should().ContainSingle(because: "one input rule yields one outcome");
		results[0].Success.Should().BeFalse(because: "update requires the rule name as the match key");
		results[0].Name.Should().Be("No name",
			because: "a rule without a name is identified by its caption in the outcome");
		results[0].Error.Should().Be("name is required to update a business rule.",
			because: "the failure must tell the caller the match key is missing");
		addonService.DidNotReceiveWithAnyArgs().UpdateRules(default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Converts each update rule and passes a BusinessRuleUpdateItem carrying the trimmed name, the caller's enabled intent, and the generated metadata to the add-on service through the correct schema request.")]
	public void Update_Should_Pass_Trimmed_Name_And_Enabled_To_Addon_Service() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		IReadOnlyList<BusinessRuleUpdateItem>? capturedItems = null;
		addonService.UpdateRules(
				Arg.Any<AddonGetRequestDto>(),
				Arg.Do<IReadOnlyList<BusinessRuleUpdateItem>>(items => capturedItems = items))
			.Returns(callInfo => callInfo.Arg<IReadOnlyList<BusinessRuleUpdateItem>>()
				.Select(item => new BusinessRuleBatchItemResult(item.Name, true, item.Name, null))
				.ToList());
		PageBusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrPage",
			[
				CreatePageRule(caption: "Updated rule") with {
					Name = " BusinessRule_pg ",
					Enabled = false
				}
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = service.Update(request);

		// Assert
		results.Should().ContainSingle(because: "one input rule yields one outcome");
		results[0].Success.Should().BeTrue(because: "the add-on service reported a successful update");
		capturedItems.Should().NotBeNull(because: "the service must delegate the converted batch to the add-on service");
		capturedItems!.Should().ContainSingle(because: "one valid rule produces one update item");
		capturedItems[0].Name.Should().Be("BusinessRule_pg",
			because: "the caller-supplied name must be trimmed before it is used as the match key");
		capturedItems[0].Enabled.Should().BeFalse(
			because: "the caller's explicit enabled intent must travel with the update item");
		capturedItems[0].GeneratedRules.Should().ContainSingle(
			because: "a page rule converts to exactly one metadata rule with no children");
		capturedItems[0].GeneratedRules[0].Name.Should().Be("BusinessRule_pg",
			because: "the converted metadata carries the trimmed rule name");
		addonService.Received(1).UpdateRules(
			Arg.Is<AddonGetRequestDto>(addonRequest =>
				addonRequest.AddonName == "BusinessRule"
				&& addonRequest.TargetSchemaManagerName == "ClientUnitSchemaManager"
				&& addonRequest.UseFullHierarchy),
			Arg.Any<IReadOnlyList<BusinessRuleUpdateItem>>());
	}

	[Test]
	[Category("Unit")]
	[Description("Isolates a per-rule validation failure inside an update batch: only the valid rule reaches the add-on service and the invalid rule keeps its own error.")]
	public void Update_Should_Isolate_Validation_Failure_When_Batch_Has_Invalid_Rule() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		addonService.UpdateRules(Arg.Any<AddonGetRequestDto>(), Arg.Any<IReadOnlyList<BusinessRuleUpdateItem>>())
			.Returns(callInfo => callInfo.Arg<IReadOnlyList<BusinessRuleUpdateItem>>()
				.Select(item => new BusinessRuleBatchItemResult(item.Name, true, item.Name, null))
				.ToList());
		PageBusinessRulesBatchRequest request = new(
			"UsrPkg",
			"UsrPage",
			[
				CreatePageRule(caption: "Good rule") with { Name = "BusinessRule_good" },
				CreatePageRule(caption: "Bad rule", actionElementName: "MissingInput") with { Name = "BusinessRule_bad" }
			]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = service.Update(request);

		// Assert
		results.Should().HaveCount(2, because: "every input rule gets an outcome in input order");
		results[0].Success.Should().BeTrue(because: "the valid rule must be updated despite the failing sibling");
		results[1].Success.Should().BeFalse(because: "the second rule targets an unknown page element");
		results[1].Error.Should().Contain("MissingInput",
			because: "the validation failure keeps its own error message");
		addonService.Received(1).UpdateRules(
			Arg.Any<AddonGetRequestDto>(),
			Arg.Is<IReadOnlyList<BusinessRuleUpdateItem>>(items =>
				items.Count == 1 && items[0].Name == "BusinessRule_good"));
	}

	[TestCase(true)]
	[TestCase(false)]
	[Category("Unit")]
	[Description("Rejects delete requests without rule names before the add-on service is invoked.")]
	public void Delete_Should_Reject_When_RuleNames_Are_Missing(bool useNullList) {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		PageBusinessRulesDeleteRequest request = new(
			"UsrPkg",
			"UsrPage",
			useNullList ? null! : []);

		// Act
		Action act = () => service.Delete(request);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule-names is required and must contain at least one rule name.",
				because: "delete without rule names is a request-level error");
		addonService.DidNotReceiveWithAnyArgs().DeleteRules(default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Delegates delete to the add-on service with the requested names and the correct page add-on schema request.")]
	public void Delete_Should_Pass_Names_Through_Correct_Addon_Request() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		addonService.DeleteRules(Arg.Any<AddonGetRequestDto>(), Arg.Any<IReadOnlyList<string>>())
			.Returns([new BusinessRuleBatchItemResult("BusinessRule_pg", true, "BusinessRule_pg", null)]);

		// Act
		IReadOnlyList<BusinessRuleBatchItemResult> results = service.Delete(
			new PageBusinessRulesDeleteRequest("UsrPkg", "UsrPage", ["BusinessRule_pg"]));

		// Assert
		results.Should().ContainSingle(because: "the add-on service reported one outcome");
		results[0].Success.Should().BeTrue(because: "the add-on service reported a successful delete");
		addonService.Received(1).DeleteRules(
			Arg.Is<AddonGetRequestDto>(request =>
				request.AddonName == "BusinessRule"
				&& request.TargetSchemaManagerName == "ClientUnitSchemaManager"
				&& request.UseFullHierarchy),
			Arg.Is<IReadOnlyList<string>>(names => names.Count == 1 && names[0] == "BusinessRule_pg"));
	}

	[Test]
	[Category("Unit")]
	[Description("Strips caller-supplied block uIds on create so fresh block identities are minted, converting and appending a stripped copy instead of the caller's instance.")]
	public void Create_Should_Strip_Caller_Block_UIds_When_Rule_Carries_Them() {
		// Arrange
		const string actionUId = "99999999-9999-9999-9999-999999999992";
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		IReadOnlyList<BusinessRuleMetadataDto>? capturedMetadata = null;
		addonService.AppendRule(
				Arg.Any<AddonGetRequestDto>(),
				Arg.Any<BusinessRule>(),
				Arg.Do<IReadOnlyList<BusinessRuleMetadataDto>>(metadata => capturedMetadata = metadata))
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		BusinessRule rule = CreatePageRule() with {
			Actions = [new ShowElementBusinessRuleAction(["Input_One"]) { UId = actionUId }]
		};

		// Act
		service.Create(new PageBusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

		// Assert
		addonService.Received(1).AppendRule(
			Arg.Any<AddonGetRequestDto>(),
			Arg.Is<BusinessRule>(appended => !ReferenceEquals(appended, rule)),
			Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>());
		capturedMetadata.Should().NotBeNull(because: "the stripped rule must still be converted and appended");
		capturedMetadata![0].Cases[0].Actions[0].UId.Should().NotBe(actionUId,
			because: "create must mint a fresh action uId instead of honoring the caller-supplied one");
	}

	[Test]
	[Category("Unit")]
	[Description("Passes the caller's rule instance through unchanged on create when it carries no block uIds, avoiding a needless copy.")]
	public void Create_Should_Keep_Same_Rule_Instance_When_No_Block_UIds_Present() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		addonService.AppendRule(
				Arg.Any<AddonGetRequestDto>(),
				Arg.Any<BusinessRule>(),
				Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		BusinessRule rule = CreatePageRule();

		// Act
		service.Create(new PageBusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

		// Assert
		addonService.Received(1).AppendRule(
			Arg.Any<AddonGetRequestDto>(),
			Arg.Is<BusinessRule>(appended => ReferenceEquals(appended, rule)),
			Arg.Any<IReadOnlyList<BusinessRuleMetadataDto>>());
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
