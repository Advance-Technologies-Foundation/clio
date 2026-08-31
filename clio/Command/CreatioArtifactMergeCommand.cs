using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Clio.Common;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>
/// Options for the <c>merge-creatio-artifact</c> command.
/// </summary>
[Verb("merge-creatio-artifact", HelpText = "Semantically merge three versions of a supported Creatio artifact")]
public sealed class CreatioArtifactMergeOptions {
	/// <summary>Repository-relative artifact path used to classify the artifact.</summary>
	[Option("artifact-path", Required = true,
		HelpText = "Repository-relative artifact path used to classify the artifact.")]
	public string ArtifactPath { get; set; }

	/// <summary>File containing the common-base content (Git stage 1).</summary>
	[Option("base-file", Required = true, HelpText = "File containing the common-base content (Git stage 1).")]
	public string BaseFile { get; set; }

	/// <summary>File containing the current-branch content (Git stage 2).</summary>
	[Option("ours-file", Required = true, HelpText = "File containing the current-branch content (Git stage 2).")]
	public string OursFile { get; set; }

	/// <summary>File containing the incoming-branch content (Git stage 3).</summary>
	[Option("theirs-file", Required = true, HelpText = "File containing the incoming-branch content (Git stage 3).")]
	public string TheirsFile { get; set; }

	/// <summary>Optional file containing the resolved sibling descriptor.</summary>
	[Option("descriptor-file", Required = false,
		HelpText = "Resolved sibling descriptor file. Required for metadata and data-binding artifacts.")]
	public string DescriptorFile { get; set; }
}

/// <summary>
/// Reads explicit stage files and invokes the shared Creatio semantic merge service.
/// </summary>
public sealed class CreatioArtifactMergeCommand(
	ICreatioArtifactMergeService mergeService,
	IFileSystem fileSystem,
	ILogger logger) : Command<CreatioArtifactMergeOptions> {

	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true
	};

	/// <inheritdoc />
	public override int Execute(CreatioArtifactMergeOptions options) {
		ArgumentNullException.ThrowIfNull(options);

		try {
			long remainingBytes = CreatioArtifactMergeArgs.MaxCombinedContentBytes;
			CreatioArtifactMergeArgs args = new(
				options.ArtifactPath,
				ReadBounded(options.BaseFile, ref remainingBytes),
				ReadBounded(options.OursFile, ref remainingBytes),
				ReadBounded(options.TheirsFile, ref remainingBytes),
				ReadOptional(options.DescriptorFile, ref remainingBytes));
			CreatioArtifactMergeResult result = mergeService.MergeAsync(args).GetAwaiter().GetResult();
			logger.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
			return string.Equals(result.Status, CreatioArtifactMergeResult.ResolvedStatus, StringComparison.Ordinal) ? 0 : 1;
		}
		catch (IOException exception) {
			logger.WriteError($"Unable to read merge input: {exception.Message}");
			return 1;
		}
		catch (InvalidDataException exception) {
			logger.WriteError($"Unable to read merge input: {exception.Message}");
			return 1;
		}
		catch (UnauthorizedAccessException exception) {
			logger.WriteError($"Unable to read merge input: {exception.Message}");
			return 1;
		}
	}

	private string ReadOptional(string path, ref long remainingBytes) {
		return string.IsNullOrWhiteSpace(path) ? null : ReadBounded(path, ref remainingBytes);
	}

	private string ReadBounded(string path, ref long remainingBytes) {
		using Stream stream = fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length > remainingBytes) {
			throw new InvalidDataException("Combined merge content exceeds the 4 MiB limit.");
		}

		remainingBytes -= stream.Length;
		using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		return reader.ReadToEnd();
	}
}
