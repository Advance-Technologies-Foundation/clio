using System.ComponentModel.DataAnnotations;
using System.Linq;
using Clio.Command.Branding;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class SetLogoToolTests {

	private const string LogoFile = "C:/brand/logo.svg";

	/// <summary>An arbitrary package the fake command reports back; the tool only relays whatever it resolved.</summary>
	private const string BoundPackageName = "UsrBrandingPkg";

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the set-logo tool method: a destructive write (the product logos change for all users and cannot be automatically reverted), idempotent (re-applying the same files converges to the same state), closed-world.")]
	public void SetLogoTool_ShouldDeclareDestructiveWriteSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SetLogoTool)
			.GetMethod(nameof(SetLogoTool.SetLogo))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(SetLogoTool.ToolName,
			because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "applying logos writes to the environment");
		attribute.Destructive.Should().BeTrue(
			because: "the product logos change for all users and cannot be automatically reverted, so the MCP host must prompt before running it");
		attribute.Idempotent.Should().BeTrue(
			because: "re-applying the same files converges to the same logo state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void SetLogoTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(SetLogoTool)
			.GetMethod(nameof(SetLogoTool.SetLogo))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-scoped set-logo command, forwards every slot and the package, and returns a structured success result.")]
	public void SetLogo_ShouldResolveCommandAndReturnSuccess() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		FakeSetLogoCommand resolvedCommand = new(SetLogoResult.Successful(["logo", "dark-logo"], "UsrMyApp", []));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>())
			.Returns(resolvedCommand);
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetLogoToolResult result = tool.SetLogo(new SetLogoArgs(
			EnvironmentName: "docker_fix2", Logo: LogoFile, DarkLogo: "C:/brand/logo-white.svg",
			Package: "UsrMyApp"));

		// Assert
		result.Success.Should().BeTrue(because: "applied and bound logos must report success");
		commandResolver.Received(1).Resolve<SetLogoCommand>(Arg.Is<SetLogoOptions>(options =>
			options.Environment == "docker_fix2"
			&& options.Logo == LogoFile
			&& options.DarkLogo == "C:/brand/logo-white.svg"
			&& options.PackageName == "UsrMyApp"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command instance should apply the logos");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path must use the resolved command instance, not the startup-time one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted, without resolving a command.")]
	public void SetLogo_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetLogoToolResult result = tool.SetLogo(new SetLogoArgs(Logo: LogoFile));

		// Assert
		result.Success.Should().BeFalse(because: "a request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming the accepted slot fields when no slot is passed, without resolving a command.")]
	public void SetLogo_ShouldReturnFailure_WhenNoLogoSlotIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetLogoToolResult result = tool.SetLogo(new SetLogoArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "a request with no logo file has nothing to apply");
		result.Error.Should().Contain("at least one logo",
			because: "the failure must tell the caller that one of the slot fields is required");
		commandResolver.DidNotReceive().Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Carries the applied slots, the bound package, and the reconcile's skipped entries on the structured result for relay to the user.")]
	public void SetLogo_ShouldCarryAppliedPackageAndSkippedEntries_OnTheResult() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		FakeSetLogoCommand resolvedCommand = new(SetLogoResult.Successful(
			["logo"], BoundPackageName, ["MenuLogoImage: no All-Users value on this environment"]));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>())
			.Returns(resolvedCommand);
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetLogoToolResult result = tool.SetLogo(new SetLogoArgs(
			EnvironmentName: "docker_fix2", Logo: LogoFile));

		// Assert
		result.Applied.Should().BeEquivalentTo(["logo"],
			because: "the caller must learn which slots were actually written");
		result.Package.Should().Be(BoundPackageName,
			because: "the caller must learn which package the logo data was bound into");
		result.Warnings.Should().ContainSingle(entry => entry.Contains("MenuLogoImage"),
			because: "the delivery gaps the reconcile reported must reach the MCP caller for relay to the user");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts sensitive tokens (a full request URI carrying the target host and embedded credentials) out of the command's failure message before it crosses into the MCP transcript, while keeping the human-readable reason intact.")]
	public void SetLogo_ShouldRedactSensitiveErrorText_WhenCommandFailsWithSensitiveMessage() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string sensitiveMessage =
			"Applying logo failed: POST https://admin:s3cr3t@stand.creatio.com/0/DataService returned 500.";
		FakeSetLogoCommand defaultCommand = new();
		FakeSetLogoCommand resolvedCommand = new(SetLogoResult.Failure(sensitiveMessage));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>())
			.Returns(resolvedCommand);
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetLogoToolResult result = tool.SetLogo(new SetLogoArgs(
			EnvironmentName: "docker_fix2", Logo: LogoFile));

		// Assert
		result.Success.Should().BeFalse(because: "a failed apply must not report success");
		result.Error.Should().NotContain("s3cr3t",
			because: "the credential embedded in the request URI must never reach the MCP transcript");
		result.Error.Should().NotContain("stand.creatio.com",
			because: "the target host must be scrubbed from the surfaced error");
		result.Error.Should().Contain("Applying logo failed",
			because: "the human-readable reason must survive redaction so the agent can self-correct");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a camelCase legacy alias with a structured rename hint instead of silently ignoring the misnamed field.")]
	public void SetLogo_ShouldReturnRenameHint_WhenLegacyAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		SetLogoArgs args = new(EnvironmentName: "docker_fix2") {
			ExtensionData = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> {
				["menuLogo"] = System.Text.Json.JsonSerializer.SerializeToElement("C:/brand/menu.svg")
			}
		};

		// Act
		SetLogoToolResult result = tool.SetLogo(args);

		// Assert
		result.Error.Should().Contain("menu-logo",
			because: "the rename hint must name the canonical kebab-case field so the caller can fix the call");
		commandResolver.DidNotReceive().Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the login-logo argument onto the command's own login slot, so the all-slots logo argument and the login slot stay distinct over the wire.")]
	public void SetLogo_ShouldMapLoginLogo_OntoTheLoginSlot() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		FakeSetLogoCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>()).Returns(resolvedCommand);
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		tool.SetLogo(new SetLogoArgs(EnvironmentName: "docker_fix2", LoginLogo: LogoFile));

		// Assert
		resolvedCommand.CapturedOptions.LoginLogo.Should().Be(LogoFile,
			because: "login-logo brands one slot while logo brands them all, so mapping it onto the wrong property would silently write four slots");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts login-logo alone as a complete request, so a caller branding only the login page does not have to pass the all-slots argument.")]
	public void SetLogo_ShouldAcceptLoginLogo_AsTheOnlySlot() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetLogoCommand>(Arg.Any<SetLogoOptions>()).Returns(new FakeSetLogoCommand());
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetLogoToolResult result = tool.SetLogo(new SetLogoArgs(EnvironmentName: "docker_fix2", LoginLogo: LogoFile));

		// Assert
		result.Success.Should().BeTrue(
			because: "login-logo is one of the accepted slot arguments, so a request carrying only it has something to apply");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a snake_case slot field with the canonical kebab-case rename hint, because an agent that guesses snake_case would otherwise have its file silently dropped.")]
	public void SetLogo_ShouldReturnRenameHint_WhenASnakeCaseSlotFieldIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		SetLogoArgs args = new(EnvironmentName: "docker_fix2") {
			ExtensionData = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> {
				["login_logo"] = System.Text.Json.JsonSerializer.SerializeToElement(LogoFile)
			}
		};

		// Act
		SetLogoToolResult result = tool.SetLogo(args);

		// Assert
		result.Error.Should().Contain("login-logo",
			because: "snake_case is as likely a guess as camelCase, so it must produce the same actionable rename hint instead of being dropped into the overflow bag");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects package-name and package_name with the canonical rename hint, so the two most likely spellings of the package field do not silently redirect the delivery.")]
	public void SetLogo_ShouldReturnRenameHint_WhenAPackageNameVariantIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetLogoCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetLogoTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		SetLogoArgs args = new(EnvironmentName: "docker_fix2", Logo: LogoFile) {
			ExtensionData = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> {
				["package-name"] = System.Text.Json.JsonSerializer.SerializeToElement("UsrMyApp")
			}
		};

		// Act
		SetLogoToolResult result = tool.SetLogo(args);

		// Assert
		result.Error.Should().Contain("'package'",
			because: "a dropped package field would deliver the branding into the environment's current package instead of the one the caller named, which is a silent wrong-target write");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeSetLogoCommand : SetLogoCommand {
		private readonly SetLogoResult _result;

		public SetLogoOptions CapturedOptions { get; private set; }

		public FakeSetLogoCommand(SetLogoResult result = null)
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				new SysSettingsCommand(Substitute.For<ISysSettingsManager>(), Substitute.For<ILogger>(),
					Substitute.For<IFileSystem>()),
				Substitute.For<IBrandingBindingService>(), Substitute.For<IFileSystem>()) {
			_result = result ?? SetLogoResult.Successful(["logo"], BoundPackageName, []);
		}

		public override SetLogoResult ApplyLogos(SetLogoOptions options) {
			CapturedOptions = options;
			return _result;
		}
	}
}
