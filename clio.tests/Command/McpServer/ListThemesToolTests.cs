using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Clio.Command;
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
public class ListThemesToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the list-themes tool method: a read-only, non-destructive, idempotent, closed-world query.")]
	public void ListThemesTool_ShouldDeclareListSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ListThemesTool)
			.GetMethod(nameof(ListThemesTool.ListThemes))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ListThemesTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeTrue(because: "listing themes only reads the environment's theme catalog");
		attribute.Destructive.Should().BeFalse(because: "a read never destroys state");
		attribute.Idempotent.Should().BeTrue(because: "repeated listing returns the same catalog for unchanged state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only queries the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void ListThemesTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(ListThemesTool)
			.GetMethod(nameof(ListThemesTool.ListThemes))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("Resolves the list-themes MCP tool for the requested environment and returns the resolved command's themes as a structured success result.")]
	[Category("Unit")]
	public void ListThemes_ShouldResolveCommandAndReturnThemes() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IReadOnlyList<ThemeDescriptor> themes = new List<ThemeDescriptor> {
			new() { Id = "ocean-theme", Caption = "Ocean", CssClassName = "ocean-theme", CssFilePath = "a/theme.css" }
		};
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(themes);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeTrue(because: "a successful catalog read must report success");
		result.Themes.Should().ContainSingle(because: "the single theme from the resolved command must be surfaced")
			.Which.Id.Should().Be("ocean-theme", because: "the descriptor fields must be mapped into the structured result");
		commandResolver.Received(1).Resolve<ListThemesCommand>(Arg.Is<ListThemesOptions>(options =>
			options.Environment == "docker_fix2"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command instance should have been queried for the themes");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the injected one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void ListThemes_ShouldReturnFailure_WhenEnvironmentNameIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs(EnvironmentName: "   "));

		// Assert
		result.Success.Should().BeFalse(because: "an empty environment name is an invalid request and must not succeed");
		result.Error.Should().NotBeNullOrWhiteSpace(because: "the failure must carry a diagnostic message");
		commandResolver.DidNotReceive().Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps an empty resolved catalog to a successful result with an empty themes array, per the documented empty-means-no-themes-or-no-license contract.")]
	[Category("Unit")]
	public void ListThemes_ShouldReturnEmptyThemes_WhenCatalogIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(themes: Array.Empty<ThemeDescriptor>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeTrue(
			because: "an empty catalog is a successful read, not a failure");
		result.Themes.Should().NotBeNull(
			because: "a successful read must surface the themes array even when it is empty");
		result.Themes.Should().BeEmpty(
			because: "an environment without custom themes (or without the CanCustomizeBranding license) returns an empty catalog");
		result.Error.Should().BeNull(
			because: "a successful read carries no failure message");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Strips control characters and length-caps server-provided theme fields in the structured result, mirroring the CLI printer's sanitization of the same untrusted catalog data.")]
	[Category("Unit")]
	public void ListThemes_ShouldSanitizeThemeFields_WhenCatalogCarriesControlCharacters() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string hostileCaption = "Ocean\u001b[31m\nforged";
		string overlongId = new('a', 150);
		IReadOnlyList<ThemeDescriptor> themes = new List<ThemeDescriptor> {
			new() { Id = overlongId, Caption = hostileCaption, CssClassName = "ocean\ttheme", CssFilePath = "a/\rtheme.css" }
		};
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(themes);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeTrue(because: "hostile field content must not break a successful catalog read");
		ThemeDescriptorResult sanitized = result.Themes.Should().ContainSingle(
			because: "the single hostile theme must still be surfaced, sanitized").Subject;
		sanitized.Caption.Should().Be("Ocean [31m forged",
			because: "escape and newline control characters must be replaced with spaces so the caption cannot forge output or inject terminal escapes");
		sanitized.Id.Should().Be(new string('a', 100) + "...",
			because: "the id must be capped at the same MaxIdLength the CLI printer applies");
		sanitized.CssClassName.Should().Be("ocean theme",
			because: "tab control characters must be replaced with spaces");
		sanitized.CssFilePath.Should().Be("a/ theme.css",
			because: "carriage-return control characters must be replaced with spaces");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Surfaces the ThemeService failure message as a structured failure when the resolved command reports success=false.")]
	[Category("Unit")]
	public void ListThemes_ShouldReturnFailure_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(themes: null, success: false, error: "no permission");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "an explicit success=false read must surface as a tool failure");
		result.Error.Should().Contain("no permission",
			because: "the server-provided failure message must be forwarded to the caller");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Redacts a sensitive ThemeService error body before it crosses into the MCP client transcript (review: b-horodyskyi — the TryGetAvailableThemes errorMessage out-param bypassed ExecuteResolved's exception handling entirely).")]
	[Category("Unit")]
	public void ListThemes_ShouldRedactSensitiveText_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(themes: null, success: false,
			error: "Unexpected response from server: https://internal-host.example/ThemeService?token=sekret123");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "an explicit success=false read must surface as a tool failure");
		result.Error.Should().NotContain("internal-host.example",
			because: "the server-provided errorMessage can carry a target host, so it must be redacted before crossing the MCP boundary");
		result.Error.Should().NotContain("sekret123",
			because: "the server-provided errorMessage can carry a credential value, so it must be redacted before crossing the MCP boundary");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted.")]
	[Category("Unit")]
	public void ListThemes_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs());

		// Assert
		result.Success.Should().BeFalse(because: "a list request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	[Category("Unit")]
	public void ListThemes_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		ListThemesArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		ListThemesResult result = tool.ListThemes(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the list-themes argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void ListThemesArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		ListThemesArgs kebab = JsonSerializer.Deserialize<ListThemesArgs>(
			"""{"environment-name":"docker_fix2"}""", options)!;
		ListThemesArgs camel = JsonSerializer.Deserialize<ListThemesArgs>(
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
	[Description("Returns a structured failure carrying the version-requirement message and never reads the catalog when the target environment does not satisfy the Creatio version floor.")]
	[Category("Unit")]
	public void ListThemes_ShouldReturnFailure_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new();
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemes(new ListThemesArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unmet Creatio version requirement must refuse the read on the MCP surface exactly as the CLI gate does");
		result.Error.Should().Contain("requires Creatio 10.0.0 or later",
			because: "the version-requirement message must be surfaced to the MCP caller");
		result.Error.Should().Contain($"[{CreatioVersionRequirementException.VersionTooOldCode}]",
			because: "the typed result carries no exit code, so the stable machine-readable ErrorCode must travel in the error message");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the catalog must never be queried when the environment does not satisfy the version floor");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeListThemesCommand : ListThemesCommand {
		private readonly IReadOnlyList<ThemeDescriptor> _themes;
		private readonly bool _success;
		private readonly string _error;

		public ListThemesOptions CapturedOptions { get; private set; }

		public FakeListThemesCommand(IReadOnlyList<ThemeDescriptor> themes = null, bool success = true,
			string error = null)
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) {
			_themes = themes ?? Array.Empty<ThemeDescriptor>();
			_success = success;
			_error = error;
		}

		public override bool TryGetAvailableThemes(ListThemesOptions options,
			out IReadOnlyList<ThemeDescriptor> themes, out string errorMessage) {
			CapturedOptions = options;
			themes = _themes;
			errorMessage = _error;
			return _success;
		}
	}
}
