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
}
