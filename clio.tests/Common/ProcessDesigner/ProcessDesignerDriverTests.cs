using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Common.ProcessDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ProcessDesigner;

/// <summary>
/// Unit tests for <see cref="ProcessDesignerDriver"/> with a mocked <see cref="ICdpSession"/> — the recipe's
/// live correctness is covered by Story 7's E2E; here we verify orchestration, the no-false-positive-save
/// guarantee, JSON-escaped parameters, and the embedded-recipe accessor.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ProcessDesignerDriverTests {
	private static readonly TimeSpan FastTimeout = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan FastDelay = TimeSpan.FromMilliseconds(5);

	private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private static ProcessAddElementRequest Request(string readObject = "Contact", string caption = "AI PoC Read Contact") =>
		new(new EnvironmentSettings { Uri = "https://dev.creatio.com", IsNetCore = false }, 9222, null, readObject, caption);

	private static ICdpSession SessionForPhase(string phase, string resultJson) {
		ICdpSession session = Substitute.For<ICdpSession>();
		Configure(session, phase, resultJson);
		return session;
	}

	private static void Configure(ICdpSession session, string phase, string resultJson) {
		session.EvaluateAsync(Arg.Is<string>(s => s.Contains($"\"{phase}\"")), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(J(resultJson)));
	}

	private static ICdpSession HappyPathSession() {
		ICdpSession session = Substitute.For<ICdpSession>();
		Configure(session, "prepare", """{"ready":true,"x":126,"y":314}""");
		Configure(session, "append", """{"ok":true}""");
		Configure(session, "fillObject", """{"ok":true,"optionX":600,"optionY":200}""");
		Configure(session, "setCaption", """{"ok":true}""");
		Configure(session, "checkValid", """{"invalid":false}""");
		Configure(session, "saveCoords", """{"ok":true,"x":52,"y":86}""");
		Configure(session, "saveResult", """{"ok":true,"uid":"dd3a473e-736a-4957-bb6d-7315f6404bd6"}""");
		return session;
	}

	private static ProcessDesignerDriver Driver(ICdpSession session) => new(session, FastTimeout, FastDelay);

	[Test]
	[Description("On the full happy path the driver reports success and returns the caption + read-back UId.")]
	public async Task AddReadDataElementAsync_ShouldSucceedAndReturnIdentity_WhenAllPhasesSucceed() {
		// Arrange
		ICdpSession session = HappyPathSession();
		ProcessDesignerDriver driver = Driver(session);

		// Act
		ProcessAddElementResult result = await driver.AddReadDataElementAsync(Request());

		// Assert
		result.Success.Should().BeTrue(because: "every phase reported success and a save signal was observed");
		result.Caption.Should().Be("AI PoC Read Contact", because: "the deterministic caption is the readback handle");
		result.UId.Should().Be("dd3a473e-736a-4957-bb6d-7315f6404bd6", because: "the saved UId is read back from the page");
		result.Error.Should().BeNull(because: "a successful run carries no error");
	}

	[Test]
	[Description("When the canvas never renders, the driver fails fast with a designer-never-rendered error.")]
	public async Task AddReadDataElementAsync_ShouldFail_WhenCanvasNeverRenders() {
		// Arrange — 'prepare' always reports not-ready.
		ICdpSession session = SessionForPhase("prepare", """{"ready":false}""");
		ProcessDesignerDriver driver = Driver(session);

		// Act
		ProcessAddElementResult result = await driver.AddReadDataElementAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "the designer canvas never rendered");
		result.Error.Should().Contain("did not render", because: "the failure must name the never-rendered canvas");
	}

	[Test]
	[Description("An invalid connection (.djs-validate-outline) aborts before SAVE and never reports a save.")]
	public async Task AddReadDataElementAsync_ShouldAbortWithoutSaving_WhenConnectionIsInvalid() {
		// Arrange
		ICdpSession session = Substitute.For<ICdpSession>();
		Configure(session, "prepare", """{"ready":true,"x":126,"y":314}""");
		Configure(session, "append", """{"ok":true}""");
		Configure(session, "fillObject", """{"ok":true,"optionX":600,"optionY":200}""");
		Configure(session, "setCaption", """{"ok":true}""");
		Configure(session, "checkValid", """{"invalid":true}""");
		ProcessDesignerDriver driver = Driver(session);

		// Act
		ProcessAddElementResult result = await driver.AddReadDataElementAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "an invalid connection must abort the run");
		result.Error.Should().Contain("invalid", because: "the failure must name the invalid connection");
		await session.DidNotReceive().EvaluateAsync(Arg.Is<string>(s => s.Contains("\"saveResult\"")),
			Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("When no save signal is observed, the driver reports failure (never a false-positive save).")]
	public async Task AddReadDataElementAsync_ShouldFail_WhenNoSaveSignal() {
		// Arrange
		ICdpSession session = HappyPathSession();
		Configure(session, "saveResult", """{"ok":false,"error":"no signal"}""");
		ProcessDesignerDriver driver = Driver(session);

		// Act
		ProcessAddElementResult result = await driver.AddReadDataElementAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "a missing save signal must never be reported as success");
		result.Error.Should().Contain("no signal", because: "the failure surfaces the recipe's reported reason");
	}

	[Test]
	[Description("When the append fails, the driver reports failure with the recipe's reason.")]
	public async Task AddReadDataElementAsync_ShouldFail_WhenAppendFails() {
		// Arrange
		ICdpSession session = Substitute.For<ICdpSession>();
		Configure(session, "prepare", """{"ready":true,"x":126,"y":314}""");
		Configure(session, "append", """{"ok":false,"error":"Read data element was not appended"}""");
		ProcessDesignerDriver driver = Driver(session);

		// Act
		ProcessAddElementResult result = await driver.AddReadDataElementAsync(Request());

		// Assert
		result.Success.Should().BeFalse(because: "a failed append cannot proceed to save");
		result.Error.Should().Contain("was not appended", because: "the failure surfaces the append error");
	}

	[Test]
	[Description("The source object is passed to the recipe as a JSON-escaped value, not raw string concatenation.")]
	public async Task AddReadDataElementAsync_ShouldPassJsonEscapedObject_WhenObjectContainsQuotes() {
		// Arrange — an object name with a double quote would break a naive concatenation.
		ICdpSession session = HappyPathSession();
		string trickyObject = "a\"b";
		string expectedJson = JsonSerializer.Serialize(new { @object = trickyObject });
		ProcessDesignerDriver driver = Driver(session);

		// Act
		await driver.AddReadDataElementAsync(Request(readObject: trickyObject));

		// Assert
		await session.Received().EvaluateAsync(
			Arg.Is<string>(s => s.Contains("\"fillObject\"") && s.Contains(expectedJson)),
			Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("ProcessDesignerRecipes.Get reads the embedded recipe and caches it (same instance on repeat).")]
	public void ProcessDesignerRecipes_ShouldReadAndCacheEmbeddedRecipe_WhenRequested() {
		// Act
		string first = ProcessDesignerRecipes.Get("read-data-element");
		string second = ProcessDesignerRecipes.Get("read-data-element");

		// Assert
		first.Should().NotBeNullOrWhiteSpace(because: "the embedded recipe must be readable from the assembly manifest");
		first.Should().Contain("add.serviceTask", because: "the recipe drives the context-pad System-actions append");
		second.Should().BeSameAs(first, because: "the recipe is cached after the first read");
	}

	[Test]
	[Description("BuildDesignerUrl uses the 0/ alias for .NET Framework and omits it for .NET Core.")]
	public void BuildDesignerUrl_ShouldRespectNetCoreAlias_WhenBuildingUrl() {
		// Arrange
		EnvironmentSettings netFramework = new() { Uri = "http://host:1026/", IsNetCore = false };
		EnvironmentSettings netCore = new() { Uri = "https://dev.creatio.com", IsNetCore = true };

		// Assert
		ProcessDesignerDriver.BuildDesignerUrl(netFramework, null)
			.Should().Be("http://host:1026/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/",
				because: ".NET Framework Creatio uses the 0/ WebApp alias");
		ProcessDesignerDriver.BuildDesignerUrl(netCore, "704a1424")
			.Should().Be("https://dev.creatio.com/Nui/ViewModule.aspx?vm=SchemaDesigner#process/704a1424",
				because: ".NET Core Creatio omits the 0/ alias and opens the given process id");
	}
}
