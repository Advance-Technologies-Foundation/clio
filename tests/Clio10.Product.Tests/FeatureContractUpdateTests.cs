using System.Text.Json;
using System.Security.Cryptography;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio10.Product.Tests;

public sealed partial class AutomaticUpdateTests {
    [Test]
    [Description("One unchanged host acquires a new capability assembly then a breaking revision, preserving its active old workflow and offline selection.")]
    public async Task New_and_changed_capability_interfaces_update_without_host_restart() {
        // Arrange
        string cache = Path.Combine(Path.GetTempPath(), "clio10-feature-evolution-" + Guid.NewGuid());
        CopyBundle("10.0.0.0", Path.Combine(cache, "10.0.0.0"), true);
        string hostDirectory = Path.GetDirectoryName(Product)!;
        File.Exists(Path.Combine(hostDirectory, "Clio10.FeatureFixture.dll")).Should().BeFalse("the host must never ship or reference the capability used by this proof");
        File.Exists(Path.Combine(cache, "10.0.0.0", "Clio10.FeatureFixture.dll")).Should().BeFalse("V1 must not know the future capability either");
        string hostHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Product)));
        var packages = new Dictionary<string, byte[]> { ["10.1.0.0"] = Package("10.1.0.0", runtimeComposition: true), ["10.2.0.0"] = Package("10.2.0.0", runtimeComposition: true) };
        int generation = 0, firstRequest = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var creatio = new TestHttpServer(async path => {
            if (path.Contains("Login", StringComparison.Ordinal)) return Reply.Json("{\"Code\":0}");
            if (Interlocked.Increment(ref firstRequest) == 1) { entered.TrySetResult(); await finish.Task.WaitAsync(timeout.Token); }
            return Reply.Json("{\"success\":true}");
        });
        const string id = "clio10.runtimebundle";
        TestHttpServer? feed = null;
        feed = new TestHttpServer(path => Task.FromResult(path switch {
            "/v3/index.json" => Reply.Json(JsonSerializer.Serialize(new { resources = new[] { new Dictionary<string, string> { ["@id"] = feed!.Address + "flat/", ["@type"] = "PackageBaseAddress/3.0.0" } } })),
            "/flat/" + id + "/index.json" => Reply.Json(JsonSerializer.Serialize(new { versions = Volatile.Read(ref generation) switch {
                0 => new[] { "10.0.0.0" }, 1 => new[] { "10.0.0.0", "10.1.0.0" }, _ => new[] { "10.0.0.0", "10.1.0.0", "10.2.0.0" } } })),
            _ => new Reply(packages[path.Split('/')[3]])
        }));
        await using var feedLifetime = feed;
        using var process = Start(cache, creatio.Address, feed.Address + "v3/index.json", true);
        int pid = process.Id;
        var errors = process.StandardError.ReadToEndAsync();
        try {
            await using (var client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream), cancellationToken: timeout.Token)) {
                // Act
                var oldCall = Execute(client, timeout.Token, "release-proof");
                await entered.Task.WaitAsync(timeout.Token);
                Volatile.Write(ref generation, 1);
                await Until(() => Directory.Exists(Path.Combine(cache, "10.1.0.0")), timeout.Token);
                (await Operations(client, timeout.Token)).Should().Contain("runtime-file", "the new release must expose its genuinely new capability workflow");
                var v2Call = ExecuteFile(client, Path.Combine(cache, "v2.txt"), timeout.Token);
                finish.TrySetResult();
                var old = await oldCall;
                var v2 = await v2Call;
                Volatile.Write(ref generation, 2);
                await Until(() => Directory.Exists(Path.Combine(cache, "10.2.0.0")), timeout.Token);
                var v3 = await ExecuteFile(client, Path.Combine(cache, "v3.txt"), timeout.Token);
                // Assert
                Version(old).Should().Be("10.0.0.0", "an active workflow and its children must finish on their original release");
                AssertFileResult(v2, "10.1.0.0", "2.0.0.0", "v2-new-capability");
                AssertFileResult(v3, "10.2.0.0", "3.0.0.0", "v3-new-signature");
                (await File.ReadAllTextAsync(Path.Combine(cache, "v2.txt"))).Should().Be("v2-new-capability", "V2 must execute real primitive I/O");
                (await File.ReadAllTextAsync(Path.Combine(cache, "v3.txt"))).Should().Be("v3-new-signature", "V3 must execute the changed API rather than reuse V2");
                process.Id.Should().Be(pid, "both updates must retain the same MCP process and connection");
                process.HasExited.Should().BeFalse("the host must still be serving after both updates");
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Product))).Should().Be(hostHash, "the executable must not have been rebuilt or replaced during either update");
                TestContext.Out.WriteLine($"Same MCP PID {pid}: V1 pinned, new capability V2, breaking capability V3 succeeded; host SHA256 {hostHash}.");
            }
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            (await errors).Should().BeEmpty("successful updates must not report infrastructure errors");
            await feed.DisposeAsync();
            using var offline = Start(cache, creatio.Address, null, true);
            var offlineErrors = offline.StandardError.ReadToEndAsync();
            try {
                await using var client = await McpClient.CreateAsync(new StreamClientTransport(offline.StandardInput.BaseStream, offline.StandardOutput.BaseStream), cancellationToken: timeout.Token);
                AssertFileResult(await ExecuteFile(client, Path.Combine(cache, "offline.txt"), timeout.Token), "10.2.0.0", "3.0.0.0", "v3-new-signature");
            }
            finally { if (!offline.HasExited) offline.Kill(entireProcessTree: true); await offline.WaitForExitAsync(timeout.Token); await offlineErrors; }
        }
        finally {
            finish.TrySetResult();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            TestContext.Out.WriteLine("Inspectable feature update cache: " + cache);
        }
    }
    private static async Task<JsonElement> ExecuteFile(McpClient client, string path, CancellationToken token) {
        var response = await client.CallToolAsync("execute", new Dictionary<string, object?> {
            ["operation"] = "runtime-file", ["arguments"] = new Dictionary<string, object?> { ["path"] = path }
        }, cancellationToken: token);
        using var result = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().Single().Text);
        return result.RootElement.Clone();
    }
    private static void AssertFileResult(JsonElement result, string runtime, string contract, string text) {
        result.GetProperty("Accepted").GetBoolean().Should().BeTrue("the release-private capability must successfully execute");
        Version(result).Should().Be(runtime, "the selected runtime must own this call");
        result.GetProperty("Payload").GetProperty("contractVersion").GetString().Should().Be(contract, "each runtime must load its own incompatible interface assembly");
        result.GetProperty("Payload").GetProperty("text").GetString().Should().Be(text, "the primitive response must return through ordinary composition results");
    }
}
