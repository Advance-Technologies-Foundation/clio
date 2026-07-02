using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class CheckThemingAccessToolTests {

	[Test]
	[Category("Unit")]
	[TestCase(nameof(CheckThemingAccessTool.CheckThemingAccessByName))]
	[TestCase(nameof(CheckThemingAccessTool.CheckThemingAccessByCredentials))]
	[Description("Declares the safety flags on both check-theming-access tool methods: a read-only, non-destructive, idempotent, closed-world permission probe.")]
	public void CheckThemingAccessTool_Should_DeclareCheckSafetyFlags_WhenInspectingMcpServerToolAttribute(string methodName) {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CheckThemingAccessTool)
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.ReadOnly.Should().BeTrue(because: "the access check only reads rights and license status");
		attribute.Destructive.Should().BeFalse(because: "a read never destroys state");
		attribute.Idempotent.Should().BeTrue(because: "repeated checks return the same verdict for unchanged grants");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only queries the addressed Creatio environment");
	}

	private static (CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient rights,
		ICreatioLicenseClient license) CreateTool() {
		ICreatioRightsClient rights = Substitute.For<ICreatioRightsClient>();
		ICreatioLicenseClient license = Substitute.For<ICreatioLicenseClient>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<ICreatioRightsClient>(Arg.Any<EnvironmentOptions>()).Returns(rights);
		resolver.Resolve<ICreatioLicenseClient>(Arg.Any<EnvironmentOptions>()).Returns(license);
		CheckThemingAccessTool tool = new(resolver);
		return (tool, resolver, rights, license);
	}

	[Test]
	[Description("Resolves both clients for the requested environment and reports full theming access when the operation right and the license are both granted.")]
	[Category("Unit")]
	public void CheckThemingAccessByEnvironment_Should_Report_Access_When_Operation_And_License_Granted() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient rights,
			ICreatioLicenseClient license) = CreateTool();
		rights.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(true);
		license.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool> { ["CanCustomizeBranding"] = true });

		// Act
		ThemingAccessResult result = tool.CheckThemingAccessByName("docker_fix2");

		// Assert
		result.Success.Should().BeTrue(because: "a completed access check must report success");
		result.CanManageThemes.Should().BeTrue(because: "the operation-right check returned true");
		result.CanCustomizeBranding.Should().BeTrue(because: "the license check returned true");
		resolver.Received(1).Resolve<ICreatioRightsClient>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "docker_fix2"));
		rights.Received(1).GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Surfaces canCustomizeBranding=false when the operation right is granted but the CanCustomizeBranding license is absent from the status map.")]
	[Category("Unit")]
	public void CheckThemingAccessByEnvironment_Should_Report_No_License_When_License_Missing() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver _, ICreatioRightsClient rights,
			ICreatioLicenseClient license) = CreateTool();
		rights.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(true);
		license.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool>());

		// Act
		ThemingAccessResult result = tool.CheckThemingAccessByName("docker_fix2");

		// Assert
		result.Success.Should().BeTrue(because: "a completed check with a missing license is still a successful check");
		result.CanManageThemes.Should().BeTrue(because: "the operation right was reported as granted");
		result.CanCustomizeBranding.Should().BeFalse(because: "the CanCustomizeBranding license was absent from the status map");
	}

	[Test]
	[Description("Returns a structured failure without resolving any client when the environment name is empty.")]
	[Category("Unit")]
	public void CheckThemingAccessByEnvironment_Should_Return_Failure_When_Environment_Name_Is_Empty() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient _, ICreatioLicenseClient __) = CreateTool();

		// Act
		ThemingAccessResult result = tool.CheckThemingAccessByName("   ");

		// Assert
		result.Success.Should().BeFalse(because: "an empty environment name is an invalid request and must not succeed");
		result.Error.Should().NotBeNullOrWhiteSpace(because: "the failure must carry a diagnostic message");
		resolver.DidNotReceive().Resolve<ICreatioRightsClient>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Surfaces the underlying failure as a structured failure result when a resolved client throws.")]
	[Category("Unit")]
	public void CheckThemingAccessByEnvironment_Should_Return_Failure_When_Client_Throws() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver _, ICreatioRightsClient rights, ICreatioLicenseClient __) = CreateTool();
		rights.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("Unexpected response from RightsService: OK"));

		// Act
		ThemingAccessResult result = tool.CheckThemingAccessByName("docker_fix2");

		// Assert
		result.Success.Should().BeFalse(because: "a thrown transport/parse error must surface as a tool failure");
		result.Error.Should().Contain("RightsService", because: "the underlying diagnostic message must be forwarded");
	}

	[Test]
	[Description("Resolves the clients for the credentials request and preserves the default false value for isNetCore when the argument is omitted.")]
	[Category("Unit")]
	public void CheckThemingAccessByCredentials_Should_Use_Default_IsNetCore_When_Omitted() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient rights,
			ICreatioLicenseClient license) = CreateTool();
		rights.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(true);
		license.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool> { ["CanCustomizeBranding"] = true });

		// Act
		ThemingAccessResult result = tool.CheckThemingAccessByCredentials("http://localhost:5000", "Supervisor", "Supervisor");

		// Assert
		result.Success.Should().BeTrue(because: "the credentials tool should resolve and compose the checks");
		resolver.Received(1).Resolve<ICreatioRightsClient>(Arg.Is<EnvironmentOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.Login == "Supervisor" &&
			options.Password == "Supervisor" &&
			options.IsNetCore == false));
	}

	[Test]
	[Description("Returns a structured failure carrying the missing-field message when a required credential argument is empty.")]
	[Category("Unit")]
	public void CheckThemingAccessByCredentials_Should_Return_Failure_When_Url_Is_Empty() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient _, ICreatioLicenseClient __) = CreateTool();

		// Act
		ThemingAccessResult result = tool.CheckThemingAccessByCredentials("  ", "Supervisor", "Supervisor");

		// Assert
		result.Success.Should().BeFalse(because: "an empty url is an invalid request and must not resolve a client");
		result.Error.Should().Contain("url", because: "the failure must point at the missing field");
		resolver.DidNotReceive().Resolve<ICreatioRightsClient>(Arg.Any<EnvironmentOptions>());
	}
}
