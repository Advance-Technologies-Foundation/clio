using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SystemTextJson = System.Text.Json;

namespace Clio.Command.SchemaTransfer;

/// <summary>
/// Reads and writes schema-export bundles on disk.
/// </summary>
/// <remarks>
/// A bundle is a folder. Only <c>schema-data.json</c> is authoritative — it holds the verbatim platform
/// payload and is the only file <see cref="Read"/> consumes. Everything else is a projection written for a
/// human reviewer, so a hand-edited projection can never silently become the thing that ships.
/// </remarks>
public interface ISchemaBundleStore {

	/// <summary>
	/// Writes a bundle into the given folder.
	/// </summary>
	/// <param name="bundleDirectory">
	/// The exact folder to write the bundle into. The caller owns confining this path — see
	/// <c>OutputPathConfinement</c>, which <c>export-schema</c> applies because the destination can be supplied
	/// by an MCP agent rather than typed at a shell.
	/// </param>
	/// <param name="bundle">Bundle to write.</param>
	/// <returns>The full path of the bundle folder that was written.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the bundle folder already exists.</exception>
	string Write(string bundleDirectory, SchemaBundle bundle);

	/// <summary>
	/// Reads a bundle.
	/// </summary>
	/// <param name="path">
	/// Either a bundle folder or the <c>schema-data.json</c> file inside one.
	/// </param>
	/// <returns>The bundle, with its descriptor when the folder carries one.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the path is not a readable bundle.</exception>
	SchemaBundle Read(string path);
}

/// <inheritdoc cref="ISchemaBundleStore"/>
public sealed class SchemaBundleStore : ISchemaBundleStore {

	/// <summary>Name of the authoritative payload file inside a bundle folder.</summary>
	public const string SchemaDataFileName = "schema-data.json";

	/// <summary>Name of the provenance file inside a bundle folder.</summary>
	public const string DescriptorFileName = "descriptor.json";

	private const string MetadataFileName = "metadata.json";
	private const string PropertiesFileName = "properties.json";
	private const string ResourcesDirectoryName = "resources";

	private static readonly SystemTextJson.JsonSerializerOptions WriteOptions = new() {
		WriteIndented = true
	};

	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="SchemaBundleStore"/> class.
	/// </summary>
	/// <param name="fileSystem">File system abstraction used for all reads and writes.</param>
	/// <param name="logger">Sink for the warning a skipped projection produces.</param>
	public SchemaBundleStore(IFileSystem fileSystem, ILogger logger) {
		_fileSystem = fileSystem;
		_logger = logger;
	}

	/// <inheritdoc/>
	public string Write(string bundleDirectory, SchemaBundle bundle) {
		ArgumentNullException.ThrowIfNull(bundle);
		if (_fileSystem.ExistsDirectory(bundleDirectory)) {
			throw new InvalidOperationException(
				$"'{bundleDirectory}' already exists. Choose another destination, or remove it first — "
				+ "export never overwrites an existing bundle.");
		}
		_fileSystem.CreateDirectory(bundleDirectory);
		_fileSystem.WriteAllTextToFile(System.IO.Path.Combine(bundleDirectory, DescriptorFileName),
			SystemTextJson.JsonSerializer.Serialize(bundle.Descriptor, WriteOptions));
		_fileSystem.WriteAllTextToFile(System.IO.Path.Combine(bundleDirectory, SchemaDataFileName),
			bundle.SchemaData);
		TryWriteProjections(bundleDirectory, bundle.SchemaData);
		return bundleDirectory;
	}

	/// <inheritdoc/>
	public SchemaBundle Read(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			throw new InvalidOperationException("A bundle path is required.");
		}
		string schemaDataPath;
		string bundleDirectory;
		if (_fileSystem.ExistsDirectory(path)) {
			bundleDirectory = path;
			schemaDataPath = System.IO.Path.Combine(path, SchemaDataFileName);
		} else {
			schemaDataPath = path;
			bundleDirectory = System.IO.Path.GetDirectoryName(path);
		}
		if (!_fileSystem.ExistsFile(schemaDataPath)) {
			throw new InvalidOperationException(
				$"'{schemaDataPath}' was not found. Point import at a bundle folder produced by "
				+ $"'clio export-schema', or directly at its {SchemaDataFileName}.");
		}
		string schemaData = _fileSystem.ReadAllText(schemaDataPath);
		if (string.IsNullOrWhiteSpace(schemaData)) {
			throw new InvalidOperationException($"'{schemaDataPath}' is empty.");
		}
		SchemaBundleDescriptor descriptor = ReadDescriptor(bundleDirectory) ?? DescribeFromPayload(schemaData);
		return new SchemaBundle(descriptor, schemaData);
	}

	private SchemaBundleDescriptor ReadDescriptor(string bundleDirectory) {
		if (string.IsNullOrEmpty(bundleDirectory)) {
			return null;
		}
		string descriptorPath = System.IO.Path.Combine(bundleDirectory, DescriptorFileName);
		if (!_fileSystem.ExistsFile(descriptorPath)) {
			return null;
		}
		try {
			return SystemTextJson.JsonSerializer.Deserialize<SchemaBundleDescriptor>(_fileSystem.ReadAllText(descriptorPath));
		}
		catch (SystemTextJson.JsonException) {
			// The descriptor is provenance, not input: a damaged one must not block an import whose actual
			// payload is intact, so fall back to reading the identity out of the payload itself.
			return null;
		}
	}

	/// <summary>
	/// Recovers the schema identity from the payload, for a bundle whose descriptor is missing or damaged.
	/// </summary>
	private static SchemaBundleDescriptor DescribeFromPayload(string schemaData) {
		JObject payload = ParsePayload(schemaData);
		return new SchemaBundleDescriptor {
			SchemaName = payload?.Value<string>("Name"),
			SchemaUId = payload?.Value<string>("UId"),
			Caption = payload?.Value<string>("Caption"),
			ManagerName = payload?.Value<string>("ManagerName")
		};
	}

	/// <summary>
	/// Writes the human-readable projections of the payload: the metadata document, the properties list and the
	/// localization resources, one file per culture.
	/// </summary>
	/// <remarks>
	/// Best-effort. A projection that cannot be produced is skipped rather than failing the export — the
	/// authoritative <c>schema-data.json</c> has already been written, and losing a convenience view must not
	/// cost the operator the artifact they came for.
	/// </remarks>
	/// <remarks>
	/// The best-effort contract has to hold for EVERY way a projection can fail, not only for an
	/// unparsable payload. The authoritative <c>schema-data.json</c> is already on disk by the time this
	/// runs, so letting an I/O failure escape would abort a completed export and — because
	/// <see cref="Write"/> refuses to overwrite an existing bundle folder — leave a half-written folder
	/// that blocks every retry until the operator deletes it by hand. The failure is logged rather than
	/// swallowed, so a missing projection is still visible in the run output.
	/// </remarks>
	private void TryWriteProjections(string bundleDirectory, string schemaData) {
		try {
			WriteProjections(bundleDirectory, schemaData);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
			or System.Security.SecurityException or NotSupportedException or JsonException) {
			_logger?.WriteWarning(
				$"The bundle in '{bundleDirectory}' was exported, but its human-readable projections could not "
				+ $"be written: {exception.Message}. The authoritative {SchemaDataFileName} is intact and "
				+ "import-schema only reads that file.");
		}
	}

	private void WriteProjections(string bundleDirectory, string schemaData) {
		JObject payload = ParsePayload(schemaData);
		if (payload is null) {
			return;
		}
		string metadata = payload.Value<string>("MetaData");
		if (!string.IsNullOrWhiteSpace(metadata)) {
			_fileSystem.WriteAllTextToFile(System.IO.Path.Combine(bundleDirectory, MetadataFileName),
				Prettify(metadata));
		}
		JToken properties = payload["Properties"];
		if (properties is not null) {
			_fileSystem.WriteAllTextToFile(System.IO.Path.Combine(bundleDirectory, PropertiesFileName),
				properties.ToString(Formatting.Indented));
		}
		WriteResourceProjections(bundleDirectory, payload["LocalizableValues"] as JArray);
	}

	private void WriteResourceProjections(string bundleDirectory, JArray localizableValues) {
		if (localizableValues is null || localizableValues.Count == 0) {
			return;
		}
		Dictionary<string, JArray> byCulture = new(StringComparer.OrdinalIgnoreCase);
		foreach (JToken value in localizableValues.Where(item => item is not null)) {
			string culture = value.Value<string>("Culture") ?? "unknown";
			if (!byCulture.TryGetValue(culture, out JArray bucket)) {
				bucket = [];
				byCulture[culture] = bucket;
			}
			bucket.Add(value.DeepClone());
		}
		string resourcesDirectory = System.IO.Path.Combine(bundleDirectory, ResourcesDirectoryName);
		_fileSystem.CreateDirectory(resourcesDirectory);
		foreach (KeyValuePair<string, JArray> culture in byCulture) {
			_fileSystem.WriteAllTextToFile(
				System.IO.Path.Combine(resourcesDirectory, $"resource.{culture.Key}.json"),
				culture.Value.ToString(Formatting.Indented));
		}
	}

	/// <summary>
	/// Parses the platform payload.
	/// </summary>
	/// <returns>The parsed payload, or <c>null</c> when it is not JSON at all.</returns>
	/// <remarks>
	/// Deliberately Newtonsoft, not <c>System.Text.Json</c>. The platform exporter embeds the schema metadata as
	/// a JSON STRING containing raw CR/LF control characters, which RFC 8259 forbids and
	/// <c>System.Text.Json</c> therefore refuses — every real payload would fail to parse and every projection
	/// would be silently skipped. Newtonsoft accepts it.
	/// </remarks>
	[SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
		Justification = "null means 'the payload is not JSON at all', which every caller branches on; an empty "
			+ "JObject would be indistinguishable from a valid payload with no members and would make the "
			+ "projections silently wrong instead of skipped.")]
	private static JObject ParsePayload(string schemaData) {
		try {
			return JObject.Parse(schemaData);
		}
		catch (JsonException) {
			return null;
		}
	}

	private static string Prettify(string json) {
		JObject parsed = ParsePayload(json);
		return parsed is null ? json : parsed.ToString(Formatting.Indented);
	}
}
