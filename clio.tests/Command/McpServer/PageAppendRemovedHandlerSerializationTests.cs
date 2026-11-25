using System.Text.Json;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageAppendRemovedHandlerSerializationTests {

	[Test]
	[Description("ENG-107305: a removed-handler entry reaches the MCP payload with request, removedCount and the computed hadDuplicates flag.")]
	public void Serialize_WhenRemovedHandlerHadDuplicates_CarriesAllThreeFields() {
		// Arrange
		PageUpdateResponse response = new() {
			Success = true,
			AppendProjection = new PageAppendProjection {
				RemovedHandlers = [new PageAppendRemovedHandler("crt.SaveRecordRequest", 2)]
			}
		};
		JsonSerializerOptions options = BindingsModule.CreateMcpSerializerOptions();

		// Act
		string json = JsonSerializer.Serialize(response, options);

		// Assert
		json.Should().Contain("\"removedHandlers\":[{", because: "the handler channel is serialized inside appendProjection");
		json.Should().Contain("\"request\":\"crt.SaveRecordRequest\"", because: "the entry names the request whose handlers were removed");
		json.Should().Contain("\"removedCount\":2", because: "the entry carries how many current handlers were removed");
		json.Should().Contain("\"hadDuplicates\":true",
			because: "the contract tells agents to act on hadDuplicates, so the computed flag must reach the wire");
	}
}
