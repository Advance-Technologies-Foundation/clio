using System;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;

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
	/// info line, measured exactly as <see cref="Of{T}"/> measures any response: the serialized
	/// <see cref="CommandExecutionResult"/>, the result text the agent reads. The reply travels as a string inside
	/// it, so each quote and backslash of the reply costs two bytes there - the reason a graph's character count
	/// understates the reply (ENG-103023).
	/// </summary>
	/// <param name="reply">The reply text the command writes.</param>
	/// <returns>The UTF-8 byte count of the serialized result.</returns>
	internal static long OfCommandReply(string reply) =>
		// A correlation id of the real length, so the measured envelope is the one that ships.
		Of(new CommandExecutionResult(0, [new InfoMessage(reply)], CorrelationId: Guid.Empty.ToString()));
}
