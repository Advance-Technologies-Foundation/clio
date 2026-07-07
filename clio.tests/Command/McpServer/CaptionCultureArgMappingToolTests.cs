using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Verifies that optional MCP arguments are threaded from each tool's argument record into the
/// command options / service request that the tool ultimately executes.
/// Covers <c>caption-culture</c> on the four tools that gained the argument (create-entity-schema,
/// modify-entity-schema-column, create-page, create-app-section) and <c>optional-properties</c>
/// on create-page.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CaptionCultureArgMappingToolTests {

	private const string CaptionCultureValue = "uk-UA";

	[Test]
	[Description("create-entity-schema maps args.caption-culture into CreateEntitySchemaOptions.CaptionCulture.")]
	public void CreateOptions_ShouldMapCaptionCulture_WhenCaptionCultureProvided() {
		// Arrange
		CreateEntitySchemaArgs args = new(
			"Custom",
			"UsrAlpha",
			new Dictionary<string, string> { ["en-US"] = "Alpha" },
			"dev") {
			CaptionCulture = CaptionCultureValue
		};

		// Act
		CreateEntitySchemaOptions options = CreateEntitySchemaTool.CreateOptions(args, args.ParentSchemaName, args.ExtendParent);

		// Assert
		options.CaptionCulture.Should().Be(CaptionCultureValue,
			"because the create-entity-schema tool must thread the caption-culture argument into the command options");
	}

	[Test]
	[Description("modify-entity-schema-column maps args.caption-culture into ModifyEntitySchemaColumnOptions.CaptionCulture.")]
	public void ModifyEntitySchemaColumn_ShouldMapCaptionCulture_WhenCaptionCultureProvided() {
		// Arrange
		CapturingModifyCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyEntitySchemaColumnCommand>(Arg.Any<ModifyEntitySchemaColumnOptions>())
			.Returns(command);
		ModifyEntitySchemaColumnTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		ModifyEntitySchemaColumnArgs args = new("dev", "Custom", "UsrAlpha", "modify", "UsrField") {
			CaptionCulture = CaptionCultureValue
		};

		// Act
		ConsoleLogger.Instance.ClearMessages();
		tool.ModifyEntitySchemaColumn(args);

		// Assert
		command.CapturedOptions.Should().NotBeNull("because the resolved command must receive the mapped options");
		command.CapturedOptions!.CaptionCulture.Should().Be(CaptionCultureValue,
			"because the modify-entity-schema-column tool must thread the caption-culture argument into the command options");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("create-page maps args.caption-culture into PageCreateOptions.CaptionCulture.")]
	public void CreatePage_ShouldMapCaptionCulture_WhenCaptionCultureProvided() {
		// Arrange
		CapturingPageCreateCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>())
			.Returns(command);
		PageCreateTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		PageCreateArgs args = new("UsrPage", "BlankPageTemplate", "Custom", null, null, null, "dev", null, null, null,
			CaptionCultureValue);

		// Act
		ConsoleLogger.Instance.ClearMessages();
		tool.CreatePage(args);

		// Assert
		command.CapturedOptions.Should().NotBeNull("because the resolved command must receive the mapped options");
		command.CapturedOptions!.CaptionCulture.Should().Be(CaptionCultureValue,
			"because the create-page tool must thread the caption-culture argument into the command options");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("create-page maps args.optional-properties into PageCreateOptions.OptionalProperties.")]
	public void CreatePage_ShouldMapOptionalProperties_WhenOptionalPropertiesProvided() {
		// Arrange
		const string optionalProperties =
			"""[{"key":"DashboardsEntitySchemaName","value":"Contact"}]""";
		CapturingPageCreateCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>())
			.Returns(command);
		PageCreateTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		PageCreateArgs args = new("UsrPage", "BaseDashboardTemplate", "Custom", null, null, null, "dev", null, null, null,
			OptionalProperties: optionalProperties);

		// Act
		ConsoleLogger.Instance.ClearMessages();
		tool.CreatePage(args);

		// Assert
		command.CapturedOptions.Should().NotBeNull("because the resolved command must receive the mapped options");
		command.CapturedOptions!.OptionalProperties.Should().Be(optionalProperties,
			"because the create-page tool must thread the optional-properties argument into the command options");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("create-app-section maps args.caption-culture into ApplicationSectionCreateRequest.CaptionCulture.")]
	public async Task ApplicationSectionCreate_ShouldMapCaptionCulture_WhenCaptionCultureProvided() {
		// Arrange
		ApplicationSectionCreateRequest? capturedRequest = null;
		IApplicationSectionCreateService service = Substitute.For<IApplicationSectionCreateService>();
		service.CreateSection(Arg.Any<string>(), Arg.Do<ApplicationSectionCreateRequest>(request => capturedRequest = request), Arg.Any<int?>(), Arg.Any<int?>());
		ApplicationSectionCreateTool tool = new(service);
		ApplicationSectionCreateArgs args = new("dev", "UsrApp", "Orders", CaptionCulture: CaptionCultureValue);

		// Act
		await tool.ApplicationSectionCreate(args, null);

		// Assert
		capturedRequest.Should().NotBeNull("because the section create service must receive a request");
		capturedRequest!.CaptionCulture.Should().Be(CaptionCultureValue,
			"because the create-app-section tool must thread the caption-culture argument into the service request");
	}

	private sealed class CapturingModifyCommand : ModifyEntitySchemaColumnCommand {
		public ModifyEntitySchemaColumnOptions? CapturedOptions { get; private set; }

		public CapturingModifyCommand()
			: base(Substitute.For<IRemoteEntitySchemaColumnManager>(), ConsoleLogger.Instance) {
		}

		public override int Execute(ModifyEntitySchemaColumnOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class CapturingPageCreateCommand : PageCreateCommand {
		public PageCreateOptions? CapturedOptions { get; private set; }

		public CapturingPageCreateCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<ISchemaTemplateCatalog>(), ConsoleLogger.Instance,
				Substitute.For<ICaptionCultureResolver>()) {
		}

		public override bool TryCreatePage(PageCreateOptions options, out PageCreateResponse response) {
			CapturedOptions = options;
			response = new PageCreateResponse { Success = true };
			return true;
		}
	}
}
