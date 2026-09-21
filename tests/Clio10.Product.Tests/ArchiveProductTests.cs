using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio10.Product.Tests;

/// <summary>Exercises package archives through the public CLI and one long-running MCP session.</summary>
public sealed class ArchiveProductTests {
    private static string ProductPath => Environment.GetEnvironmentVariable("CLIO10_TEST_PRODUCT") ??
        typeof(ArchiveProductTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "ProductPath").Value!;

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    [Description("CLI and MCP discover archive commands, honor selection rules, and require explicit replacement of an existing package.")]
    public async Task Archive_roundtrip_and_explicit_overwrite(bool mcp, bool batch) {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-product-archive-").FullName;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await using var client = mcp ? await McpClient.CreateAsync(new StdioClientTransport(new() {
            Command = "dotnet", Arguments = [ProductPath, "mcp", "--local"]
        }), cancellationToken: timeout.Token) : null;
        try {
            string source = Directory.CreateDirectory(Path.Combine(root, "packages", "Example")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "Files"));
            await File.WriteAllTextAsync(Path.Combine(source, "descriptor.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(source, "clioignore"), "private.txt");
            await File.WriteAllTextAsync(Path.Combine(source, "Files", "private.txt"), "do not archive");
            await File.WriteAllTextAsync(Path.Combine(source, "Files", "code.pdb"), "debug");
            await File.WriteAllTextAsync(Path.Combine(source, "Files", "code.cs"), "original");
            if (batch) {
                string second = Directory.CreateDirectory(Path.Combine(root, "packages", "Second")).FullName;
                await File.WriteAllTextAsync(Path.Combine(second, "descriptor.json"), "{}");
            }
            string archive = Path.Combine(root, batch ? "Example.zip" : "Example.gz");
            string parent = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            if (client is not null) {
                var discovery = await client.CallToolAsync("list-operations", cancellationToken: timeout.Token);
                discovery.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("generate-pkg-zip",
                    "archive workflows must be discoverable without adding resident MCP tool definitions");
            }
            // Act
            Dictionary<string, object?> packArguments = new() { ["package-path"] = batch ? Path.GetDirectoryName(source) : source, ["destination-path"] = archive, ["skip-pdb"] = true };
            if (batch) packArguments["packages"] = "Example,Second";
            var packed = await Run("generate-pkg-zip", packArguments,
                batch ? ["compress", Path.GetDirectoryName(source)!, "--DestinationPath", archive, "--SkipPdb", "--Packages", "Example,Second"] : ["compress", source, "-d", archive, "-s"]);
            var extracted = await Run("extract-pkg-zip", new() { ["archive-path"] = archive, ["destination-path"] = parent },
                batch ? ["extract", archive] : ["extract", archive, "-d", parent], workingDirectory: parent);
            string content = Path.Combine(parent, "Example", "Files", "code.cs");
            await File.WriteAllTextAsync(content, "locally changed");
            var refused = await Run("extract-pkg-zip", new() { ["archive-path"] = archive, ["destination-path"] = parent },
                ["extract", archive, "-d", parent], rejected: true);
            string preserved = await File.ReadAllTextAsync(content);
            if (batch && OperatingSystem.IsWindows()) {
                using var locked = new FileStream(Path.Combine(parent, "Second", "descriptor.json"), FileMode.Open, FileAccess.Read, FileShare.None);
                var partial = await Run("extract-pkg-zip", new() { ["archive-path"] = archive, ["destination-path"] = parent, ["overwrite"] = true },
                    ["extract", archive, "-d", parent, "--overwrite"], rejected: true);
                partial.GetProperty("AcceptedSteps").EnumerateArray().Select(x => x.GetString()).Should().Equal([Path.Combine(parent, "Example")],
                    "both adapters must preserve the completed package when a Windows lock blocks the next publication");
                partial.GetProperty("Payload").GetProperty("failedDestination").GetString().Should().Be(Path.Combine(parent, "Second"),
                    "the caller needs the exact failed destination to avoid retrying an already completed package");
            }
            var replaced = await Run("extract-pkg-zip", new() { ["archive-path"] = archive, ["destination-path"] = parent, ["overwrite"] = true },
                ["unzip", archive, "-d", parent, "--overwrite"]);
            // Assert
            packed.GetProperty("Payload").GetProperty("files").GetInt32().Should().Be(batch ? 3 : 2, "ignore and skip-PDB selection must survive the adapters");
            extracted.GetProperty("Accepted").GetBoolean().Should().BeTrue("the public surface must extract its own published package");
            refused.GetProperty("Code").GetString().Should().Be("archive-extraction-failed", "implicit replacement is refused for agents and terminal users alike");
            preserved.Should().Be("locally changed", "refusal must leave existing package edits intact");
            replaced.GetProperty("Accepted").GetBoolean().Should().BeTrue("explicit replacement is supported through the same adapter contract");
            string? expectedVersion = Environment.GetEnvironmentVariable("CLIO10_PRIMITIVE_VERSION");
            if (expectedVersion is not null) {
                packed.GetProperty("PrimitiveVersion").GetString().Should().Be(expectedVersion, "packaged runtime verification must identify the selected implementation, not silently use a host fallback");
                replaced.GetProperty("PrimitiveVersion").GetString().Should().Be(expectedVersion, "extraction must use the same explicitly selected compatible runtime");
            }
            (await File.ReadAllTextAsync(content)).Should().Be("original", "the replacement publishes archive contents");
        }
        finally { Directory.Delete(root, recursive: true); }

        async Task<JsonElement> Run(string operation, Dictionary<string, object?> arguments, string[] cliArguments, bool rejected = false, string? workingDirectory = null) {
            string output;
            if (client is not null) {
                var result = await client.CallToolAsync("execute", new Dictionary<string, object?> {
                    ["operation"] = operation, ["arguments"] = arguments
                }, cancellationToken: timeout.Token);
                result.IsError.GetValueOrDefault().Should().Be(rejected, "MCP error classification follows the workflow outcome");
                output = result.Content.OfType<TextContentBlock>().Single().Text;
            }
            else {
                var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
                start.ArgumentList.Add(ProductPath);
                foreach (string argument in cliArguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                try {
                    var read = process.StandardOutput.ReadToEndAsync(timeout.Token);
                    var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                    await process.WaitForExitAsync(timeout.Token);
                    output = await read;
                    process.ExitCode.Should().Be(rejected ? 1 : 0, "CLI exit codes must reflect archive publication");
                    (await errors).Should().BeEmpty("structured operation results belong on stdout");
                }
                finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            }
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
    }
}
