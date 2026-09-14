using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>Options for saving a Binary system setting to a caller-named file.</summary>
[Verb("download-sys-setting-file", HelpText = "Save a Binary system setting's exact bytes to a required filename.")]
[RequiresPackage("cliogate", Hint = "Run 'clio install-gate -e <environment>' first.")]
public sealed class DownloadSysSettingFileOptions : EnvironmentOptions {

	/// <summary>Gets or sets the system setting code.</summary>
	[Option("code", Required = true, HelpText = "Code of an existing Binary system setting.")]
	public string Code { get; set; }

	/// <summary>Gets or sets the destination path including its filename.</summary>
	[Option("file-name", Required = true, HelpText = "Destination path including filename. Must not already exist. No format or extension is inferred.")]
	public string FileName { get; set; }
}

/// <summary>Metadata for bytes saved locally; file content is never returned.</summary>
/// <param name="FileName">Absolute destination path supplied by the caller.</param>
/// <param name="ByteCount">Number of decoded bytes saved.</param>
public sealed record DownloadSysSettingFileReceipt(
	[property: JsonPropertyName("file-name")] string FileName,
	[property: JsonPropertyName("byte-count")] long ByteCount);

/// <summary>Saves a Binary system setting without interpreting its content.</summary>
public interface IDownloadSysSettingFileService {
	/// <summary>Reads the effective user value through ClioGate and saves its decoded bytes without overwriting.</summary>
	/// <param name="code">Existing Binary system setting code.</param>
	/// <param name="fileName">Destination path including filename in an existing directory.</param>
	/// <returns>The saved path and byte count.</returns>
	DownloadSysSettingFileReceipt Download(string code, string fileName);
}

/// <inheritdoc />
public sealed class DownloadSysSettingFileService(ISysSettingsManager settings, IDataProvider dataProvider,
	IFileSystem fileSystem) : IDownloadSysSettingFileService {

	/// <inheritdoc />
	public DownloadSysSettingFileReceipt Download(string code, string fileName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(code);
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		if (string.IsNullOrWhiteSpace(fileSystem.Path.GetFileName(fileName))) {
			throw new ArgumentException("file-name must include a filename, not only a directory.");
		}
		string destination = fileSystem.Path.GetFullPath(fileName);
		if (fileSystem.File.Exists(destination) || fileSystem.Directory.Exists(destination)) {
			throw new IOException("Destination already exists; choose a new filename.");
		}
		string directory = fileSystem.Path.GetDirectoryName(destination);
		if (!fileSystem.Directory.Exists(directory)) {
			throw new DirectoryNotFoundException("Destination directory does not exist.");
		}
		var definition = settings.GetSysSettingTypeByCode(code);
		if (definition is null) {
			throw new ArgumentException("System setting does not exist or is not readable.");
		}
		if (!string.Equals(definition.Value.ValueTypeName, "Binary", StringComparison.Ordinal)) {
			throw new ArgumentException("Only Binary system settings can be downloaded as files.");
		}
		// Unlike the legacy scalar reader, never turn a ClioGate failure into an empty file.
		string value = dataProvider.GetSysSettingValue<string>(code);
		if (string.IsNullOrWhiteSpace(value)) {
			throw new InvalidOperationException("Binary system setting has no downloadable value.");
		}
		long cap = SysSettingsManager.MaxBinaryValueBytes;
		if (value.Length > ((cap + 2) / 3) * 4) {
			throw new InvalidOperationException("Binary value exceeds the 10 MB download limit.");
		}
		byte[] bytes = Convert.FromBase64String(value);
		if (bytes.LongLength > cap) {
			throw new InvalidOperationException("Binary value exceeds the 10 MB download limit.");
		}
		string temporary = fileSystem.Path.Combine(directory, $".clio-{Guid.NewGuid():N}.tmp");
		try {
			using (Stream stream = fileSystem.FileStream.New(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
				stream.Write(bytes);
			}
			fileSystem.File.Move(temporary, destination, overwrite: false);
		}
		finally {
			if (fileSystem.File.Exists(temporary)) {
				fileSystem.File.Delete(temporary);
			}
		}
		return new DownloadSysSettingFileReceipt(destination, bytes.LongLength);
	}
}

/// <summary>CLI adapter for the Binary system-setting file download service.</summary>
public sealed class DownloadSysSettingFileCommand(IDownloadSysSettingFileService service, ILogger logger)
	: Command<DownloadSysSettingFileOptions> {

	/// <summary>Saves the bytes and prints only the resulting path and byte count.</summary>
	public override int Execute(DownloadSysSettingFileOptions options) {
		DownloadSysSettingFileReceipt receipt = Download(options);
		logger.WriteInfo(JsonSerializer.Serialize(receipt));
		return 0;
	}

	/// <summary>Runs the shared download operation for the CLI and typed MCP adapter.</summary>
	public DownloadSysSettingFileReceipt Download(DownloadSysSettingFileOptions options) =>
		service.Download(options.Code, options.FileName);
}
