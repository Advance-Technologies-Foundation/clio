using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class ListEntityClientSchemasToolTests {

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve resolves the command for the requested environment and maps entity-name into options.")]
	public void Resolve_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved fake command returns a successful response");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the environment-scoped command must execute");
		resolvedCommand.CapturedOptions.EntityName.Should().Be("Contract", because: "entity-name is the required lookup key");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment-name drives command resolution");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the startup command must not run for environment-scoped calls");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns a redacted error response when environment-scoped command resolution fails.")]
	public void Resolve_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "resolver failures must be returned as typed tool failures");
		response.Error.Should().Contain("boom", because: "the caller needs the resolver failure reason");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns a typed failure when the MCP request explicitly passes null args.")]
	public void Resolve_Should_Return_Error_When_Args_Are_Null() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(null);

		// Assert
		response.Success.Should().BeFalse(because: "args:null is invalid but should not escape as an NRE");
		response.Error.Should().Contain("args", because: "the failure should name the missing argument object");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller.")]
	public void Resolve_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new() {
			ResponseToReturn = new ListEntityClientSchemasResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve redacts a sensitive URI/host carried in the response warnings, not just the error, so a warning cannot leak backend detail into the MCP transcript — parity with GetClassicPageSourcesTool.")]
	public void Resolve_Should_Redact_Sensitive_Text_In_Warnings() {
		// Arrange — a successful resolve whose warning carries a raw DataService failure host
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new() {
			ResponseToReturn = new ListEntityClientSchemasResponse {
				Success = true,
				Warnings = [
					"Section lookup failed (POST https://secret-host.example.com/0/DataService failed); results may be partial."
				]
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		response.Warnings.Should().ContainSingle(because: "the single warning is carried through to the caller")
			.Which.Should().NotContain("secret-host.example.com",
				because: "warnings are a second error channel and must be redacted like Error is");
		response.Warnings[0].Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the same stable placeholder used on the error path");
		response.Warnings[0].Should().Contain("results may be partial",
			because: "redaction scrubs the host, not the actionable part of the warning");
	}

	[Test]
	[Category("Unit")]
	[Description("ClassifyKind recognizes known Freedom templates, known Classic templates, and leaves unknown templates undecided.")]
	[TestCase("PageWithTabsFreedomTemplate", "freedom")]
	[TestCase("PageWithAreaFreedomTemplate", "freedom")]
	[TestCase("PageWithRightAreaAndTabsFreedomTemplate", "freedom")]
	[TestCase("PageWithTopAreaAndTabsFreedomTemplate", "freedom")]
	[TestCase("PageWithTabsAndProgressBarTemplate", "freedom")]
	[TestCase("BlankPageTemplate", "freedom")]
	[TestCase("BaseHomePage", "freedom")]
	[TestCase("BaseDashboardTemplate", "freedom")]
	[TestCase("BaseSidebarTemplate", "freedom")]
	[TestCase("BaseMiniPageTemplate", "freedom")]
	[TestCase("FormPageTemplate", "freedom")]
	[TestCase("ListPageV3Template", "freedom")]
	[TestCase("ListPageV2Template", "freedom")]
	[TestCase("BaseModulePageV2", "classic")]
	[TestCase("BasePageV2", "classic")]
	[TestCase("NotActuallyFreedomClassicTemplate", "unknown")]
	[TestCase("CustomTemplate", "unknown")]
	[TestCase(null, "unknown")]
	[TestCase("", "unknown")]
	public void ClassifyKind_Should_Split_Classic_And_Freedom(string template, string expected) {
		// Arrange / Act
		string actual = ListEntityClientSchemasCommand.ClassifyKind(template);

		// Assert
		actual.Should().Be(expected, because: "migration routing must not guess classic/freedom for unknown templates");
	}

	[Test]
	[Category("Unit")]
	[Description("The default list-entity-client-schemas response serializes to the pinned wire JSON; the file-mode twin must not change it.")]
	public void Resolve_Response_Should_Match_Pinned_Wire_Json() {
		// Arrange
		FakeListEntityClientSchemasCommand defaultCommand = new();
		FakeListEntityClientSchemasCommand resolvedCommand = new() { ResponseToReturn = CreateSampleResponse() };
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListEntityClientSchemasResponse response = tool.Resolve(new ListEntityClientSchemasArgs("Contract") {
			EnvironmentName = "dev" });

		// Assert
		McpResponseBaseline.Serialize(response).Should().Be(PinnedWireJson,
			because: "the inline response is the default and stays byte-for-byte unchanged");
	}

	private const string PinnedWireJson = """{"success":true,"entity":"Contract","entityUId":"11111111-1111-1111-1111-111111111111","sections":[{"caption":"Contracts","code":"Contract","sectionSchema":"ContractSectionV2","cardSchema":"ContractPageV2","cardSchemaUId":"22222222-2222-2222-2222-222222222222","template":"BasePageV2","kind":"classic","isTyped":true}],"editPages":[{"typeColumnValue":"33333333-3333-3333-3333-333333333333","typeColumnDisplayValue":"Service","cardSchema":"ContractPageV2","cardSchemaUId":"22222222-2222-2222-2222-222222222222","template":"BasePageV2","kind":"classic","miniPageSchema":"ContractMiniPage","miniPageSchemaUId":"44444444-4444-4444-4444-444444444444","miniPageTemplate":"BaseMiniPageTemplate","miniPageKind":"freedom","miniPageModes":"add"},{"cardSchema":"Contracts_FormPage","template":"PageWithTabsFreedomTemplate","kind":"freedom"},{"cardSchema":"UsrContractPage","template":"CustomTemplate","kind":"unknown"}],"warnings":["rowCount cap reached"],"note":"One level only."}""";

	internal static ListEntityClientSchemasResponse CreateSampleResponse() => new() {
		Success = true,
		Entity = "Contract",
		EntityUId = "11111111-1111-1111-1111-111111111111",
		Sections = [
			new MigrationSectionInfo {
				Caption = "Contracts", Code = "Contract", SectionSchema = "ContractSectionV2", CardSchema = "ContractPageV2",
				CardSchemaUId = "22222222-2222-2222-2222-222222222222", Template = "BasePageV2", Kind = "classic", IsTyped = true
			}
		],
		EditPages = [
			new MigrationEditPageInfo {
				TypeColumnValue = "33333333-3333-3333-3333-333333333333", TypeColumnDisplayValue = "Service", CardSchema = "ContractPageV2",
				CardSchemaUId = "22222222-2222-2222-2222-222222222222", Template = "BasePageV2", Kind = "classic",
				MiniPageSchema = "ContractMiniPage", MiniPageSchemaUId = "44444444-4444-4444-4444-444444444444",
				MiniPageTemplate = "BaseMiniPageTemplate", MiniPageKind = "freedom", MiniPageModes = "add"
			},
			new MigrationEditPageInfo { CardSchema = "Contracts_FormPage", Template = "PageWithTabsFreedomTemplate", Kind = "freedom" },
			new MigrationEditPageInfo { CardSchema = "UsrContractPage", Template = "CustomTemplate", Kind = "unknown" }
		],
		Note = "One level only.",
		Warnings = ["rowCount cap reached"]
	};

	private static (ListEntityClientSchemasToFileTool tool, FakeListEntityClientSchemasCommand command, MockFileSystem fileSystem)
		BuildToFileTool(ListEntityClientSchemasResponse responseToReturn, MockFileSystem? fileSystem = null) {
		MockFileSystem fs = fileSystem ?? new MockFileSystem();
		FakeListEntityClientSchemasCommand resolvedCommand = new() { ResponseToReturn = responseToReturn };
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListEntityClientSchemasCommand>(Arg.Any<ListEntityClientSchemasOptions>())
			.Returns(resolvedCommand);
		ListEntityClientSchemasTool listTool = new(new FakeListEntityClientSchemasCommand(), ConsoleLogger.Instance, commandResolver);
		ListEntityClientSchemasToFileTool tool = new(listTool, new McpOutputFileWriter(fs, new MockConfinedFileAccess(fs)));
		return (tool, resolvedCommand, fs);
	}

	private static string TempPath(MockFileSystem fileSystem) =>
		fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"entity-schemas-{System.Guid.NewGuid():N}.json");

	[Test]
	[Category("Unit")]
	[Description("The file twin writes exactly the inline list-entity-client-schemas response and returns the path and the classic/freedom counts instead of the list.")]
	public void ResolveToFile_Should_Write_Inline_Response_And_Return_Counts() {
		// Arrange
		(ListEntityClientSchemasToFileTool tool, _, MockFileSystem fileSystem) = BuildToFileTool(CreateSampleResponse());
		string outputFile = TempPath(fileSystem);

		// Act
		ListEntityClientSchemasToFileResponse response = tool.ResolveToFile(
			new ListEntityClientSchemasToFileArgs("Contract", outputFile) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the lookup succeeded and the file was written");
		fileSystem.File.ReadAllText(outputFile).Should().Be(PinnedWireJson,
			because: "the file holds the same JSON the inline tool returns, so nothing is lost");
		response.OutputFile.Should().Be(fileSystem.Path.GetFullPath(outputFile), because: "the caller needs the resolved path");
		response.Sections.Should().Be(new PageKindCounts(1, 1, 0, 0), because: "the single section is classic");
		response.EditPages.Should().Be(new PageKindCounts(3, 1, 1, 1),
			because: "one edit page of each kind; an unknown kind is counted, not dropped");
		response.Warnings.Should().Equal(["rowCount cap reached"], because: "warnings stay inline so a partial result is visible");
		response.Note.Should().Be("One level only.", because: "the note explains how to read the result and stays inline too");
		McpResponseBaseline.Serialize(response).Should().NotContain("cardSchema",
			because: "the page list itself is not returned inline");
	}

	[Test]
	[Category("Unit")]
	[Description("A failed lookup is returned without writing a file.")]
	public void ResolveToFile_Should_Not_Write_When_Lookup_Fails() {
		// Arrange
		(ListEntityClientSchemasToFileTool tool, _, MockFileSystem fileSystem) = BuildToFileTool(
			new ListEntityClientSchemasResponse { Success = false, Error = "entity not found" });
		string outputFile = TempPath(fileSystem);

		// Act
		ListEntityClientSchemasToFileResponse response = tool.ResolveToFile(
			new ListEntityClientSchemasToFileArgs("Nope", outputFile) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the lookup failed");
		response.Error.Should().Be("entity not found", because: "the lookup failure reaches the caller");
		fileSystem.File.Exists(outputFile).Should().BeFalse(because: "a failed lookup leaves no file");
	}

	[Test]
	[Category("Unit")]
	[Description("An existing output-file is refused before the lookup runs.")]
	public void ResolveToFile_Should_Reject_Existing_Output_File_Before_Lookup() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = TempPath(fileSystem);
		fileSystem.AddFile(outputFile, new MockFileData("{}"));
		(ListEntityClientSchemasToFileTool tool, FakeListEntityClientSchemasCommand command, _) =
			BuildToFileTool(CreateSampleResponse(), fileSystem);

		// Act
		ListEntityClientSchemasToFileResponse response = tool.ResolveToFile(
			new ListEntityClientSchemasToFileArgs("Contract", outputFile) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "an existing file is never overwritten");
		response.Error.Should().Contain("already exists", because: "the caller has to choose another path");
		command.CapturedOptions.Should().BeNull(because: "a refused path must not cost the lookup");
		fileSystem.File.ReadAllText(outputFile).Should().Be("{}", because: "the existing file is left untouched");
	}

	[Test]
	[Category("Unit")]
	[Description("An output-file outside the workspace and the OS temp directory is refused before the lookup runs.")]
	public void ResolveToFile_Should_Reject_Output_File_Outside_Allowed_Locations() {
		// Arrange
		(ListEntityClientSchemasToFileTool tool, FakeListEntityClientSchemasCommand command, MockFileSystem fileSystem) =
			BuildToFileTool(CreateSampleResponse());
		string outsidePath = System.IO.Path.Combine(
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
			$"clio-entity-schemas-probe-{System.Guid.NewGuid():N}.json");

		// Act
		ListEntityClientSchemasToFileResponse response = tool.ResolveToFile(
			new ListEntityClientSchemasToFileArgs("Contract", outsidePath) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "a path outside the allowed locations is never written");
		response.Error.Should().Contain("allowed locations", because: "the caller is told confinement refused it");
		response.Error.Should().Contain("list-entity-client-schemas", because: "the caller is sent to the inline tool");
		command.CapturedOptions.Should().BeNull(because: "the path is checked before the lookup");
		fileSystem.File.Exists(outsidePath).Should().BeFalse(because: "nothing is created on the file system the tool writes to");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable tool name and the write-capable annotations a local file write needs.")]
	public void ResolveToFile_Should_Advertise_Stable_Name_And_Write_Capable_Annotations() {
		// Arrange

		// Act
		ModelContextProtocol.Server.McpServerToolAttribute attribute = (ModelContextProtocol.Server.McpServerToolAttribute)
			typeof(ListEntityClientSchemasToFileTool)
				.GetMethod(nameof(ListEntityClientSchemasToFileTool.ResolveToFile))!
				.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)[0];

		// Assert
		attribute.Name.Should().Be("list-entity-client-schemas-to-file", because: "the name is part of the MCP contract");
		attribute.ReadOnly.Should().BeFalse(because: "the tool creates a local file");
		attribute.Idempotent.Should().BeFalse(because: "a second call to the same path is refused");
		attribute.Destructive.Should().BeFalse(because: "the tool only adds a local file");
	}

	private sealed class FakeListEntityClientSchemasCommand : ListEntityClientSchemasCommand {
		public ListEntityClientSchemasOptions CapturedOptions { get; private set; }
		public ListEntityClientSchemasResponse ResponseToReturn { get; init; }

		public FakeListEntityClientSchemasCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance,
				Substitute.For<Clio.Common.EntitySchema.IRuntimeEntitySchemaReader>(),
				Substitute.For<Clio.Command.EntitySchemaDesigner.ILookupDefaultDisplayValueResolver>()) {
		}

		public override bool TryResolve(ListEntityClientSchemasOptions options, out ListEntityClientSchemasResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn ?? new ListEntityClientSchemasResponse { Success = true, Entity = options.EntityName };
			return response.Success;
		}
	}
}
