using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio10.Product.Tests;

/// <summary>Proves directory comparison through the real generic CLI and MCP entry points.</summary>
public sealed class CompareDirectoriesProductTests {
    private static string ProductPath => Environment.GetEnvironmentVariable("CLIO10_TEST_PRODUCT") ??
        typeof(CompareDirectoriesProductTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "ProductPath").Value!;

    /// <summary>Checks the read-only operation and required inputs on both discovery surfaces.</summary>
    [TestCase(false)]
    [TestCase(true)]
    [Description("CLI and MCP discover compare-directories with required left and right string arguments and no extra resident tool.")]
    public async Task Discovery_exposes_comparison_metadata(bool mcp) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = mcp ? await StartMcp(timeout.Token) : null;

        // Act
        string catalog;
        if (client is not null) {
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            tools.Select(x => x.Name).Should().BeEquivalentTo(new[] { "list-operations", "execute" },
                "new operations must use the existing generic MCP bridge");
            var response = await client.CallToolAsync("list-operations", cancellationToken: timeout.Token);
            response.IsError.GetValueOrDefault().Should().BeFalse("local discovery needs no external target");
            catalog = response.Content.OfType<TextContentBlock>().Single().Text;
        }
        else catalog = await RunCli(["--list"], 0, timeout.Token);
        using var operations = JsonDocument.Parse(catalog);
        var descriptor = operations.RootElement.EnumerateArray().Single(x => x.GetProperty("Name").GetString() == "compare-directories");

        // Assert
        AssertDescriptor(descriptor);
        if (!mcp) {
            using var help = JsonDocument.Parse(await RunCli(["compare-directories", "--help"], 0, timeout.Token));
            AssertDescriptor(help.RootElement);
        }
    }

    /// <summary>Exercises deterministic real trees, comparison direction, and failure envelopes.</summary>
    [TestCase(false)]
    [TestCase(true)]
    [Description("CLI and MCP preserve sorted difference arrays, equal and same-tree success, content hashing, read-only behavior and stable input failures.")]
    public async Task Comparison_outcomes_survive_adapters(bool mcp) {
        // Arrange
        string root = Directory.CreateDirectory(Path.Combine(CanonicalDirectory(Path.GetTempPath()),
            "clio10-product-compare-" + Guid.NewGuid().ToString("N"))).FullName;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try {
            string left = Directory.CreateDirectory(Path.Combine(root, "left tree")).FullName;
            string right = Directory.CreateDirectory(Path.Combine(root, "right tree")).FullName;
            foreach (string tree in new[] { left, right }) {
                Directory.CreateDirectory(Path.Combine(tree, "nested"));
                Directory.CreateDirectory(Path.Combine(tree, "equal-tree"));
                await File.WriteAllTextAsync(Path.Combine(tree, "equal-tree", "same.txt"), "equal content", timeout.Token);
                await File.WriteAllTextAsync(Path.Combine(tree, "nested", "equal.txt"), "same", timeout.Token);
                await File.WriteAllTextAsync(Path.Combine(tree, "empty.bin"), "", timeout.Token);
            }
            Directory.CreateDirectory(Path.Combine(left, "ignored-empty-directory"));
            await File.WriteAllTextAsync(Path.Combine(left, "changed.txt"), "abc", timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(right, "changed.txt"), "abd", timeout.Token);
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(left, "changed.txt"), timestamp);
            File.SetLastWriteTimeUtc(Path.Combine(right, "changed.txt"), timestamp);
            await File.WriteAllTextAsync(Path.Combine(left, "removed.txt"), "old", timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(right, "z-added.txt"), "new", timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(right, "nested", "A-added.txt"), "new", timeout.Token);
            var before = CaptureFiles(root);
            await using var client = mcp ? await StartMcp(timeout.Token) : null;

            // Act
            var forward = await Run(left, right);
            var reverse = await Run(right, left);
            var same = await Run(left, left);
            var equal = await Run(Path.Combine(left, "equal-tree"), Path.Combine(right, "equal-tree"));
            await Run(left, Path.Combine(root, "missing"), "directory-not-found");
            await Run(Path.Combine(root, "missing"), right, "directory-not-found");
            await Run(left, Path.Combine(left, "changed.txt"), "directory-not-found");
            await Run(left, null, "invalid-arguments");
            await Run(null, right, "invalid-arguments");
            await Run(" ", right, "invalid-arguments");
            await Run(left, " ", "invalid-arguments");

            // Assert
            AssertDiff(forward, ["nested/A-added.txt", "z-added.txt"], ["removed.txt"], ["changed.txt"]);
            AssertDiff(reverse, ["removed.txt"], ["nested/A-added.txt", "z-added.txt"], ["changed.txt"]);
            AssertDiff(same, [], [], []);
            AssertDiff(equal, [], [], []);
            CaptureFiles(root).Should().BeEquivalentTo(before, "comparison and failures must leave every input path and byte unchanged");
            Directory.Exists(Path.Combine(left, "ignored-empty-directory")).Should().BeTrue("comparison must preserve empty directories");

            async Task<JsonElement> Run(string? first, string? second, string code = "completed") {
                var arguments = new Dictionary<string, object?>();
                if (first is not null) arguments["left"] = first;
                if (second is not null) arguments["right"] = second;
                bool rejected = code != "completed";
                string output;
                if (client is not null) {
                    var result = await client.CallToolAsync("execute", new Dictionary<string, object?> {
                        ["operation"] = "compare-directories", ["arguments"] = arguments
                    }, cancellationToken: timeout.Token);
                    result.IsError.GetValueOrDefault().Should().Be(rejected, "differences are successful comparisons and only failures set the MCP error flag");
                    output = result.Content.OfType<TextContentBlock>().Single().Text;
                }
                else {
                    string[] options = first is null || second is null
                        ? ["--execute", "compare-directories", "--local", JsonSerializer.Serialize(arguments)]
                        : ["compare-directories", "--left", first, "--right", second];
                    output = await RunCli(options, rejected ? 1 : 0, timeout.Token);
                }
                using var document = JsonDocument.Parse(output);
                var outcome = document.RootElement;
                outcome.GetProperty("Accepted").GetBoolean().Should().Be(!rejected, "differences must not reject an otherwise successful comparison");
                outcome.GetProperty("Code").GetString().Should().Be(code, "adapters must preserve the stable operation category");
                if (rejected)
                    outcome.GetProperty("Payload").ValueKind.Should().Be(JsonValueKind.Null, "failed comparisons must not return a partial diff");
                return outcome.Clone();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Checks the conventional named-option usage contract.</summary>
    [Test]
    [Description("Missing right on the named CLI entry point produces the existing usage error before operation execution.")]
    public async Task Missing_named_right_is_a_cli_usage_error() {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Act
        string output = await RunCli(["compare-directories", "--left", "."], 2, timeout.Token);

        // Assert
        output.Should().BeEmpty("missing required named options do not produce a workflow result");
    }

    private static string CanonicalDirectory(string path) {
        var directory = new DirectoryInfo(path);
        if (directory.Parent is null) return directory.FullName;
        // Resolve each ancestor, including platform temp aliases such as macOS /var.
        var canonical = new DirectoryInfo(Path.Combine(CanonicalDirectory(directory.Parent.FullName), directory.Name));
        var target = canonical.ResolveLinkTarget(returnFinalTarget: true);
        return target is null ? canonical.FullName : CanonicalDirectory(target.FullName);
    }

    private static Dictionary<string, string> CaptureFiles(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToBase64String(File.ReadAllBytes(path)), StringComparer.Ordinal);

    private static void AssertDescriptor(JsonElement descriptor) {
        descriptor.GetProperty("Name").GetString().Should().Be("compare-directories", "operation identity is part of the frozen contract");
        descriptor.GetProperty("Destructive").GetBoolean().Should().BeFalse("comparison only reads local trees");
        var arguments = descriptor.GetProperty("Arguments").EnumerateArray().ToArray();
        arguments.Select(x => x.GetProperty("Name").GetString()).Should().BeEquivalentTo(new[] { "left", "right" }, "comparison requires exactly two roots");
        foreach (var argument in arguments) {
            argument.GetProperty("Kind").GetString().Should().Be("String", "directory roots are string arguments");
            argument.GetProperty("Required").GetBoolean().Should().BeTrue("both roots are mandatory");
        }
    }

    private static void AssertDiff(JsonElement result, string[] added, string[] removed, string[] changed) {
        var payload = result.GetProperty("Payload");
        payload.GetProperty("added").EnumerateArray().Select(x => x.GetString()).Should().Equal(added, "added paths are sorted right-only relative paths");
        payload.GetProperty("removed").EnumerateArray().Select(x => x.GetString()).Should().Equal(removed, "removed paths are sorted left-only relative paths");
        payload.GetProperty("changed").EnumerateArray().Select(x => x.GetString()).Should().Equal(changed, "changed paths compare content hashes even when length and timestamps match");
    }
    private static Task<McpClient> StartMcp(CancellationToken token) => McpClient.CreateAsync(new StdioClientTransport(new() {
        Command = "dotnet", Arguments = [ProductPath, "mcp", "--local"],
        EnvironmentVariables = new Dictionary<string, string?> { ["CLIO10_SETTINGS"] = null }
    }), cancellationToken: token);

    private static async Task<string> RunCli(string[] arguments, int expectedExitCode, CancellationToken token) {
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        // Preserve runtime-selection overrides so this same proof can run against an unchanged installed host.
        start.Environment.Remove("CLIO10_SETTINGS");
        start.ArgumentList.Add(ProductPath);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try {
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            string output = await stdout;
            string error = await stderr;
            process.ExitCode.Should().Be(expectedExitCode, "CLI exit status distinguishes success, rejection and invalid named arguments");
            if (expectedExitCode == 2)
                error.Should().Contain("Invalid operation arguments", "missing named inputs retain the existing usage-error contract");
            else error.Should().BeEmpty("structured discovery and execution results belong exclusively on stdout");
            return output;
        }
        finally {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
