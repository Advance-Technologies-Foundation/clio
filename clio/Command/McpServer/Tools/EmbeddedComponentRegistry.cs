using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Anchor type used to locate the clio assembly that ships the embedded Freedom UI
/// component registry. Keeping this as a dedicated type avoids accidentally rebinding
/// the resource lookup when other types in the assembly are refactored.
/// </summary>
internal static class EmbeddedRegistryMarker { }

/// <summary>
/// Provides access to the Freedom UI component registry that is embedded into the
/// clio assembly at build time by the <c>ResolveCdnSnapshot</c> MSBuild target.
/// </summary>
public interface IEmbeddedRegistryReader {
	/// <summary>
	/// Opens a fresh read-only stream over the embedded component registry JSON.
	/// </summary>
	/// <returns>A disposable stream positioned at the start of the resource.</returns>
	Stream OpenRegistryStream();

	/// <summary>
	/// Returns the version label baked into the embedded snapshot at clio build time.
	/// </summary>
	/// <remarks>
	/// In v1 of the CDN model this is always <c>"latest"</c> (the MSBuild target fetches
	/// <c>latest.json</c> from the academy CDN). A future improvement can record the
	/// actual platform semver if the CDN response exposes it as a sidecar header.
	/// </remarks>
	string EmbeddedVersion { get; }
}

/// <summary>
/// Reads the component registry shipped as an embedded resource in the clio assembly.
/// </summary>
public sealed class EmbeddedRegistryReader : IEmbeddedRegistryReader {
	internal const string RegistryResourceName = "Clio.ComponentRegistry.ComponentRegistry.json";
	internal const string MetadataResourceName = "Clio.ComponentRegistry.embedded-metadata.json";

	private readonly Assembly _assembly = typeof(EmbeddedRegistryMarker).Assembly;
	private readonly Lazy<string> _embeddedVersion;

	public EmbeddedRegistryReader() {
		_embeddedVersion = new Lazy<string>(ReadEmbeddedVersion, isThreadSafe: true);
	}

	/// <inheritdoc />
	public Stream OpenRegistryStream() {
		Stream? stream = _assembly.GetManifestResourceStream(RegistryResourceName);
		if (stream is null) {
			string available = string.Join(", ", _assembly.GetManifestResourceNames());
			throw new InvalidOperationException(
				$"Embedded component registry resource '{RegistryResourceName}' was not found in {_assembly.FullName}. "
				+ $"Available resources: [{available}]. This indicates a broken clio build "
				+ "(MSBuild ResolveCdnSnapshot target did not embed the registry).");
		}

		return stream;
	}

	/// <inheritdoc />
	public string EmbeddedVersion => _embeddedVersion.Value;

	private string ReadEmbeddedVersion() {
		using Stream? stream = _assembly.GetManifestResourceStream(MetadataResourceName);
		if (stream is null) {
			return "latest";
		}

		try {
			JsonElement metadata = JsonSerializer.Deserialize<JsonElement>(stream);
			return metadata.TryGetProperty("embeddedVersion", out JsonElement version)
				&& version.ValueKind == JsonValueKind.String
				? version.GetString() ?? "latest"
				: "latest";
		} catch (JsonException) {
			return "latest";
		}
	}
}
