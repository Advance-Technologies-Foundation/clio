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
	[Description("Building specs from CLI scalar options with a portal page scopes the employee set to 'All employees' and the portal set to 'All external users'.")]
	public void TryCreate_ShouldScopeEachAudienceByRoleName_WhenPortalPageProvided() {
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
		_captured.Pages.Should().Contain(page => page.RoleName == "All employees" && page.IsDefault,
			because: "the internal default page is scoped to All employees once a portal audience is also configured");
		_captured.Pages.Should().Contain(page => page.RoleName == "All employees" && page.IsAdd,
			because: "the internal add page is scoped to All employees alongside the internal default page");
		_captured.Pages.Should().Contain(page => page.RoleName == "All external users" && page.IsDefault,
			because: "portal-default-page binds the All external users (portal) audience");
		_captured.Pages.Should().Contain(page => page.RoleName == "All external users" && page.IsAdd,
			because: "the portal add page defaults to the portal default page");
	}

	[Test]
	[Description("Building specs from CLI scalar options with no portal page leaves the employee pages role-less so they apply to everyone.")]
	public void TryCreate_ShouldLeaveEmployeePagesRoleLess_WhenNoPortalPageProvided() {
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
		_captured.Pages.Should().OnlyContain(page => string.IsNullOrEmpty(page.RoleName),
			because: "without a portal audience the employee pages stay role-less and apply to everyone");
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
		response.Success.Should().BeFalse();
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
		response.Success.Should().BeFalse();
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
}
