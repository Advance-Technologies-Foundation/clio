using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpGuidanceForcingTests {
	[Test]
	[Description("Confirms the response types carry a nullable note property so the deterministic compile-not-required signal can be emitted.")]
	public void ResponseTypes_ShouldExposeNoteProperty_ForDeterministicSignal() {
		// Arrange

		// Act
		PropertyInfo pageNote = typeof(PageCreateResponse).GetProperty("Note");
		PropertyInfo commandNote = typeof(CommandExecutionResult).GetProperty("Note");

		// Assert
		pageNote.Should().NotBeNull(
			because: "create-page emits a deterministic compile-not-required note on success");
		commandNote.Should().NotBeNull(
			because: "command results need the same deterministic note channel");
	}

	[Test]
	[Description("create-page emits the deterministic compile-not-required note after a successful page save.")]
	public void CreatePage_ShouldEmitCompileNotRequiredNote_WhenCreateSucceeds() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakePageCreateCommand command = new(success: true);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>()).Returns(command);
		PageCreateTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act
		PageCreateResponse response = tool.CreatePage(BuildPageCreateArgs());

		// Assert
		response.Success.Should().BeTrue(because: "the fake command reports a successful page create");
		response.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "the deterministic signal prevents an unnecessary compile after a successful page save");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("create-page suppresses the deterministic note when the page save fails.")]
	public void CreatePage_ShouldNotEmitCompileNotRequiredNote_WhenCreateFails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakePageCreateCommand command = new(success: false);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>()).Returns(command);
		PageCreateTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act
		PageCreateResponse response = tool.CreatePage(BuildPageCreateArgs());

		// Assert
		response.Success.Should().BeFalse(because: "the fake command reports an unsuccessful page create");
		response.Note.Should().BeNull(
			because: "a success-only signal must not ride an unsuccessful save");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("CommandExecutionResult omits a null note and emits a populated note on its JSON wire shape.")]
	public void CommandExecutionResultNote_ShouldBeOmittedWhenNullAndPresentWhenSet_OnSystemTextJson() {
		// Arrange
		CommandExecutionResult withoutNote = new(0, []);
		CommandExecutionResult withNote = withoutNote with { Note = CommandExecutionResult.CompileNotRequiredNote };

		// Act
		string jsonWithoutNote = JsonSerializer.Serialize(withoutNote);
		string jsonWithNote = JsonSerializer.Serialize(withNote);

		// Assert
		jsonWithoutNote.Should().NotContain("\"note\"",
			because: "a null optional signal must be absent from the wire output");
		jsonWithNote.Should().Contain($"\"note\":\"{CommandExecutionResult.CompileNotRequiredNote}\"",
			because: "a populated signal must reach the agent under its stable wire key");
	}

	[Test]
	[Description("PageCreateResponse applies identical optional-note behavior under both supported JSON serializers.")]
	public void PageCreateResponseNote_ShouldBeOmittedWhenNullAndPresentWhenSet_OnBothSerializers() {
		// Arrange
		PageCreateResponse withoutNote = new() { Success = true, SchemaName = "UsrTestPage" };
		PageCreateResponse withNote = new() {
			Success = true,
			SchemaName = "UsrTestPage",
			Note = CommandExecutionResult.CompileNotRequiredNote
		};

		// Act
		string stjWithoutNote = JsonSerializer.Serialize(withoutNote);
		string stjWithNote = JsonSerializer.Serialize(withNote);
		string newtonsoftWithoutNote = Newtonsoft.Json.JsonConvert.SerializeObject(withoutNote);
		string newtonsoftWithNote = Newtonsoft.Json.JsonConvert.SerializeObject(withNote);

		// Assert
		stjWithoutNote.Should().NotContain("\"note\"",
			because: "System.Text.Json must omit a null optional signal");
		stjWithNote.Should().Contain($"\"note\":\"{CommandExecutionResult.CompileNotRequiredNote}\"",
			because: "System.Text.Json must emit a populated signal");
		newtonsoftWithoutNote.Should().NotContain("\"note\"",
			because: "Newtonsoft.Json must omit a null optional signal");
		newtonsoftWithNote.Should().Contain($"\"note\":\"{CommandExecutionResult.CompileNotRequiredNote}\"",
			because: "Newtonsoft.Json must emit a populated signal");
	}

	private static PageCreateArgs BuildPageCreateArgs() =>
		new("UsrTestPage", "FormPage", "UsrPackage",
			Caption: null, Description: null, EntitySchemaName: null,
			EnvironmentName: "dev", Uri: null, Login: null, Password: null);

	private sealed class FakePageCreateCommand : PageCreateCommand {
		private readonly bool _success;

		public FakePageCreateCommand(bool success)
			: base(
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<ISchemaTemplateCatalog>(),
				Substitute.For<ILogger>(),
				Substitute.For<Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver>()) {
			_success = success;
		}

		public override bool TryCreatePage(PageCreateOptions options, out PageCreateResponse response) {
			response = _success
				? new PageCreateResponse { Success = true, SchemaName = options.SchemaName }
				: new PageCreateResponse { Success = false, Error = "create failed" };
			return _success;
		}
	}
}
