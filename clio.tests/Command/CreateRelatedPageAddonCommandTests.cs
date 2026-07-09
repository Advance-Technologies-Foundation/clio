namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreateRelatedPageAddonCommandTests {
	private IRelatedPageAddonService _service = null!;
	private ILogger _logger = null!;
	private RelatedPageAddonRequest _captured;
	private CreateRelatedPageAddonCommand _command = null!;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<IRelatedPageAddonService>();
		_service.Create(Arg.Any<RelatedPageAddonRequest>()).Returns(callInfo => {
			_captured = callInfo.Arg<RelatedPageAddonRequest>();
			return new RelatedPageAddonResult("entity-uid", "package-uid", _captured.Pages.Count, "RelatedPage");
		});
		_logger = Substitute.For<ILogger>();
		_command = new CreateRelatedPageAddonCommand(_service, _logger);
	}

	[Test]
	[Description("Building specs from CLI scalar options with a portal page scopes the base set to 'All employees' and layers an 'All external users' portal set on top; with no add pages, each audience is a single default entry (no auto-add).")]
	public void TryCreate_ShouldScopeBaseToAllEmployeesAndPortalToExternalUsers_WhenPortalPageProvided() {
		// Arrange
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage",
			PortalDefaultPage = "CasePortalFormPage"
		};

		// Act
		bool ok = _command.TryCreate(options, out CreateRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeTrue(response.Error);
		response.Success.Should().BeTrue(
			because: "the request resolves successfully when both audiences are supplied");
		_captured.Should().NotBeNull(
			because: "the command must forward a built request to the related-page service");
		_captured.Pages.Should().HaveCount(2,
			because: "no add pages are given, so each audience is a single default entry (the default also serves adding)");
		_captured.Pages.Should().Contain(page => page.RoleName == "All employees" && page.IsDefault && page.PageSchemaName == "CaseFormPage",
			because: "the base default is scoped to All employees, matching the designer");
		_captured.Pages.Should().Contain(page => page.RoleName == "All external users" && page.IsDefault && page.PageSchemaName == "CasePortalFormPage",
			because: "portal-default-page is layered on top as an All external users override");
		_captured.Pages.Should().NotContain(page => page.IsAdd,
			because: "with no explicit add page, the default page serves adding — no separate add entry is written");
	}

	[Test]
	[Description("Building specs from CLI scalar options with only a default page emits a single 'All employees' default entry (no auto-add fallback; the default serves adding too).")]
	public void TryCreate_ShouldEmitSingleAllEmployeesDefault_WhenOnlyDefaultPageProvided() {
		// Arrange
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage"
		};

		// Act
		_command.TryCreate(options, out _);

		// Assert
		_captured.Should().NotBeNull(
			because: "the command must forward a built request to the related-page service");
		_captured.Pages.Should().HaveCount(1,
			because: "only a default page is given, so a single entry is built (no auto-add fallback)");
		RelatedPageSpec spec = _captured.Pages[0];
		spec.IsDefault.Should().BeTrue(
			because: "a default-only build yields a single is-default entry");
		spec.IsAdd.Should().BeFalse(
			because: "the default page serves adding at runtime; no separate add entry is written");
		spec.RoleName.Should().Be("All employees",
			because: "the base set is scoped to All employees, matching the designer (not role-less)");
		spec.PageSchemaName.Should().Be("CaseFormPage",
			because: "the built entry carries the default page name from --default-page");
	}

	[Test]
	[Description("A CLI invocation with no page options is rejected rather than silently wiping the configuration; clearing (reset to inline) is an explicit MCP-only operation via an empty pages list.")]
	public void TryCreate_ShouldFail_WhenNoPageOptionsProvided() {
		// Arrange — no scalar page options and no explicit Pages, so the CLI scalar path yields an empty set.
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom"
		};

		// Act
		bool ok = _command.TryCreate(options, out CreateRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeFalse(
			because: "a no-option CLI call must not silently clear the configuration");
		response.Success.Should().BeFalse(
			because: "the no-option CLI call is rejected, not silently cleared");
		response.Error.Should().Contain("No pages specified",
			because: "the CLI rejects an empty scalar build; clearing is an explicit MCP-only operation");
		_service.DidNotReceiveWithAnyArgs().Create(default!);
	}

	[Test]
	[Description("Maps a distinct --add-page to its own is-add entry instead of falling back to the default page.")]
	public void TryCreate_ShouldMapDistinctAddPageAsSeparateEntry_WhenAddPageDiffersFromDefault() {
		// Arrange
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage",
			AddPage = "CaseAddPage"
		};

		// Act
		_command.TryCreate(options, out _);

		// Assert
		_captured.Should().NotBeNull(
			because: "the command must forward a built request to the related-page service");
		_captured.Pages.Should().Contain(page => page.IsDefault && page.PageSchemaName == "CaseFormPage",
			because: "the default page maps to an is-default entry");
		_captured.Pages.Should().Contain(page => page.IsAdd && page.PageSchemaName == "CaseAddPage",
			because: "a distinct --add-page maps to its own is-add entry, not the default page");
	}

	[Test]
	[Description("Rejects a portal add page given with no portal default page, since the portal audience would have a page to add a record but none to open one.")]
	public void TryCreate_ShouldFailWithoutCallingService_WhenPortalAddPageHasNoPortalDefaultPage() {
		// Arrange — an employee default satisfies the service-level base-default check, so only the
		// per-audience guard can catch the portal audience having an add page but no default page.
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage",
			PortalAddPage = "CasePortalAddPage"
		};

		// Act
		bool ok = _command.TryCreate(options, out CreateRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeFalse(
			because: "a portal add page with no portal default leaves portal users no page to open");
		response.Success.Should().BeFalse(
			because: "a portal add page with no portal default page is rejected");
		response.Error.Should().Contain("--portal-default-page",
			because: "the error should name the missing portal default page option");
		_service.DidNotReceiveWithAnyArgs().Create(default!);
	}

	[Test]
	[Description("Rejects an internal add page given with no internal default page, since that audience would have a page to add a record but none to open one.")]
	public void TryCreate_ShouldFailWithoutCallingService_WhenAddPageHasNoDefaultPage() {
		// Arrange — a portal default satisfies the service-level base-default check, so only the
		// per-audience guard can catch the internal audience having an add page but no default page.
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			AddPage = "CaseAddPage",
			PortalDefaultPage = "CasePortalFormPage"
		};

		// Act
		bool ok = _command.TryCreate(options, out CreateRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeFalse(
			because: "an internal add page with no internal default leaves internal users no page to open");
		response.Success.Should().BeFalse(
			because: "an internal add page with no internal default page is rejected");
		response.Error.Should().Contain("--default-page",
			because: "the error should name the missing default page option");
		_service.DidNotReceiveWithAnyArgs().Create(default!);
	}

	[Test]
	[Description("Execute returns exit code 0 and logs the serialized response when the create succeeds.")]
	public void Execute_ShouldReturnZero_WhenCreateSucceeds() {
		// Arrange — SetUp wires the service to succeed.
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful create maps to exit code 0 for the shell/CI");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("\"success\":true")));
	}

	[Test]
	[Description("Execute returns exit code 1 and reports the error when the create fails.")]
	public void Execute_ShouldReturnOne_WhenCreateFails() {
		// Arrange — the service throws, so TryCreate catches it and reports failure.
		_service.Create(Arg.Any<RelatedPageAddonRequest>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a failed create maps to exit code 1 for the shell/CI");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("boom")));
	}

	[Test]
	[Description("TryCreate returns false and surfaces the service message in the response — the catch->Fail branch every service-layer error reaches on its way to the CLI user.")]
	public void TryCreate_ShouldReturnFalseWithError_WhenServiceThrows() {
		// Arrange
		_service.Create(Arg.Any<RelatedPageAddonRequest>())
			.Returns(_ => throw new InvalidOperationException("service boom"));
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage"
		};

		// Act
		bool ok = _command.TryCreate(options, out CreateRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeFalse(because: "a service failure is reported as a failed response, not surfaced as an exception");
		response.Success.Should().BeFalse(
			because: "a thrown service error is reported as a failed response envelope");
		response.Error.Should().Contain("service boom",
			because: "the service exception message is carried to the CLI user via response.Error");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("service boom")));
	}

	[Test]
	[Description("TryCreate surfaces a non-fatal service warning (e.g. an unverified type-column on a column-less schema response) in the response payload, so an MCP/API caller that cannot see server logs still detects it; success is unaffected.")]
	public void TryCreate_ShouldSurfaceServiceWarning_InResponse() {
		// Arrange — the service reports success WITH a warning.
		_service.Create(Arg.Any<RelatedPageAddonRequest>()).Returns(
			new RelatedPageAddonResult("entity-uid", "package-uid", 1, "RelatedPage",
				"type-column-uid '...' could not be verified"));
		CreateRelatedPageAddonOptions options = new() {
			EntitySchemaName = "Case",
			PackageName = "Custom",
			DefaultPage = "CaseFormPage"
		};

		// Act
		bool ok = _command.TryCreate(options, out CreateRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeTrue(because: "a non-fatal warning does not fail the operation");
		response.Success.Should().BeTrue(because: "the write succeeded; the warning is advisory");
		response.Warning.Should().Contain("could not be verified",
			because: "the service warning must be carried into the response payload for callers that don't see server logs");
	}

	[Test]
	[Description("TryCreate returns false without calling the service when options is null.")]
	public void TryCreate_ShouldReturnFalseWithoutCallingService_WhenOptionsIsNull() {
		// Act
		bool ok = _command.TryCreate(null, out CreateRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeFalse(because: "a null options object is invalid input");
		response.Success.Should().BeFalse(
			because: "null options is rejected as invalid input");
		response.Error.Should().Contain("options is required",
			because: "the response should explain that options is required");
		_service.DidNotReceiveWithAnyArgs().Create(default!);
	}
}
