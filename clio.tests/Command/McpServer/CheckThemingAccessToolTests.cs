using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
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
	[Description("Declares the safety flags on the check-theming-access tool method: a read-only, non-destructive, idempotent, closed-world permission probe.")]
	public void CheckThemingAccessTool_ShouldDeclareCheckSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CheckThemingAccessTool)
			.GetMethod(nameof(CheckThemingAccessTool.CheckThemingAccess))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CheckThemingAccessTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeTrue(because: "the access check only reads rights and license status");
		attribute.Destructive.Should().BeFalse(because: "a read never destroys state");
		attribute.Idempotent.Should().BeTrue(because: "repeated checks return the same verdict for unchanged grants");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only queries the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void CheckThemingAccessTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(CheckThemingAccessTool)
			.GetMethod(nameof(CheckThemingAccessTool.CheckThemingAccess))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("Resolves both clients for the requested environment and reports full theming access when the operation right and the license are both granted.")]
	[Category("Unit")]
	public void CheckThemingAccess_ShouldReportAccess_WhenOperationAndLicenseGranted() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient rights,
			ICreatioLicenseClient license) = CreateTool();
		rights.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(true);
		license.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool> { ["CanCustomizeBranding"] = true });

		// Act
		ThemingAccessResult result = tool.CheckThemingAccess(new CheckThemingAccessArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeTrue(because: "a completed access check must report success");
		result.CanManageThemes.Should().BeTrue(because: "the operation-right check returned true");
		result.CanCustomizeBranding.Should().BeTrue(because: "the license check returned true");
		resolver.Received(1).Resolve<CheckThemingAccessCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "docker_fix2"));
		rights.Received(1).GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns a structured failure without resolving any client when the environment name is empty.")]
	[Category("Unit")]
	public void CheckThemingAccess_ShouldReturnFailure_WhenEnvironmentNameIsEmpty() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient _, ICreatioLicenseClient __) = CreateTool();

		// Act
		ThemingAccessResult result = tool.CheckThemingAccess(new CheckThemingAccessArgs(EnvironmentName: "   "));

		// Assert
		result.Success.Should().BeFalse(because: "an empty environment name is an invalid request and must not succeed");
		result.Error.Should().NotBeNullOrWhiteSpace(because: "the failure must carry a diagnostic message");
		resolver.DidNotReceive().Resolve<CheckThemingAccessCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted.")]
	[Category("Unit")]
	public void CheckThemingAccess_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient _, ICreatioLicenseClient __) = CreateTool();

		// Act
		ThemingAccessResult result = tool.CheckThemingAccess(new CheckThemingAccessArgs());

		// Assert
		result.Success.Should().BeFalse(because: "an access check without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		resolver.DidNotReceive().Resolve<CheckThemingAccessCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	[Category("Unit")]
	public void CheckThemingAccess_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient _, ICreatioLicenseClient __) = CreateTool();
		CheckThemingAccessArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		ThemingAccessResult result = tool.CheckThemingAccess(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		resolver.DidNotReceive().Resolve<CheckThemingAccessCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Surfaces the underlying failure as a structured failure result when a resolved client throws.")]
	[Category("Unit")]
	public void CheckThemingAccess_ShouldReturnFailure_WhenClientThrows() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver _, ICreatioRightsClient rights, ICreatioLicenseClient __) = CreateTool();
		rights.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("Unexpected response from RightsService: OK"));

		// Act
		ThemingAccessResult result = tool.CheckThemingAccess(new CheckThemingAccessArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "a thrown transport/parse error must surface as a tool failure");
		result.Error.Should().Contain("RightsService", because: "the underlying diagnostic message must be forwarded");
	}

	[Test]
	[Description("Binds the check-theming-access argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void CheckThemingAccessArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		CheckThemingAccessArgs kebab = JsonSerializer.Deserialize<CheckThemingAccessArgs>(
			"""{"environment-name":"docker_fix2"}""", options)!;
		CheckThemingAccessArgs camel = JsonSerializer.Deserialize<CheckThemingAccessArgs>(
			"""{"environmentName":"docker_fix2"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2", because: "the advertised kebab-case environment-name field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.EnvironmentName.Should().BeNull(
			because: "environmentName is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("environmentName",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	[Test]
	[Description("Returns a structured failure carrying the version-requirement message and never probes rights or license when the target environment does not satisfy the Creatio version floor.")]
	[Category("Unit")]
	public void CheckThemingAccess_ShouldReturnFailure_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		(CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient rights,
			ICreatioLicenseClient _) = CreateTool();
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);

		// Act
		ThemingAccessResult result = tool.CheckThemingAccess(new CheckThemingAccessArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unmet Creatio version requirement must refuse the access probe exactly as the theme CRUD tools are refused");
		result.Error.Should().Contain("requires Creatio 10.0.0 or later",
			because: "the version-requirement message must be surfaced to the MCP caller");
		result.Error.Should().Contain($"[{CreatioVersionRequirementException.VersionTooOldCode}]",
			because: "the typed result carries no exit code, so the stable machine-readable ErrorCode must travel in the error message");
		rights.DidNotReceive().GetCanExecuteOperation(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
	}

	private static (CheckThemingAccessTool tool, IToolCommandResolver resolver, ICreatioRightsClient rights,
		ICreatioLicenseClient license) CreateTool() {
		ICreatioRightsClient rights = Substitute.For<ICreatioRightsClient>();
		ICreatioLicenseClient license = Substitute.For<ICreatioLicenseClient>();
		CheckThemingAccessCommand resolvedCommand = new(rights, license, Substitute.For<ILogger>());
		CheckThemingAccessCommand defaultCommand = new(
			Substitute.For<ICreatioRightsClient>(), Substitute.For<ICreatioLicenseClient>(),
			Substitute.For<ILogger>());
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<CheckThemingAccessCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CheckThemingAccessTool tool = new(defaultCommand, Substitute.For<ILogger>(), resolver);
		return (tool, resolver, rights, license);
	}
}
