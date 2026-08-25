using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
	/// <remarks>
	/// A write that fails part-way removes the folder it created, so a failed export never leaves a partial
	/// bundle behind for the no-overwrite guard to reject on the retry.
	/// </remarks>
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
		try {
			_fileSystem.WriteAllTextToFile(System.IO.Path.Combine(bundleDirectory, DescriptorFileName),
				SystemTextJson.JsonSerializer.Serialize(bundle.Descriptor, WriteOptions));
			_fileSystem.WriteAllTextToFile(System.IO.Path.Combine(bundleDirectory, SchemaDataFileName),
				bundle.SchemaData);
		}
		catch {
			RollBackFailedBundle(bundleDirectory);
			throw;
		}
		// Deliberately OUTSIDE the rollback: by this point the authoritative payload is on disk and the export
		// has succeeded, so the projections are best-effort (see TryWriteProjections) and must never be able to
		// take the artifact back down with them.
		TryWriteProjections(bundleDirectory, bundle.SchemaData);
		return bundleDirectory;
	}

	/// <summary>
	/// Removes the bundle folder after a write that did not complete.
	/// </summary>
	/// <remarks>
	/// The folder is ours and was just created by <see cref="Write"/> — it did not exist a moment earlier,
	/// because Write refuses an existing one — so nothing but this failed attempt can be inside it. That same
	/// no-overwrite guard is why the rollback matters: a half-written folder left behind rejects every retry
	/// until the operator deletes it by hand, turning a transient disk or permission error into manual cleanup.
	/// The cleanup itself is best-effort and never replaces the original failure, which is what the caller needs
	/// to see; a cleanup that also fails is reported as a warning naming the folder to remove.
	/// </remarks>
	private void RollBackFailedBundle(string bundleDirectory) {
		try {
			_fileSystem.DeleteDirectoryIfExists(bundleDirectory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
			or System.Security.SecurityException or NotSupportedException or ArgumentException) {
			_logger?.WriteWarning(
				$"The export failed and '{bundleDirectory}' could not be cleaned up: {exception.Message}. "
				+ "Remove that folder before retrying — export never overwrites an existing bundle.");
		}
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
		SchemaBundleDescriptor descriptor = ResolveIdentity(DescribeFromPayload(schemaData),
			ReadDescriptor(bundleDirectory), schemaDataPath);
		return new SchemaBundle(descriptor, schemaData);
	}

	/// <summary>
	/// Combines the identity read from the payload with the provenance read from <c>descriptor.json</c>.
	/// </summary>
	/// <remarks>
	/// The payload is the authority for identity, because it is the only thing import writes: anything else lets
	/// the plan (and <c>--dry-run</c>) describe a different schema than the import performs. The descriptor keeps
	/// the fields it alone knows — source package, source environment, export timestamp, clio version. When the
	/// two disagree about identity the bundle is refused rather than silently retargeted, so a hand-edited or
	/// copy-pasted descriptor is a loud error instead of a wrong import.
	/// </remarks>
	/// <exception cref="InvalidOperationException">Thrown when the descriptor names a different schema.</exception>
	private static SchemaBundleDescriptor ResolveIdentity(SchemaBundleDescriptor payloadIdentity,
		SchemaBundleDescriptor fileDescriptor, string schemaDataPath) {
		if (fileDescriptor is null) {
			return payloadIdentity;
		}
		EnsureIdentityAgrees(payloadIdentity, fileDescriptor, schemaDataPath);
		fileDescriptor.SchemaName = payloadIdentity.SchemaName ?? fileDescriptor.SchemaName;
		fileDescriptor.SchemaUId = payloadIdentity.SchemaUId ?? fileDescriptor.SchemaUId;
		fileDescriptor.ManagerName = payloadIdentity.ManagerName ?? fileDescriptor.ManagerName;
		fileDescriptor.Caption = payloadIdentity.Caption ?? fileDescriptor.Caption;
		return fileDescriptor;
	}

	private static void EnsureIdentityAgrees(SchemaBundleDescriptor payloadIdentity,
		SchemaBundleDescriptor fileDescriptor, string schemaDataPath) {
		List<string> disagreements = [];
		AddDisagreement(disagreements, "schemaName", fileDescriptor.SchemaName, payloadIdentity.SchemaName,
			NamesAgree);
		AddDisagreement(disagreements, "schemaUId", fileDescriptor.SchemaUId, payloadIdentity.SchemaUId,
			UIdsAgree);
		AddDisagreement(disagreements, "managerName", fileDescriptor.ManagerName, payloadIdentity.ManagerName,
			NamesAgree);
		if (disagreements.Count == 0) {
			return;
		}
		throw new InvalidOperationException(
			$"The {DescriptorFileName} of this bundle describes a different schema than its "
			+ $"{SchemaDataFileName}: {string.Join("; ", disagreements)}. "
			+ $"'{schemaDataPath}' is what the import writes, so the mismatch is refused rather than importing "
			+ $"under one identity while reporting another. Remove or correct {DescriptorFileName} — it is "
			+ "provenance only and import reads the payload without it.");
	}

	private static void AddDisagreement(List<string> disagreements, string field, string descriptorValue,
		string payloadValue, Func<string, string, bool> agree) {
		if (string.IsNullOrWhiteSpace(descriptorValue) || string.IsNullOrWhiteSpace(payloadValue)
			|| agree(descriptorValue, payloadValue)) {
			return;
		}
		disagreements.Add($"{field} is '{descriptorValue}' in {DescriptorFileName} "
			+ $"but '{payloadValue}' in {SchemaDataFileName}");
	}

	private static bool NamesAgree(string descriptorValue, string payloadValue) =>
		string.Equals(descriptorValue.Trim(), payloadValue.Trim(), StringComparison.OrdinalIgnoreCase);

	private static bool UIdsAgree(string descriptorValue, string payloadValue) =>
		Guid.TryParse(descriptorValue, out Guid descriptorUId) && Guid.TryParse(payloadValue, out Guid payloadUId)
			? descriptorUId == payloadUId
			: NamesAgree(descriptorValue, payloadValue);

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
	/// Reads the schema identity out of the payload, which is the authority for what an import writes.
	/// </summary>
	private static SchemaBundleDescriptor DescribeFromPayload(string schemaData) {
		JObject payload = ParsePayload(schemaData);
		return new SchemaBundleDescriptor {
			SchemaName = ReadString(payload, "Name"),
			SchemaUId = ReadString(payload, "UId"),
			Caption = ReadString(payload, "Caption"),
			ManagerName = ReadString(payload, "ManagerName")
		};
	}

	/// <summary>
	/// Reads one string member of a payload, treating a non-string member as absent.
	/// </summary>
	/// <remarks>
	/// <c>JToken.Value&lt;string&gt;</c> throws <see cref="InvalidCastException"/> — not a
	/// <see cref="JsonException"/> — when the member is an object or an array, so a payload that parses but
	/// carries, say, an object in <c>MetaData</c> would otherwise take down whichever operation is reading it.
	/// A member of the wrong shape is nothing this class can use, so it reads as missing.
	/// </remarks>
	private static string ReadString(JObject payload, string propertyName) =>
		payload?[propertyName] is JValue { Value: not null } value
			? Convert.ToString(value.Value, CultureInfo.InvariantCulture)
			: null;

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
			or System.Security.SecurityException or NotSupportedException or JsonException
			or InvalidCastException) {
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
		string metadata = ReadString(payload, "MetaData");
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
			string culture = ReadString(value as JObject, "Culture") ?? "unknown";
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
