using System.Text.Json;

namespace Clio.Command.McpServer;

/// <summary>
/// Measures a tool response the way it reaches the agent: serialized with the MCP result options, in UTF-8
/// bytes.
/// </summary>
/// <remarks>
/// The SDK serializes a tool's return value with the same options (<see cref="BindingsModule.CreateMcpSerializerOptions"/>,
/// passed to <c>WithTools</c>), so the size measured here is the size of the result text the agent reads. It
/// is NOT the JSON-RPC frame around it, which the SDK writes with its own encoder and the agent CLI strips.
/// </remarks>
internal static class McpResultSize
{
	private static readonly JsonSerializerOptions SerializerOptions = BindingsModule.CreateMcpSerializerOptions();

	/// <summary>
	/// Returns the size of <paramref name="response"/> as the agent receives it.
	/// </summary>
	/// <typeparam name="T">The declared response type the tool method returns.</typeparam>
	/// <param name="response">The response the tool is about to return.</param>
	/// <returns>The UTF-8 byte count of the serialized response.</returns>
	internal static long Of<T>(T response) =>
		JsonSerializer.SerializeToUtf8Bytes(response, SerializerOptions).LongLength;
}
