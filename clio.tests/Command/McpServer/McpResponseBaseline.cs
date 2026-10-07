using System.Text.Json;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Serializes a tool response exactly as the MCP host puts it on the wire, so a test can pin the default
/// response of a tool byte for byte against a literal.
/// </summary>
internal static class McpResponseBaseline {

	private static readonly JsonSerializerOptions WireOptions = Clio.BindingsModule.CreateMcpSerializerOptions();

	/// <summary>Returns the wire JSON of <paramref name="value"/>.</summary>
	internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WireOptions);
}
