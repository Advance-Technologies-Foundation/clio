using System;
using Clio.Command;
using Clio.Command.Filters;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

// ENG-95270: widget-filter check on the update-page/sync-pages save path.
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandWidgetFilterTests {

	private const string Uid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string PackageUId = "11111111-2222-3333-4444-555555555555";
	private const string FilterError = "Chart.values.config.data.providing.filters.filter.items.a.leftExpression.columnPath: 'Nmae' does not resolve";
	private const string UnchangedWarning = "Already on the page, not changed by this save: " + FilterError;

	private ServiceProvider _provider;
	private IApplicationClient _client;
	private IWidgetFilterValidator _filterValidator;
	private PageUpdateCommand _command;

	private const string Body =
		"define('UsrPage',/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {return {"
		+ "viewConfigDiff:/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,"
		+ "viewModelConfigDiff:/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,"
		+ "modelConfigDiff:/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,"
		+ "handlers:/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,converters:/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,"
		+ "validators:/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/};});";

	private static readonly string AppendFragment = Body.Replace(
		"handlers:/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
		"handlers:/**SCHEMA_HANDLERS*/[{request:'usr.Ping',handler:async (request, next) => next?.handle(request)}]/**SCHEMA_HANDLERS*/");

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		IPageDesignerHierarchyClient hierarchy = Substitute.For<IPageDesignerHierarchyClient>();
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<string>()).Returns(x => x.Arg<string>());
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns(ci => urls.Build(ServiceUrlBuilder.KnownRoutes[ci.Arg<ServiceUrlBuilder.KnownRoute>()]));
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SelectQuery")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[{\"UId\":\"" + Uid + "\"}]}");
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("GetSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonConvert.SerializeObject(new { success = true, schema = new { name = "UsrPage", body = Body } }));
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
		hierarchy.GetDesignPackageUId(Arg.Any<string>()).Returns(PackageUId);
		hierarchy.GetParentSchemas(Arg.Any<string>(), PackageUId).Returns([
			new PageDesignerHierarchySchema { UId = Uid, Name = "UsrPage", PackageUId = PackageUId, SchemaType = 9 }
		]);
		_filterValidator = Substitute.For<IWidgetFilterValidator>();
		_filterValidator.Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>()).Returns(PageWidgetFilterReport.Empty);
		ServiceCollection services = new();
		services.AddSingleton(_client).AddSingleton(urls).AddSingleton(hierarchy)
			.AddSingleton(Substitute.For<ILogger>()).AddSingleton(Substitute.For<IPageBaselineGuard>())
			.AddSingleton(Substitute.For<IPersistedResourceKeyReader>())
			.AddSingleton(_filterValidator)
			.AddTransient<Func<IJsonDiffApplier>>(_ => () => new JsonDiffApplier())
			.AddTransient<PageUpdateCommand>();
		_provider = services.BuildServiceProvider();
		_command = _provider.GetRequiredService<PageUpdateCommand>();
	}

	[TearDown]
	public void TearDown() => _provider.Dispose();

	[Test]
	[Description("A widget filter error blocks the save, names the filter location, and nothing is written.")]
	public void TryUpdatePage_ShouldRejectSave_WhenWidgetFilterDoesNotFitTheSchema() {
		// Arrange
		_filterValidator.Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>())
			.Returns(new PageWidgetFilterReport([FilterError], ["another leaf could not be verified"]));
		PageUpdateOptions options = new() { SchemaName = "UsrPage", Mode = "replace", Body = Body };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a filter on a column that does not exist makes the widget fail at runtime");
		response.Error.Should().Contain(FilterError, because: "the caller must learn which filter leaf is wrong");
		response.Warnings.Should().Contain("another leaf could not be verified",
			because: "the advisory findings for the same body are kept beside the error");
		_filterValidator.Received(1).Validate(Arg.Any<string>(), Guid.Parse(PackageUId), Arg.Any<string?>());
		_client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("On a dry run the same filter error is a warning: a dry run says what would happen instead of refusing.")]
	public void TryUpdatePage_ShouldWarnOnDryRun_WhenWidgetFilterDoesNotFitTheSchema() {
		// Arrange
		_filterValidator.Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>()).Returns(new PageWidgetFilterReport([FilterError], []));
		PageUpdateOptions options = new() { SchemaName = "UsrPage", Mode = "replace", DryRun = true, Body = Body };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a dry run reports instead of refusing: " + response.Error);
		response.Warnings.Should().Contain("Widget filter would block the save: " + FilterError,
			because: "the dry run must say that the real save would be rejected and why");
	}

	[TestCase("replace")]
	[TestCase("append")]
	[Description("A broken filter the page already stores warns on both the dry run and the save, in replace and append mode alike: the dry run gives the save's verdict.")]
	public void TryUpdatePage_DryRunShouldMatchSave_WhenBrokenFilterIsAlreadyOnThePage(string mode) {
		// Arrange
		StubValidatorSeeingStoredFilter();
		// Append refuses a fragment that adds nothing, so it brings one handler.
		string body = mode == "append" ? AppendFragment : Body;
		PageUpdateOptions dryRun = new() { SchemaName = "UsrPage", Mode = mode, DryRun = true, Body = body };
		PageUpdateOptions save = new() { SchemaName = "UsrPage", Mode = mode, Body = body };

		// Act
		bool dryRunResult = _command.TryUpdatePage(dryRun, out PageUpdateResponse dryRunResponse);
		bool saveResult = _command.TryUpdatePage(save, out PageUpdateResponse saveResponse);

		// Assert
		saveResult.Should().BeTrue(because: "a stale filter left on the page must not block an unrelated save: " + saveResponse.Error);
		saveResponse.Warnings.Should().Contain(UnchangedWarning, because: "the save reports the stale filter as a warning");
		dryRunResult.Should().BeTrue(because: "a dry run reports instead of refusing: " + dryRunResponse.Error);
		dryRunResponse.Warnings.Should().Contain(UnchangedWarning,
			because: "the dry run must report exactly what the save reports");
		dryRunResponse.Warnings.Should().NotContain(w => w.StartsWith("Widget filter would block the save"),
			because: "the save does not block on a filter the page already holds, so the dry run must not say it would");
	}

	[Test]
	[Description("A broken filter the stored page does not hold still blocks: the dry run says so and the save is refused.")]
	public void TryUpdatePage_DryRunShouldMatchSave_WhenBrokenFilterIsNew() {
		// Arrange
		_filterValidator.Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>())
			.Returns(new PageWidgetFilterReport([FilterError], []));
		PageUpdateOptions dryRun = new() { SchemaName = "UsrPage", Mode = "replace", DryRun = true, Body = Body };
		PageUpdateOptions save = new() { SchemaName = "UsrPage", Mode = "replace", Body = Body };

		// Act
		bool dryRunResult = _command.TryUpdatePage(dryRun, out PageUpdateResponse dryRunResponse);
		bool saveResult = _command.TryUpdatePage(save, out PageUpdateResponse saveResponse);

		// Assert
		saveResult.Should().BeFalse(because: "a broken filter the save introduces is refused");
		dryRunResult.Should().BeTrue(because: "a dry run reports instead of refusing: " + dryRunResponse.Error);
		dryRunResponse.Warnings.Should().Contain("Widget filter would block the save: " + FilterError,
			because: "the dry run must announce the refusal the save makes");
	}

	[Test]
	[Description("A replace dry run with clean filters does not read the stored page for the filter check.")]
	public void TryUpdatePage_ShouldNotReadStoredBodyOnReplaceDryRun_WhenFiltersAreClean() {
		// Arrange
		PageUpdateOptions options = new() { SchemaName = "UsrPage", Mode = "replace", DryRun = true, Body = Body };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "nothing is wrong with the page: " + response.Error);
		_filterValidator.Received(1).Validate(Body, Guid.Parse(PackageUId), null);
		_client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("GetSchema")), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	// Mirrors WidgetFilterValidator: a filter that is byte-for-byte in the stored body is reported as a warning.
	private void StubValidatorSeeingStoredFilter() {
		_filterValidator.Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Is<string?>(stored => stored == null))
			.Returns(new PageWidgetFilterReport([FilterError], []));
		_filterValidator.Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Is<string?>(stored => stored != null))
			.Returns(new PageWidgetFilterReport([], [UnchangedWarning]));
	}

	[Test]
	[Description("A validator that throws (environment unavailable) only adds a warning; the save goes through.")]
	public void TryUpdatePage_ShouldSave_WhenWidgetFilterCheckCannotRun() {
		// Arrange
		_filterValidator.Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>()).Returns(_ => throw new InvalidOperationException("timeout"));
		PageUpdateOptions options = new() { SchemaName = "UsrPage", Mode = "replace", Body = Body };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "an unavailable check must never be the reason a save is refused: " + response.Error);
		response.Warnings.Should().Contain(w => w.Contains("Widget filters were not checked") && w.Contains("timeout"),
			because: "the caller is told the filters went unverified");
	}

	[Test]
	[Description("With validation turned off the filter check does not run at all.")]
	public void TryUpdatePage_ShouldSkipWidgetFilterCheck_WhenValidationIsOff() {
		// Arrange
		PageUpdateOptions options = new() { SchemaName = "UsrPage", Mode = "replace", Body = Body, Validate = false };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the save itself is fine: " + response.Error);
		_filterValidator.DidNotReceive().Validate(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>());
	}
}
