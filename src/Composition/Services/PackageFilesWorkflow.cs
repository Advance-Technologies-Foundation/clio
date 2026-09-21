using Clio10.Contracts;
using Clio10.PrimitiveContracts;

namespace Clio10.Composition.Services;

/// <summary>Lists or reads materialized package files; the HTTP primitive owns authentication and transport.</summary>
public sealed class PackageFilesWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override bool RequiresSuccess => false;
    /// <inheritdoc />
    protected override bool IsRead => true;
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) {
        string package = Text(context, "package").Trim();
        string? file = FileName(context);
        string route = file is null ? "GetPackageFilesDirectoryContent" : "GetPackageFileContent";
        // Apply platform routing to the fixed endpoint, before appending opaque, encoded query values.
        string path = Route(context, "rest/CreatioApiGateway/" + route) + "?packageName=" + Uri.EscapeDataString(package);
        if (file is not null) path += "&filePath=" + Uri.EscapeDataString(file);
        return new("GET", path);
    }
    /// <inheritdoc />
    protected override OperationResult Interpret(OperationResult result, IWorkflowContext context) {
        string? file = FileName(context);
        var payload = new Dictionary<string, object?> { ["package-name"] = Text(context, "package").Trim() };
        if (file is null) {
            if (result.Payload is not IReadOnlyList<object?> values || values.Any(x => x is not string))
                return result with { Accepted = false, Code = "invalid-service-response", Payload = null };
            var files = values.Cast<string>().Select(x => x.Replace('\\', '/').TrimStart('/'))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).ToArray();
            payload["files"] = files;
            payload["count"] = files.Length;
        } else {
            if (result.Payload is not string content)
                return result with { Accepted = false, Code = "invalid-service-response", Payload = null };
            payload["file-path"] = file;
            payload["content"] = content;
            payload["content-length"] = content.Length;
        }
        return result with { Payload = payload };
    }
    private static string? FileName(IWorkflowContext context) {
        if (!context.Arguments.TryGetValue("file", out var value) || string.IsNullOrWhiteSpace(value as string)) return null;
        string file = ((string)value!).Trim().Replace('\\', '/');
        if (file.StartsWith('/') || file.Contains(':') || file.Split('/').Any(x => x is "." or ".."))
            throw new ArgumentException("A package-relative path is required.");
        return file;
    }
}
