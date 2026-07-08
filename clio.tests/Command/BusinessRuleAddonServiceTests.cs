namespace Clio.Tests.Command;

using System;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Regression coverage for the SHARED <see cref="AddonSchemaDesignerClient.BuildConfiguration"/> behavior as seen
/// through the pre-existing business-rule batch path (<see cref="BusinessRuleAddonService.AppendRules"/>). The
/// related-page-addon PR changed <c>BuildConfiguration</c> from fire-and-forget to throw on an EXPLICIT
/// <c>success:false</c>; because that client is shared, these tests pin what the business-rule consumer sees on a
/// rebuild failure (the schema is already saved when the throw happens) and confirm the happy path is unaffected.
/// A real <see cref="AddonSchemaDesignerClient"/> is used (not a mock) so the actual response parsing runs.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class BusinessRuleAddonServiceTests {
	private const string DesignerBase = "http://local/ServiceModel/AddonSchemaDesignerService.svc";
	private const string GetSchemaUrl = DesignerBase + "/GetSchema";
	private const string SaveSchemaUrl = DesignerBase + "/SaveSchema";
	private const string ResetCacheUrl = "http://local/rest/WorkplaceService/ResetScriptCache";
	private const string BuildConfigUrl = "http://local/ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration";

	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private BusinessRuleAddonService _service = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("ServiceModel/AddonSchemaDesignerService.svc").Returns(DesignerBase);
		_serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache").Returns(ResetCacheUrl);
		_serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration").Returns(BuildConfigUrl);
		// A REAL AddonSchemaDesignerClient so the actual BuildConfiguration success:false -> throw parsing runs,
		// exercised through the business-rule batch path (AppendRules), not a mock that no-ops the rebuild.
		AddonSchemaDesignerClient addonClient = new(_applicationClient, new JsonConverter(), _serviceUrlBuilder);
		_service = new BusinessRuleAddonService(addonClient);
	}

	private void Stub(string url, string response) =>
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);

	private static AddonGetRequestDto Request() =>
		new() {
			AddonName = "BusinessRule",
			TargetSchemaUId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			TargetParentSchemaUId = Guid.Empty,
			TargetPackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			TargetSchemaManagerName = "EntitySchemaManager",
			UseFullHierarchy = true
		};

	private static BusinessRuleMetadataDto Rule() =>
		new() { Name = "UsrRule1", UId = "33333333-3333-3333-3333-333333333333", Caption = "Rule 1" };

	[Test]
	[Description("Regression (shared BuildConfiguration path): an explicit success:false rebuild surfaces as a failure through AppendRules EVEN THOUGH SaveSchema already persisted the rule — the 'schema saved, rebuild failed' surface the business-rule batch (StampOutcome) reports to its caller, introduced by this PR's BuildConfiguration strictness change.")]
	public void AppendRules_ShouldThrowAfterSaving_WhenRebuildReportsExplicitFailure() {
		// Arrange — GetSchema / SaveSchema / ResetScriptCache succeed; only the SHARED BuildConfiguration fails.
		Stub(GetSchemaUrl, """{"success":true,"schema":{"metaData":"{}","resources":[]}}""");
		Stub(SaveSchemaUrl, """{"success":true,"value":true}""");
		Stub(ResetCacheUrl, """{"success":true}""");
		Stub(BuildConfigUrl, """{"success":false,"errorInfo":{"message":"static build failed"}}""");

		// Act
		Action act = () => _service.AppendRules(Request(), new[] { Rule() });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*static build failed*",
			because: "an explicit success:false rebuild must surface as a failure through the shared AppendRules path");
		// The throw happens AFTER the rule was persisted: SaveSchema ran before BuildConfiguration failed, so the
		// batch reports a failure for an already-saved rule (rebuild-only failure, not 'nothing saved').
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.Received(1).ExecutePostRequest(BuildConfigUrl, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The business-rule batch path completes normally when the shared BuildConfiguration returns success:true — the throw is scoped to an explicit failure and does not regress the happy path.")]
	public void AppendRules_ShouldComplete_WhenRebuildSucceeds() {
		// Arrange
		Stub(GetSchemaUrl, """{"success":true,"schema":{"metaData":"{}","resources":[]}}""");
		Stub(SaveSchemaUrl, """{"success":true,"value":true}""");
		Stub(ResetCacheUrl, """{"success":true}""");
		Stub(BuildConfigUrl, """{"success":true,"errorInfo":null}""");

		// Act
		BusinessRuleCreateResult result = _service.AppendRules(Request(), new[] { Rule() });

		// Assert
		result.RuleName.Should().Be("UsrRule1",
			because: "a successful batch save returns the first appended rule's generated name");
	}
}
