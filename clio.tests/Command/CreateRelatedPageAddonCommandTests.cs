namespace Clio.Tests.Command;

using System.Linq;
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
	private RelatedPageAddonRequest _captured;
	private CreateRelatedPageAddonCommand _command = null!;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<IRelatedPageAddonService>();
		_service.Create(Arg.Any<RelatedPageAddonRequest>()).Returns(callInfo => {
			_captured = callInfo.Arg<RelatedPageAddonRequest>();
			return new RelatedPageAddonResult("entity-uid", "package-uid", _captured.Pages.Count, "RelatedPage");
		});
		_command = new CreateRelatedPageAddonCommand(_service, Substitute.For<ILogger>());
	}

	[Test]
	[Description("Building specs from CLI scalar options with a portal page scopes the employee set to 'All employees' and the portal set to 'All external users'.")]
	public void TryCreate_With_Portal_Pages_Scopes_Each_Audience_By_Role_Name() {
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
	public void TryCreate_Without_Portal_Pages_Leaves_Employee_Pages_Role_Less() {
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
	public void TryCreate_With_Distinct_Add_Page_Maps_It_As_A_Separate_Add_Entry() {
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
}
