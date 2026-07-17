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
			CreatePageValidator(lookupReferenceValidator));

		// Act
		BusinessRuleCreateResult result = service.Create(new BusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

		// Assert
		result.RuleName.Should().Be("BusinessRule_1234567",
			because: "the service should return the add-on service result after successful creation");
		addonService.Received(1).AppendRule(
			Arg.Is<AddonGetRequestDto>(request =>
				request.AddonName == "BusinessRule"
				// The page is sent as the PARENT so the add-on resolves in the requested writable
				// package; the target is an unresolvable placeholder, not the page's committed uId.
				&& request.TargetParentSchemaUId == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
				&& request.TargetSchemaUId != Guid.Empty
				&& request.TargetSchemaUId != Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
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
			CreatePageValidator(lookupReferenceValidator));

		// Act
		Action act = () => service.Create(new BusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

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
			CreatePageValidator(lookupReferenceValidator));

		// Act
		Action act = () => service.Create(new BusinessRuleCreateRequest("UsrPkg", "UsrPage", rule));

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
		BusinessRulesBatchRequest request = new(
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
		BusinessRulesBatchRequest request = new(
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
		BusinessRulesBatchRequest request = new(
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
		BusinessRulesBatchRequest request = new(packageName, pageSchemaName, [CreatePageRule()]);

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
		BusinessRulesBatchRequest request = new("UsrPkg", "UsrPage", []);

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
		BusinessRule model = new("Page rule", new BusinessRuleConditionGroup("AND", []), []) {
			Name = "BusinessRule_pg",
			Enabled = true
		};
		addonService.ReadRules(Arg.Any<AddonGetRequestDto>()).Returns([model]);

		// Act
		IReadOnlyList<BusinessRule> models = service.Read(new BusinessRulesReadRequest("UsrPkg", "UsrPage"));

		// Assert
		models.Should().ContainSingle(because: "the add-on service returned one rule");
		models[0].Should().BeSameAs(model,
			because: "the page service passes the add-on rules through unchanged");
		addonService.Received(1).ReadRules(
			Arg.Is<AddonGetRequestDto>(request =>
				request.AddonName == "BusinessRule"
				&& request.TargetParentSchemaUId == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
				&& request.TargetSchemaUId != Guid.Empty
				&& request.TargetSchemaUId != Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
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
		Action act = () => service.Read(new BusinessRulesReadRequest(packageName, pageSchemaName));

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
		BusinessRulesBatchRequest request = new(
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
		addonService.DidNotReceiveWithAnyArgs().SaveSchema(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Replaces the matched page rule in place preserving its persisted rule uId, trims the caller-supplied name, applies the explicit enabled=false intent, and saves exactly once through the correct schema request.")]
	public void Update_Should_Replace_Page_Rule_Preserving_Identity_And_Honoring_Enabled() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		addonService.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(BuildPageAddonSchema(("BusinessRule_pg", "pg-rule")));
		AddonSchemaDto? saved = null;
		addonService
			.When(addon => addon.SaveSchema(Arg.Any<AddonSchemaDto>()))
			.Do(callInfo => saved = callInfo.Arg<AddonSchemaDto>());
		BusinessRulesBatchRequest request = new(
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
		results[0].Success.Should().BeTrue(because: "the trimmed name matches the persisted page rule");
		results[0].Name.Should().Be("BusinessRule_pg",
			because: "the caller-supplied name must be trimmed before matching and reporting");
		saved.Should().NotBeNull(because: "a matched update must persist the mutated metadata");
		using JsonDocument metaData = JsonDocument.Parse(saved!.MetaData);
		JsonElement[] rules = [.. metaData.RootElement.GetProperty("rules").EnumerateArray()];
		rules.Should().ContainSingle(because: "the matched page rule is replaced in place without adding new rules");
		rules[0].GetProperty("uId").GetString().Should().Be("pg-rule",
			because: "the persisted page rule uId must be preserved so the platform stores a short diff");
		rules[0].GetProperty("name").GetString().Should().Be("BusinessRule_pg",
			because: "the replacement keeps the persisted internal rule name");
		rules[0].GetProperty("enabled").GetBoolean().Should().BeFalse(
			because: "the caller's explicit enabled=false intent must be applied");
		rules[0].GetProperty("caption").GetString().Should().Be("Updated rule",
			because: "the replacement carries the new caption");
		addonService.Received(1).GetSchema(
			Arg.Is<AddonGetRequestDto>(addonRequest =>
				addonRequest.AddonName == "BusinessRule"
				&& addonRequest.TargetSchemaManagerName == "ClientUnitSchemaManager"
				&& addonRequest.UseFullHierarchy));
		addonService.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[Test]
	[Category("Unit")]
	[Description("Isolates a per-rule validation failure inside an update batch: only the valid rule is saved and the invalid rule keeps its own error.")]
	public void Update_Should_Isolate_Validation_Failure_When_Batch_Has_Invalid_Rule() {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		addonService.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(BuildPageAddonSchema(("BusinessRule_good", "good"), ("BusinessRule_bad", "bad")));
		BusinessRulesBatchRequest request = new(
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
		addonService.Received(1).SaveSchema(Arg.Any<AddonSchemaDto>());
	}

	[TestCase(true)]
	[TestCase(false)]
	[Category("Unit")]
	[Description("Rejects delete requests without rule names before the add-on service is invoked.")]
	public void Delete_Should_Reject_When_RuleNames_Are_Missing(bool useNullList) {
		// Arrange
		PageBusinessRuleService service = BuildBatchService(out IBusinessRuleAddonService addonService);
		BusinessRulesDeleteRequest request = new(
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
			new BusinessRulesDeleteRequest("UsrPkg", "UsrPage", ["BusinessRule_pg"]));

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
		addonService.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto { MetaData = string.Empty, Resources = [] });
		return new PageBusinessRuleService(
			packageResolver,
			schemaProvider,
			attributeProvider,
			elementProvider,
			addonService,
			CreatePageValidator(lookupReferenceValidator));
	}

	private static AddonSchemaDto BuildPageAddonSchema(params (string Name, string UId)[] rules) {
		string rulesJson = string.Join(",", rules.Select(rule => $$"""
			{
			  "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			  "uId": "{{rule.UId}}",
			  "name": "{{rule.Name}}",
			  "enabled": true,
			  "caption": "{{rule.Name}}",
			  "cases": [{ "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}", "uId": "{{rule.UId}}-case", "actions": [] }],
			  "triggers": []
			}
			"""));
		return new AddonSchemaDto {
			MetaData = $$"""{ "typeName": "{{BusinessRuleConstants.BusinessRulesMetadataTypeName}}", "rules": [{{rulesJson}}] }""",
			Resources = []
		};
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

	private static PageBusinessRuleValidator CreatePageValidator(
		IBusinessRuleLookupReferenceValidator lookupReferenceValidator) {
		// The service tests exercise the default (feature-off) page-rule behaviour with root-scope operands.
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService.IsFeatureEnabled(Arg.Any<string>()).Returns(false);
		return new PageBusinessRuleValidator(
			new BusinessRuleValidator(lookupReferenceValidator),
			featureToggleService);
	}
}
