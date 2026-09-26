using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.Localization;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the <c>localize-page</c> MCP tool (story-page-localization-2, TC-U-21..24).
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class LocalizePageToolTests : BaseClioModuleTests {
	private IToolCommandResolver _resolver;
	private ILocalizePageService _service;
	private LocalizePageTool _tool;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_resolver = Substitute.For<IToolCommandResolver>();
		_service = Substitute.For<ILocalizePageService>();
		_resolver.Resolve<ILocalizePageService>(Arg.Any<EnvironmentOptions>()).Returns(_service);
		containerBuilder.AddSingleton(_resolver);
	}

	public override void Setup() {
		base.Setup();
		_tool = Container.GetRequiredService<LocalizePageTool>();
	}

	public override void TearDown() {
		_resolver.ClearReceivedCalls();
		_service.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("TC-U-21: every argument, including environment-name and the explicit connection fallback, reaches the options passed to the resolved service.")]
	public void LocalizePage_ShouldMapArgumentsToOptions_WhenCalled() {
		// Arrange
		LocalizePageOptions received = null;
		_service.Localize(Arg.Do<LocalizePageOptions>(options => received = options))
			.Returns(new LocalizePageResponse { Success = true });
		LocalizePageArgs args = new(
			SchemaName: "UsrApp_FormPage",
			Culture: "es-ES",
			Resources: "{\"UsrLabel_caption\":\"Etiqueta\"}",
			Caption: "Pagina",
			OutputDirectory: "/work/project",
			EnvironmentName: "dev",
			Uri: "http://localhost:5000",
			Login: "Supervisor",
			Password: "secret");

		// Act
		_tool.LocalizePage(args);

		// Assert
		received.Should().NotBeNull(because: "the tool must delegate to the resolved localize-page service");
		received.Should().BeEquivalentTo(new LocalizePageOptions {
			SchemaName = "UsrApp_FormPage",
			Culture = "es-ES",
			Resources = "{\"UsrLabel_caption\":\"Etiqueta\"}",
			Caption = "Pagina",
			OutputDirectory = "/work/project",
			Environment = "dev",
			Uri = "http://localhost:5000",
			Login = "Supervisor",
			Password = "secret"
		}, because: "every MCP argument must map one-to-one onto the CLI options the command already validates");
	}

	[Test]
	[Description("TC-U-21: a real MCP JSON payload binds every kebab-case field onto the argument record through the server's serializer options.")]
	public void LocalizePageArgs_ShouldBindEveryField_WhenDeserializedFromMcpJson() {
		// Arrange
		const string payload = """
			{"schema-name":"UsrApp_FormPage","culture":"de-DE","resources":"{\"K\":\"V\"}","caption":"Seite",
			 "output-directory":"/work/project","environment-name":"dev","uri":"http://h","login":"u","password":"p"}
			""";

		// Act
		LocalizePageArgs args = JsonSerializer.Deserialize<LocalizePageArgs>(payload, BindingsModule.CreateMcpSerializerOptions());

		// Assert
		args.Should().NotBeNull(because: "the payload is a valid argument object");
		args.SchemaName.Should().Be("UsrApp_FormPage", because: "schema-name must bind by its kebab-case name");
		args.Culture.Should().Be("de-DE", because: "culture must bind");
		args.Resources.Should().Be("{\"K\":\"V\"}", because: "resources is a JSON object string, bound verbatim");
		args.Caption.Should().Be("Seite", because: "caption must bind");
		args.OutputDirectory.Should().Be("/work/project", because: "output-directory must bind by its kebab-case name");
		args.EnvironmentName.Should().Be("dev", because: "environment-name must bind by its kebab-case name");
		args.Uri.Should().Be("http://h", because: "uri must bind");
		args.Login.Should().Be("u", because: "login must bind");
		args.Password.Should().Be("p", because: "password must bind");
		args.ExtensionData.Should().BeNullOrEmpty(because: "no field of a canonical payload may fall into the overflow bag");
	}

	[Test]
	[Description("TC-U-21: omitted optional arguments stay null, so the call is report-only and no connection fallback is invented.")]
	public void LocalizePage_ShouldKeepOptionalFieldsNull_WhenOmitted() {
		// Arrange
		LocalizePageArgs args = new("UsrApp_FormPage", "es-ES", EnvironmentName: "dev");

		// Act
		LocalizePageOptions options = LocalizePageTool.BuildOptions(args);

		// Assert
		options.Resources.Should().BeNull(because: "an omitted resources map means report-only, not an empty write");
		options.Caption.Should().BeNull(because: "an omitted caption must not write an empty title");
		options.Uri.Should().BeNull(because: "the explicit connection fallback is used only when supplied");
		options.OutputDirectory.Should().BeNull(because: "an omitted anchor resolves the baseline the way get-page does by default");
	}

	[Test]
	[Description("TC-U-22: camelCase aliases are rejected with a rename hint to the canonical name and the service is never resolved.")]
	public void LocalizePage_ShouldRejectLegacyAliases_WhenCamelCaseNamesSupplied() {
		// Arrange
		LocalizePageArgs args = JsonSerializer.Deserialize<LocalizePageArgs>(
			"""{"schemaName":"UsrApp_FormPage","culture":"es-ES","environmentName":"dev"}""",
			BindingsModule.CreateMcpSerializerOptions());

		// Act
		LocalizePageResponse response = _tool.LocalizePage(args);

		// Assert
		response.Success.Should().BeFalse(because: "a mis-spelled field must fail instead of being silently dropped");
		response.Error.Should().Contain("'schemaName' -> 'schema-name'",
			because: "the caller must be told the canonical spelling of schema-name");
		response.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the caller must be told the canonical spelling of environment-name");
		_resolver.DidNotReceive().Resolve<ILocalizePageService>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("TC-U-22: an unknown argument is rejected with the list of valid argument names.")]
	public void LocalizePage_ShouldListValidNames_WhenUnknownArgumentSupplied() {
		// Arrange
		LocalizePageArgs args = JsonSerializer.Deserialize<LocalizePageArgs>(
			"""{"schema-name":"UsrApp_FormPage","culture":"es-ES","environment-name":"dev","body":"x"}""",
			BindingsModule.CreateMcpSerializerOptions());

		// Act
		LocalizePageResponse response = _tool.LocalizePage(args);

		// Assert
		response.Success.Should().BeFalse(because: "localize-page takes no body and must say so");
		response.Error.Should().Contain("Valid: schema-name, culture, resources, caption, output-directory, environment-name",
			because: "the error must list the arguments the tool accepts");
		_resolver.DidNotReceive().Resolve<ILocalizePageService>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("TC-U-23: the service is resolved per call for the requested environment, not taken from the startup container.")]
	public void LocalizePage_ShouldResolveCommandForEnvironment_WhenEnvironmentNameGiven() {
		// Arrange
		_service.Localize(Arg.Any<LocalizePageOptions>()).Returns(new LocalizePageResponse { Success = true });

		// Act
		_tool.LocalizePage(new LocalizePageArgs("UsrApp_FormPage", "es-ES", EnvironmentName: "eng90576"));

		// Assert
		_resolver.Received(1).Resolve<ILocalizePageService>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "eng90576"));
	}

	[Test]
	[Description("TC-U-23: the story-1 response is returned unchanged so the CLI and MCP surfaces agree.")]
	public void LocalizePage_ShouldReturnServiceResponseUnchanged_WhenServiceSucceeds() {
		// Arrange
		LocalizePageResponse expected = new() {
			Success = true,
			SchemaName = "UsrApp_FormPage",
			SchemaUId = "777339c8-0000-0000-0000-000000000000",
			PackageName = "UsrApp",
			Culture = "es-ES",
			CultureActive = false,
			Saved = true,
			Written = ["UsrLabel_caption"],
			Unchanged = [],
			CaptionOutcome = LocalizePageResponse.CaptionWritten,
			Coverage = new LocalizePageCoverage { Keys = 14, Translated = 13, Missing = ["UsrOther_caption"] },
			Warnings = ["Culture 'es-ES' exists but is inactive"]
		};
		_service.Localize(Arg.Any<LocalizePageOptions>()).Returns(expected);

		// Act
		LocalizePageResponse response = _tool.LocalizePage(new LocalizePageArgs("UsrApp_FormPage", "es-ES", EnvironmentName: "dev"));

		// Assert
		response.Should().BeSameAs(expected, because: "the tool must pass the command's response through without reshaping it");
	}

	[Test]
	[Description("TC-U-23: an absent culture reported by the service reaches the caller as success:false with the Languages-section message.")]
	public void LocalizePage_ShouldReturnErrorEnvelope_WhenCultureIsAbsent() {
		// Arrange
		string message = string.Format(CultureMessages.CultureAbsentMessageFormat, "xx-XX", "en-US, es-ES");
		_service.Localize(Arg.Any<LocalizePageOptions>())
			.Returns(new LocalizePageResponse { Success = false, SchemaName = "UsrApp_FormPage", Error = message });

		// Act
		LocalizePageResponse response = _tool.LocalizePage(new LocalizePageArgs("UsrApp_FormPage", "xx-XX", EnvironmentName: "dev"));

		// Assert
		response.Success.Should().BeFalse(because: "an absent culture must fail rather than report a silent success");
		response.Error.Should().Contain("Languages section",
			because: "the agent must be told where the culture is added");
	}

	[Test]
	[Description("TC-U-23: a resolution failure (for example an unknown environment) is returned as a structured failure, not thrown.")]
	public void LocalizePage_ShouldReturnFailure_WhenResolutionThrows() {
		// Arrange
		_resolver.Resolve<ILocalizePageService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'missing' not found"));

		// Act
		LocalizePageResponse response = _tool.LocalizePage(new LocalizePageArgs("UsrApp_FormPage", "es-ES", EnvironmentName: "missing"));

		// Assert
		response.Success.Should().BeFalse(because: "a resolution failure must stay inside the response envelope");
		response.SchemaName.Should().Be("UsrApp_FormPage", because: "the failure must name the page it was about");
		response.Error.Should().Contain("not found", because: "the caller needs the reason");
	}

	[Test]
	[Description("TC-U-24: the SDK-emitted input schema requires exactly schema-name and culture, so strict clients accept calls without the optional fields.")]
	public void ToolSchema_ShouldRequireOnlySchemaNameAndCulture_WhenListed() {
		// Arrange
		using JsonDocument schema = EmittedSchemaProbe.EmittedInputSchema(LocalizePageTool.ToolName);

		// Act
		JsonElement arguments = EmittedSchemaProbe.EffectiveArgumentSchema(schema.RootElement);
		string[] required = EmittedSchemaProbe.RequiredNames(arguments);

		// Assert
		required.Should().BeEquivalentTo(["schema-name", "culture"],
			because: "the SDK derives required from non-nullable positional parameters; every other field is optional");
		foreach (string field in new[] { "resources", "caption", "output-directory", "environment-name", "uri", "login", "password" }) {
			EmittedSchemaProbe.Advertises(arguments, field).Should().BeTrue(
				because: $"the emitted schema must advertise the optional field '{field}'");
		}
	}

	[Test]
	[Description("The tool annotations match update-page for write safety and declare the call idempotent.")]
	public void LocalizePage_ShouldDeclareWriteAnnotations_WhenInspected() {
		// Arrange
		MethodInfo method = typeof(LocalizePageTool).GetMethod(nameof(LocalizePageTool.LocalizePage));

		// Act
		McpServerToolAttribute attribute = method!.GetCustomAttribute<McpServerToolAttribute>();

		// Assert
		attribute.Should().NotBeNull(because: "the method must be an MCP tool");
		attribute!.Name.Should().Be(LocalizePageTool.ToolName, because: "the tool name is the production constant");
		attribute.ReadOnly.Should().BeFalse(because: "the tool saves the page schema");
		attribute.Destructive.Should().BeTrue(because: "it overwrites an existing value in the target culture, like update-page");
		attribute.Idempotent.Should().BeTrue(because: "the same call twice leaves the same state and the second one does not save");
	}

	[Test]
	[Description("localize-page stays long-tail: it is not resident in the core tools/list profile and is reached through clio-run / get-tool-contract.")]
	public void LocalizePage_ShouldNotBeResident_WhenCoreProfileInspected() {
		// Arrange
		IEnumerable<Type> coreTools = Clio.Command.McpServer.McpCoreToolProfile.CoreToolTypes;

		// Act
		bool resident = coreTools.Contains(typeof(LocalizePageTool));

		// Assert
		resident.Should().BeFalse(because: "ADR D1 keeps localize-page long-tail, like update-page");
	}
}
