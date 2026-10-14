using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ApplicationToolResultMapperTests {
	private const string ResetWarning = "navigation cache reset failed: boom";
	private const string BrowserSessionNote = "run GetData(true) in the open tab, then reload it";

	private static readonly ApplicationSectionInfoResult Section = new(
		"section-id", "UsrOrders", "Orders", null, "UsrOrder", "pkg-uid", "section-schema-uid", "icon-id", "#111111", null);

	[Test]
	[Description("ENG-101680: create-app carries the service warnings, such as a failed navigation cache reset, into the MCP envelope.")]
	public void Map_Should_Copy_Warnings_From_ApplicationInfoResult() {
		// Arrange
		ApplicationInfoResult result = new("pkg-uid", "UsrOrdersApp", [], Warnings: [ResetWarning]);

		// Act
		ApplicationContextResponse response = ApplicationToolResultMapper.Map(result);

		// Assert
		response.Warnings.Should().Equal([ResetWarning],
			because: "the agent must learn that the menu cache of its session was not cleared");
	}

	[Test]
	[Description("ENG-101680: create-app and get-app-info omit the warnings field when the service reported none.")]
	public void Map_Should_Omit_Warnings_When_ApplicationInfoResult_Has_None() {
		// Arrange
		ApplicationInfoResult result = new("pkg-uid", "UsrOrdersApp", [], Warnings: []);

		// Act
		ApplicationContextResponse response = ApplicationToolResultMapper.Map(result);

		// Assert
		response.Warnings.Should().BeNull(
			because: "an empty list carries no finding and is left out of the envelope");
	}

	[Test]
	[Description("ENG-101680: create-app-section carries the service warnings, such as a failed navigation cache reset, into the MCP envelope.")]
	public void Map_Should_Copy_Warnings_From_ApplicationSectionCreateResult() {
		// Arrange
		ApplicationSectionCreateResult result = new(
			"pkg-uid", "UsrOrdersApp", "app-id", "Orders App", "UsrOrdersApp", "1.0.0", Section, null, [],
			Warnings: [ResetWarning]);

		// Act
		ApplicationSectionContextResponse response = ApplicationToolResultMapper.Map(result);

		// Assert
		response.Warnings.Should().Equal([ResetWarning],
			because: "the agent must learn that the menu cache of its session was not cleared");
	}

	[Test]
	[Description("create-app carries the browser-session note into the MCP envelope as next-step.")]
	public void Map_Should_Copy_NextStep_From_ApplicationInfoResult() {
		// Arrange
		ApplicationInfoResult result = new("pkg-uid", "UsrOrdersApp", [], NextStep: BrowserSessionNote);

		// Act
		ApplicationContextResponse response = ApplicationToolResultMapper.Map(result);

		// Assert
		response.NextStep.Should().Be(BrowserSessionNote,
			because: "the agent must learn how to refresh an open browser tab that still shows the old menu");
	}

	[Test]
	[Description("create-app-section carries the browser-session note into the MCP envelope as next-step.")]
	public void Map_Should_Copy_NextStep_From_ApplicationSectionCreateResult() {
		// Arrange
		ApplicationSectionCreateResult result = new(
			"pkg-uid", "UsrOrdersApp", "app-id", "Orders App", "UsrOrdersApp", "1.0.0", Section, null, [],
			NextStep: BrowserSessionNote);

		// Act
		ApplicationSectionContextResponse response = ApplicationToolResultMapper.Map(result);

		// Assert
		response.NextStep.Should().Be(BrowserSessionNote,
			because: "the agent must learn how to refresh an open browser tab that still shows the old menu");
	}

	[Test]
	[Description("update-app-section carries the browser-session note into the MCP envelope as next-step.")]
	public void Map_Should_Copy_NextStep_From_ApplicationSectionUpdateResult() {
		// Arrange
		ApplicationSectionUpdateResult result = new(
			"pkg-uid", "UsrOrdersApp", "app-id", "Orders App", "UsrOrdersApp", "1.0.0", Section, Section,
			NextStep: BrowserSessionNote);

		// Act
		ApplicationSectionUpdateContextResponse response = ApplicationToolResultMapper.Map(result);

		// Assert
		response.NextStep.Should().Be(BrowserSessionNote,
			because: "the agent must learn how to refresh an open browser tab that still shows the old caption");
	}

	[Test]
	[Description("A failed create-app envelope carries no next-step, because nothing changed that a browser tab could miss.")]
	public void CreateContextErrorResponse_Should_Omit_NextStep() {
		// Act
		ApplicationContextResponse response = ApplicationToolHelper.CreateContextErrorResponse("boom");

		// Assert
		response.NextStep.Should().BeNull(because: "the browser-session note belongs to the success path only");
	}
}
