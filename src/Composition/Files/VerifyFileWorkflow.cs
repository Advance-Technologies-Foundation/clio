using Clio10.Contracts;
using Clio10.PrimitiveContracts;

namespace Clio10.Composition.Files;

/// <summary>Validates a caller-supplied SHA-256 digest and compares it with a file-hash capability result.</summary>
public sealed class VerifyFileWorkflow : IClioWorkflow {
    /// <summary>Read-only discovery metadata for local file verification.</summary>
    public static OperationDescriptor Descriptor => new("verify-file", "Compare a local file's SHA-256 with an expected digest.", false,
        [new("path", ArgumentKind.String, true), new("sha256", ArgumentKind.String, true)]);

    /// <summary>Requires file hashing from a compatible Clio 10 runtime.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0), new(11, 0), Capabilities: ["file-hash"]);

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        if (!context.Arguments.TryGetValue("path", out var pathValue) || pathValue is not string path || string.IsNullOrWhiteSpace(path) ||
            !context.Arguments.TryGetValue("sha256", out var digestValue) || digestValue is not string expected ||
            expected.Length != 64 || !expected.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            return new(false, "invalid-arguments");

        cancellationToken.ThrowIfCancellationRequested();
        try {
            var actual = await context.Get<IFileHashPrimitive>().HashAsync(path, cancellationToken);
            bool matches = string.Equals(actual.Sha256, expected, StringComparison.OrdinalIgnoreCase);
            return new(matches, matches ? "completed" : "hash-mismatch", PrimitiveVersion: context.PrimitiveVersion.ToString(),
                Payload: new Dictionary<string, object?> {
                    ["sha256"] = actual.Sha256, ["bytes"] = actual.Bytes, ["matches"] = matches
                });
        }
        catch (FileNotFoundException) { return Failure("file-not-found"); }
        catch (DirectoryNotFoundException) { return Failure("file-not-found"); }
        catch (UnauthorizedAccessException) { return Failure("file-access-denied"); }
        catch (IOException) { return Failure("file-read-failed"); }

        OperationResult Failure(string code) => new(false, code, PrimitiveVersion: context.PrimitiveVersion.ToString());
    }
}
