using Clio10.Contracts;
using Clio10.PrimitiveContracts;

namespace Clio10.Composition.Files;

/// <summary>Compares two directory snapshots by ordinal relative path and SHA-256 content.</summary>
public sealed class CompareDirectoriesWorkflow : IClioWorkflow {
    /// <summary>Read-only discovery metadata for local directory comparison.</summary>
    public static OperationDescriptor Descriptor => new("compare-directories", "Compare file contents in two local directory trees.", false,
        [new("left", ArgumentKind.String, true), new("right", ArgumentKind.String, true)]);

    /// <summary>Requires directory snapshots from a compatible Clio 10 runtime.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0), new(11, 0), Capabilities: ["directory-snapshot"]);

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        if (!context.Arguments.TryGetValue("left", out var leftValue) || leftValue is not string left || string.IsNullOrWhiteSpace(left) ||
            !context.Arguments.TryGetValue("right", out var rightValue) || rightValue is not string right || string.IsNullOrWhiteSpace(right))
            return new(false, "invalid-arguments");

        cancellationToken.ThrowIfCancellationRequested();
        try {
            var primitive = context.Get<IDirectorySnapshotPrimitive>();
            var leftSnapshot = await primitive.SnapshotAsync(left, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var rightSnapshot = await primitive.SnapshotAsync(right, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var leftFiles = leftSnapshot.ToDictionary(file => file.RelativePath, file => file.Sha256, StringComparer.Ordinal);
            var rightFiles = rightSnapshot.ToDictionary(file => file.RelativePath, file => file.Sha256, StringComparer.Ordinal);
            var payload = new Dictionary<string, object?> {
                ["added"] = rightFiles.Keys.Except(leftFiles.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                ["removed"] = leftFiles.Keys.Except(rightFiles.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                ["changed"] = leftFiles.Keys.Where(path => rightFiles.TryGetValue(path, out var digest) &&
                    !string.Equals(leftFiles[path], digest, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray()
            };
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, "completed", PrimitiveVersion: context.PrimitiveVersion.ToString(), Payload: payload);
        }
        catch (DirectoryLinkException) { return Failure("symbolic-link-not-supported"); }
        catch (DirectoryNotFoundException) { return Failure("directory-not-found"); }
        catch (UnauthorizedAccessException) { return Failure("directory-access-denied"); }
        catch (IOException) { return Failure("directory-read-failed"); }
        catch (ArgumentException) { return Failure("invalid-arguments"); }
        catch (NotSupportedException) { return Failure("invalid-arguments"); }

        OperationResult Failure(string code) => new(false, code, PrimitiveVersion: context.PrimitiveVersion.ToString());
    }
}
