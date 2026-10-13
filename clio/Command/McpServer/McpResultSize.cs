using System;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using ModelContextProtocol.Protocol;

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

	/// <summary>
	/// Returns the size of the tool reply a command produces when it logs <paramref name="reply"/> as its one
	/// info line, measured as the serialized MCP <c>CallToolResult</c>: the command result is the text of a content
	/// block, so it is JSON-encoded a SECOND time there, and every quote and backslash of the reply is paid three
	/// times. This is the number a client's spill line is measured against (ENG-103023 QA: a 14,552-character
	/// process graph was 20,074 bytes here).
	/// </summary>
	/// <param name="reply">The reply text the command writes.</param>
	/// <returns>The UTF-8 byte count of the serialized tool result.</returns>
	internal static long OfCommandReply(string reply) {
		// A correlation id of the real length, so the measured envelope is the one that ships.
		CommandExecutionResult result = new(0, [new InfoMessage(reply)], CorrelationId: Guid.Empty.ToString());
		string text = JsonSerializer.Serialize(result, SerializerOptions);
		CallToolResult toolResult = new() { Content = [new TextContentBlock { Text = text }] };
		return JsonSerializer.SerializeToUtf8Bytes(toolResult, SerializerOptions).LongLength;
	}
}
