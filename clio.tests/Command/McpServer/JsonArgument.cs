using System.Text.Json;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Builds the <see cref="JsonElement"/> a JSON-document tool argument binds to, for tests that construct an
/// args record directly instead of going through the MCP binder.
/// </summary>
internal static class JsonArgument {

	/// <summary>
	/// The long-standing STRING form: a JSON string whose content is the document's JSON text - what a caller
	/// sends when it serializes the document itself.
	/// </summary>
	internal static JsonElement Text(string json) => JsonSerializer.SerializeToElement(json);
}
