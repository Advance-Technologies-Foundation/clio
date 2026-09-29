using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Turns a <c>get-component-info</c> / <c>get-request-info</c> response into its <c>*-to-file</c> form: the
/// documentation markdown goes to a file and the response carries the path and the section headings instead.
/// </summary>
/// <remarks>
/// The projection works on the wire JSON of the inline response rather than on a copy of the response type, so
/// every other field is exactly what the inline tool returns, including fields added to that type later.
/// </remarks>
internal static class DocumentationFileProjection {

	internal const string DocumentationFieldName = "documentation";
	internal const string DocumentationFileFieldName = "documentationFile";
	internal const string DocumentationSectionsFieldName = "documentationSections";

	private static readonly JsonSerializerOptions WireOptions = BindingsModule.CreateMcpSerializerOptions();

	/// <summary>
	/// Writes <paramref name="documentation"/> to <paramref name="outputPath"/> and returns the inline response
	/// with <c>documentation</c> replaced by <c>documentationFile</c> and <c>documentationSections</c>. When the
	/// response has no documentation, nothing is written and the inline response is returned unchanged.
	/// </summary>
	/// <param name="response">The inline tool response.</param>
	/// <param name="documentation">The documentation markdown the response carries, or <see langword="null"/>.</param>
	/// <param name="outputPath">Path previously returned by <see cref="IMcpOutputFileWriter.TryResolve"/>.</param>
	/// <param name="outputFileWriter">Creates the file.</param>
	/// <returns>The response to return to the caller.</returns>
	internal static JsonObject Project<TResponse>(
		TResponse response,
		string? documentation,
		string outputPath,
		IMcpOutputFileWriter outputFileWriter) {
		JsonObject json = JsonSerializer.SerializeToNode(response, WireOptions)!.AsObject();
		if (string.IsNullOrEmpty(documentation)) {
			return json;
		}
		// The response is built BEFORE the write, so nothing that can fail runs after the file exists: a failure
		// then would leave a file behind that every retry to the same path refuses.
		JsonArray sections = [];
		foreach (string heading in ExtractHeadings(documentation)) {
			sections.Add(heading);
		}
		json.Remove(DocumentationFieldName);
		json[DocumentationFileFieldName] = outputPath;
		json[DocumentationSectionsFieldName] = sections;
		return outputFileWriter.TryWriteNew(outputPath, Encoding.UTF8.GetBytes(documentation), out string writeError)
			? json
			: Failure(writeError);
	}

	/// <summary>Builds the failure response of a <c>*-to-file</c> info tool; nothing was written.</summary>
	/// <param name="error">Caller-facing error.</param>
	internal static JsonObject Failure(string error) => new() {
		["success"] = false,
		["error"] = error
	};

	/// <summary>
	/// Returns the ATX headings (<c># Title</c> to <c>###### Title</c>) of <paramref name="markdown"/> in document
	/// order, with their <c>#</c> prefix so the level stays visible. Lines inside fenced code blocks are skipped,
	/// because a <c>#</c> there is a comment, not a heading.
	/// </summary>
	/// <param name="markdown">The documentation markdown.</param>
	internal static IReadOnlyList<string> ExtractHeadings(string markdown) {
		List<string> headings = [];
		string? openFence = null;
		foreach (string rawLine in markdown.Split('\n')) {
			string line = rawLine.TrimEnd('\r');
			string trimmed = line.TrimStart();
			if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) {
				string fence = trimmed[..3];
				if (openFence is null) {
					openFence = fence;
				} else if (openFence == fence) {
					openFence = null;
				}
				continue;
			}
			if (openFence is not null || line.Length - trimmed.Length > 3) {
				continue;
			}
			int level = 0;
			while (level < trimmed.Length && trimmed[level] == '#') {
				level++;
			}
			if (level is >= 1 and <= 6 && level < trimmed.Length && trimmed[level] == ' ') {
				headings.Add(WithoutClosingSequence(trimmed.TrimEnd(), level));
			}
		}
		return headings;
	}

	// An optional closing run of '#' counts only after a space, so "## Usage ##" loses it and "## C#" keeps it.
	private static string WithoutClosingSequence(string heading, int level) {
		int end = heading.Length;
		while (end > level && heading[end - 1] == '#') {
			end--;
		}
		return end < heading.Length && heading[end - 1] == ' ' ? heading[..end].TrimEnd() : heading;
	}
}
