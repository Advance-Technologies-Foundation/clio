using Clio10.Contracts;
namespace Clio10.PrimitiveContracts;

/// <summary>Resource bounds for a Creatio gzip record stream; limits apply before publishing extracted data.</summary>
public sealed record ArchiveLimits(int MaxEntries = 100_000, long MaxExpandedBytes = 21_474_836_480, int MaxPathCharacters = 32_767);

/// <summary>Completed archive publication, containing only portable data.</summary>
public sealed record ArchiveResult(string Destination, int Files, long Bytes, string? RetainedBackup = null);

/// <summary>Publication failed and automatic restoration also failed; the previous destination remains at BackupPath.</summary>
public sealed class ArchiveRestoreException(string destination, string backupPath, Exception innerException)
    : IOException("Archive publication failed; the original directory is preserved in a backup.", innerException) {
    /// <summary>Original destination requiring recovery.</summary>
    public string Destination { get; } = destination;
    /// <summary>Existing backup of the original directory; never deleted after restoration failure.</summary>
    public string BackupPath { get; } = backupPath;
}

/// <summary>Immediate filesystem entry metadata; links are reported without traversing their targets.</summary>
public sealed record ArchiveEntry(string Name, bool IsDirectory, bool IsLink);

/// <summary>A selected source tree to encode within a ZIP container. Name is a single package name; the encoder appends .gz.</summary>
public sealed record ArchiveSource(string Name, string Root, IReadOnlyList<string> Files);

/// <summary>An existing gzip file and the single directory name to publish beneath the caller's destination.</summary>
public sealed record ArchiveInput(string Name, string Path);

/// <summary>Bounds shared by every archive in a batch, including the ZIP container payload.</summary>
public sealed record ArchiveContainerLimits(ArchiveLimits Content, int MaxArchives = 1000, long MaxContainerBytes = 21_474_836_480, int MaxMetadataBytes = 16_777_216);

/// <summary>Published package receipts. A failed destination means publication stopped after the listed successful packages.</summary>
public sealed record ArchiveBatchResult(IReadOnlyList<ArchiveResult> Completed, string? FailedDestination = null, string? RecoveryPath = null);

/// <summary>Binary archive and file enumeration capability, independent of package selection policy.</summary>
[Capability("archive")]
public interface IArchivePrimitive {
    /// <summary>Checks whether an explicit path identifies an existing directory.</summary>
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken);
    /// <summary>Lists immediate entry names without traversing directories or symbolic links.</summary>
    Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string root, CancellationToken cancellationToken);
    /// <summary>Lists regular files relative to an explicit root; symbolic links and reparse points are rejected.</summary>
    Task<IReadOnlyList<string>> ListFilesAsync(string root, CancellationToken cancellationToken);
    /// <summary>Writes selected relative files in Creatio's gzip record format, publishing only a complete archive.</summary>
    Task<ArchiveResult> PackAsync(string root, IReadOnlyList<string> files, string destination, ArchiveLimits limits, CancellationToken cancellationToken);
    /// <summary>Validates a bounded Creatio stream before publication. Explicit overwrite replaces a whole directory, preserving it on validation failure.</summary>
    /// <remarks>A retained backup is reported when publication succeeded but backup cleanup failed. Restoration failure throws ArchiveRestoreException.</remarks>
    Task<ArchiveResult> ExtractAsync(string archive, string destination, ArchiveLimits limits, CancellationToken cancellationToken, bool overwrite = false);
    /// <summary>Creates a ZIP of named Creatio gzip archives and publishes only the complete container.</summary>
    Task<ArchiveResult> PackContainerAsync(IReadOnlyList<ArchiveSource> sources, string destination, ArchiveContainerLimits limits, CancellationToken cancellationToken);
    /// <summary>Extracts root gzip entries from a ZIP. All entries are decoded before any destination changes.</summary>
    Task<ArchiveBatchResult> ExtractContainerAsync(string archive, string destination, ArchiveContainerLimits limits, CancellationToken cancellationToken, bool overwrite = false);
    /// <summary>Validates a batch of gzip files then publishes each package. Cancellation is honored before publication; completed receipts survive later publication failure.</summary>
    Task<ArchiveBatchResult> ExtractManyAsync(IReadOnlyList<ArchiveInput> inputs, string destination, ArchiveContainerLimits limits, CancellationToken cancellationToken, bool overwrite = false);
}
