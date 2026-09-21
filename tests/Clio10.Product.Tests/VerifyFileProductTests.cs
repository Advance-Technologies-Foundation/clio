using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio10.Product.Tests;

/// <summary>Proves local file verification through the shipped generic CLI and MCP adapters.</summary>
public sealed class VerifyFileProductTests {
    private const string AbcDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private static string ProductPath => Environment.GetEnvironmentVariable("CLIO10_TEST_PRODUCT") ??
        typeof(VerifyFileProductTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "ProductPath").Value!;

    /// <summary>Checks operation discovery and typed metadata on both public surfaces.</summary>
    [TestCase(false)]
    [TestCase(true)]
    [Description("CLI and MCP discover the read-only verify-file operation and both required string arguments through existing generic surfaces.")]
    public async Task Discovery_exposes_verification_metadata(bool mcp) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = mcp ? await StartMcp(timeout.Token) : null;

        // Act
        string catalog;
        if (client is not null) {
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            tools.Select(x => x.Name).Should().BeEquivalentTo(new[] { "list-operations", "execute" },
                "a new workflow must use the existing bridge without adding a resident MCP tool");
            var response = await client.CallToolAsync("list-operations", cancellationToken: timeout.Token);
            response.IsError.GetValueOrDefault().Should().BeFalse("metadata discovery must succeed without a target or file");
            catalog = response.Content.OfType<TextContentBlock>().Single().Text;
        }
        else catalog = await RunCli(["--list"], 0, timeout.Token);
        using var operations = JsonDocument.Parse(catalog);
        var descriptor = operations.RootElement.EnumerateArray().Single(x => x.GetProperty("Name").GetString() == "verify-file");

        // Assert
        AssertDescriptor(descriptor);
        if (!mcp) {
            using var help = JsonDocument.Parse(await RunCli(["verify-file", "--help"], 0, timeout.Token));
            AssertDescriptor(help.RootElement);
        }
    }

    /// <summary>Exercises real files and every principal verification outcome through both adapters.</summary>
    [TestCase(false)]
    [TestCase(true)]
    [Description("Real CLI and MCP verification preserve known and empty digests, mismatch receipts, and stable invalid-input and missing-file classifications.")]
    public async Task Verification_outcomes_survive_adapters(bool mcp) {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-product-verify-").FullName;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try {
            string path = Path.Combine(root, "file with spaces.bin");
            string empty = Path.Combine(root, "empty.bin");
            string missing = Path.Combine(root, "missing.bin");
            byte[] contents = [0x61, 0x62, 0x63];
            await File.WriteAllBytesAsync(path, contents, timeout.Token);
            await File.WriteAllBytesAsync(empty, [], timeout.Token);
            await using var client = mcp ? await StartMcp(timeout.Token) : null;

            // Act
            var known = await Run(path, AbcDigest.ToUpperInvariant(), "completed");
            var emptyResult = await Run(empty, EmptyDigest, "completed");
            var mismatch = await Run(path, EmptyDigest, "hash-mismatch");
            await Run(missing, AbcDigest, "file-not-found");
            await Run(Path.Combine(root, "absent-directory", "file.bin"), AbcDigest, "file-not-found");
            foreach (string invalid in new[] { "", " ", "abc", new string('g', 64), " " + AbcDigest, AbcDigest + " " })
                await Run(path, invalid, "invalid-arguments");
            await Run(path, null, "invalid-arguments");
            await Run(" ", AbcDigest, "invalid-arguments");

            // Assert
            AssertHash(known, AbcDigest, 3, true);
            AssertHash(emptyResult, EmptyDigest, 0, true);
            AssertHash(mismatch, AbcDigest, 3, false);
            (await File.ReadAllBytesAsync(path, timeout.Token)).Should().Equal(contents,
                "verification and rejected inputs must leave the source bytes unchanged");
            new FileInfo(empty).Length.Should().Be(0, "empty-file verification must not add content");
            File.Exists(missing).Should().BeFalse("hashing must not create a missing source file");

            async Task<JsonElement> Run(string file, string? digest, string code) {
                var arguments = new Dictionary<string, object?> { ["path"] = file };
                if (digest is not null) arguments["sha256"] = digest;
                bool rejected = code != "completed";
                string output;
                if (client is not null) {
                    var result = await client.CallToolAsync("execute", new Dictionary<string, object?> {
                        ["operation"] = "verify-file", ["arguments"] = arguments
                    }, cancellationToken: timeout.Token);
                    result.IsError.GetValueOrDefault().Should().Be(rejected,
                        "MCP error classification must reflect whether verification was accepted");
                    output = result.Content.OfType<TextContentBlock>().Single().Text;
                }
                else {
                    // Missing required fields use the JSON entry point to reach Composition validation.
                    string[] options = digest is null
                        ? ["--execute", "verify-file", "--local", JsonSerializer.Serialize(arguments)]
                        : ["verify-file", "--path", file, "--sha256", digest];
                    output = await RunCli(options, rejected ? 1 : 0, timeout.Token);
                }
                using var document = JsonDocument.Parse(output);
                var outcome = document.RootElement;
                outcome.GetProperty("Accepted").GetBoolean().Should().Be(!rejected,
                    "only a matching digest is accepted by the public operation");
                outcome.GetProperty("Code").GetString().Should().Be(code,
                    "both adapters must preserve the stable workflow failure category");
                return outcome.Clone();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Preserves the conventional CLI's required-option validation contract.</summary>
    [Test]
    [Description("The conventional CLI rejects a missing required sha256 flag with its existing usage-error exit code before execution.")]
    public async Task Missing_named_digest_is_a_cli_usage_error() {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-product-verify-input-").FullName;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try {
            string path = Path.Combine(root, "source.bin");
            await File.WriteAllBytesAsync(path, [0x61, 0x62, 0x63], timeout.Token);

            // Act
            string output = await RunCli(["verify-file", "--path", path], 2, timeout.Token);

            // Assert
            output.Should().BeEmpty("schema binding rejects incomplete named options before any workflow result exists");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AssertDescriptor(JsonElement descriptor) {
        descriptor.GetProperty("Name").GetString().Should().Be("verify-file", "discovery must use the frozen operation identity");
        descriptor.GetProperty("Destructive").GetBoolean().Should().BeFalse("verification only reads local content");
        var arguments = descriptor.GetProperty("Arguments").EnumerateArray().ToArray();
        arguments.Select(x => x.GetProperty("Name").GetString()).Should().BeEquivalentTo(new[] { "path", "sha256" },
            "the generic adapters need exactly the source path and expected digest");
        foreach (var argument in arguments) {
            argument.GetProperty("Kind").GetString().Should().Be("String", "paths and digests must be supplied as strings");
            argument.GetProperty("Required").GetBoolean().Should().BeTrue("both verification inputs are mandatory");
        }
    }

    private static void AssertHash(JsonElement result, string digest, long bytes, bool matches) {
        var payload = result.GetProperty("Payload");
        payload.GetProperty("sha256").GetString().Should().Be(digest, "the actual lowercase SHA-256 must survive result serialization");
        payload.GetProperty("bytes").GetInt64().Should().Be(bytes, "the receipt must report the actual number of bytes read");
        payload.GetProperty("matches").GetBoolean().Should().Be(matches, "mismatch receipts must retain the comparison outcome");
        string? version = result.GetProperty("PrimitiveVersion").GetString();
        version.Should().NotBeNullOrEmpty("the result must identify its pinned primitive runtime");
        if (Environment.GetEnvironmentVariable("CLIO10_PRIMITIVE_VERSION") is { } expectedVersion)
            version.Should().Be(expectedVersion, "installed-host verification must execute the explicitly selected complete runtime");
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
