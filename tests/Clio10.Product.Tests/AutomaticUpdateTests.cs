using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio10.Product.Tests;

/// <summary>Proves remote acquisition, in-process activation, pinning and persistent selection over real MCP pipes.</summary>
public sealed partial class AutomaticUpdateTests {
    private static string Product => Environment.GetEnvironmentVariable("CLIO10_TEST_PRODUCT") ??
        typeof(AutomaticUpdateTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "ProductPath").Value!;
    private static string Bundles => typeof(AutomaticUpdateTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "BundleRoot").Value!;
    private static string RuntimeBundles => typeof(AutomaticUpdateTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "RuntimeRoot").Value!;
    private const string PackageId = "clio10.primitivebundle";

    [TestCase(false)]
    [TestCase(true)]
    [Description("Read-only discovery preserves the precise runtime resolution error through either adapter.")]
    public async Task Missing_cache_discovery_reports_resolution_code(bool mcp) {
        // Arrange
        string cache = Path.Combine(Path.GetTempPath(), "clio10-missing-cache-" + Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in mcp ? new[] { Product, "mcp", "--local" } : new[] { Product, "--list" }) start.ArgumentList.Add(argument);
        start.Environment["CLIO10_RUNTIME_COMPOSITION"] = "true"; start.Environment["CLIO10_BUNDLES"] = cache;
        start.Environment.Remove("CLIO10_UPDATE_SOURCE");
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        try {
            // Act
            string output;
            if (mcp) {
                await using var client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream), cancellationToken: timeout.Token);
                output = await Operations(client, timeout.Token);
            } else {
                output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
            }
            // Assert
            output.Should().Contain("bundle-directory-unavailable", "listing failure must preserve the actionable runtime resolution code");
            output.Should().NotContain("outcome-unknown", "read-only discovery did not perform any external operation");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await errors; }
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("Missing runtime configuration exits cleanly without an unhandled MCP startup exception.")]
    public async Task Runtime_configuration_requires_cache(bool mcp) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in mcp ? new[] { Product, "mcp", "--local" } : new[] { Product, "--list" }) start.ArgumentList.Add(argument);
        start.Environment["CLIO10_RUNTIME_COMPOSITION"] = "true";
        start.Environment.Remove("CLIO10_BUNDLES"); start.Environment.Remove("CLIO10_UPDATE_SOURCE");
        using var process = Process.Start(start)!;
        try {
            // Act
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            // Assert
            process.ExitCode.Should().Be(2, "invalid setup is a configuration error before execution starts");
            (await error).Trim().Should().Be("Invalid composition configuration.", "startup must not expose an implementation stack trace");
            (await output).Should().BeEmpty("there is no command outcome to misclassify");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [TestCase("Clio10.Composition.dll")]
    [TestCase("Clio10.PrimitiveContracts.dll")]
    [TestCase("Clio10.PrimitiveContracts.dll", true)]
    [Description("A runtime missing a private dependency cannot silently borrow the host's static implementation or feature interfaces.")]
    public async Task Missing_runtime_dependency_cannot_borrow_host_assembly(string dependency, bool omitManifestAsset = false) {
        // Arrange
        string cache = Path.Combine(Path.GetTempPath(), "clio10-incomplete-runtime-" + Guid.NewGuid());
        CopyBundle("10.0.0.0", Path.Combine(cache, "10.0.0.0"), true);
        CopyBundle("10.1.0.0", Path.Combine(cache, "10.1.0.0"), true);
        File.Delete(Path.Combine(cache, "10.1.0.0", dependency));
        if (omitManifestAsset) {
            string path = Path.Combine(cache, "10.1.0.0", "Clio10.RuntimeFixture.deps.json");
            var dependencies = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            foreach (var target in dependencies["targets"]!.AsObject())
                foreach (var library in target.Value!.AsObject())
                    if (library.Value?["runtime"] is System.Text.Json.Nodes.JsonObject assets)
                        foreach (string name in assets.Select(x => x.Key).Where(x => Path.GetFileName(x) == dependency).ToArray())
                            assets.Remove(name);
            File.WriteAllText(path, dependencies.ToJsonString());
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var creatio = new TestHttpServer(path => Task.FromResult(Reply.Json(path.Contains("Login", StringComparison.Ordinal) ? "{\"Code\":0}" : "{}")));
        using var process = Start(cache, creatio.Address, null, true);
        var errors = process.StandardError.ReadToEndAsync();
        try {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream), cancellationToken: timeout.Token);
            // Act
            var response = await client.CallToolAsync("execute", new Dictionary<string, object?> { ["operation"] = "release-proof" }, cancellationToken: timeout.Token);
            using var document = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().Single().Text);
            var result = document.RootElement;
            // Assert
            if (omitManifestAsset)
                (response.IsError.GetValueOrDefault() || Version(result) == "10.0.0.0").Should().BeTrue(
                    "an undeclared missing dependency must fail at first use or fall back, never execute V2 using the host's feature contracts");
            else
                Version(result).Should().Be("10.0.0.0", "a declared missing dependency must reject V2 before execution and fall back");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await errors; }
    }

    [TestCase("corrupt")]
    [TestCase("incompatible")]
    [TestCase("traversal")]
    [TestCase("interrupted")]
    [TestCase("nuspec-id")]
    [TestCase("nuspec-version")]
    [TestCase("duplicate-entry")]
    [TestCase("download-limit")]
    [TestCase("contracts-version")]
    [Description("A rejected or interrupted update never replaces the working bundle or terminates its MCP server.")]
    public async Task Invalid_update_preserves_working_server(string fault) {
        // Arrange
        string cache = Path.Combine(Path.GetTempPath(), "clio10-rejected-update-" + Guid.NewGuid());
        CopyBundle("10.0.0.0", Path.Combine(cache, "10.0.0.0"));
        byte[] package = fault == "corrupt" ? [1, 2, 3] : fault == "download-limit" ? new byte[65 * 1024 * 1024] : Package("10.1.0.0", fault);
        int downloads = 0;
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var creatio = new TestHttpServer(path => Task.FromResult(Reply.Json(path.Contains("Login", StringComparison.Ordinal) ? "{\"Code\":0}" : "{\"success\":true}")));
        TestHttpServer? feed = null;
        feed = new TestHttpServer(async path => {
            if (path == "/flat/" + PackageId + "/index.json" && Volatile.Read(ref downloads) >= 2) {
                paused.TrySetResult(); await resume.Task.WaitAsync(timeout.Token);
            }
            return path switch {
            "/v3/index.json" => Reply.Json(JsonSerializer.Serialize(new { version = "3.0.0", resources = new[] { new Dictionary<string, string> { ["@id"] = feed!.Address + "flat/", ["@type"] = "PackageBaseAddress/3.0.0" } } })),
            "/flat/" + PackageId + "/index.json" => Reply.Json("{\"versions\":[\"10.0.0\",\"10.1.0\"]}"),
            _ => Download()
            };
        });
        Reply Download() { Interlocked.Increment(ref downloads); return new(package, Truncated: fault == "interrupted"); }
        await using var feedLifetime = feed;
        using var process = Start(cache, creatio.Address, feed.Address + "v3/index.json");
        var errors = process.StandardError.ReadToEndAsync();
        try {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream), cancellationToken: timeout.Token);
            // Act
            await paused.Task.WaitAsync(timeout.Token);
            var result = await Execute(client, timeout.Token);
            // Assert
            Version(result).Should().Be("10.0.0.0", "failed acquisition must leave the last complete bundle usable");
            Directory.Exists(Path.Combine(cache, "10.1.0.0")).Should().BeFalse("invalid or partial packages cannot be published");
            File.Exists(Path.Combine(cache, "escaped.txt")).Should().BeFalse("archive traversal cannot write outside staging");
            Directory.GetDirectories(cache, ".staging-*").Should().BeEmpty("rejected attempts must clean their private staging directories");
            process.HasExited.Should().BeFalse("background failure cannot stop serving existing operations");
        }
        finally {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token); resume.TrySetResult(); await errors;
            if (Path.GetDirectoryName(Path.GetFullPath(cache)) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(cache).StartsWith("clio10-rejected-update-", StringComparison.Ordinal)) {
                try { Directory.Delete(cache, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                    TestContext.Out.WriteLine("Retained temporary cache after file-lock cleanup failure: " + cache);
                }
            }
        }
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, true, false)]
    [Description("The same running MCP process downloads V2 while V1 is executing, then new calls use V2 and a fresh process retains it offline.")]
    public async Task Update_keeps_process_and_pipe_alive_and_persists(bool stallFirstDownload, bool runtimeComposition, bool cacheCollision) {
        // Arrange
        string packageId = runtimeComposition ? "clio10.runtimebundle" : PackageId;
        string cache = Path.Combine(Path.GetTempPath(), "clio10-update-" + Guid.NewGuid());
        CopyBundle("10.0.0.0", Path.Combine(cache, "10.0.0.0"), runtimeComposition);
        if (cacheCollision) CopyBundle("10.1.0.0", Path.Combine(cache, "10.1.0.0"));
        string installed = Path.Combine(cache, cacheCollision ? "10.1.0.0-" + packageId : "10.1.0.0");
        byte[] package = Package("10.1.0.0", runtimeComposition: runtimeComposition);
        int published = 0, downloads = 0, restarts = 0;
        var effects = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var creatio = new TestHttpServer(async path => {
            if (path.Contains("Login", StringComparison.Ordinal)) return Reply.Json("{\"Code\":0}");
            effects.Enqueue(path);
            if (Interlocked.Increment(ref restarts) == 1) { firstStarted.TrySetResult(); await releaseFirst.Task.WaitAsync(timeout.Token); }
            return Reply.Json("{\"success\":true}");
        });
        TestHttpServer? feed = null;
        feed = new TestHttpServer(path => Task.FromResult(path switch {
            "/v3/index.json" => Reply.Json(JsonSerializer.Serialize(new { version = "3.0.0", resources = new[] { new Dictionary<string, string> { ["@id"] = feed!.Address + "flat/", ["@type"] = "PackageBaseAddress/3.0.0" } } })),
            _ when path == "/flat/" + packageId + "/index.json" => Reply.Json(Volatile.Read(ref published) == 0 ? "{\"versions\":[\"10.0.0\"]}" : "{\"versions\":[\"10.0.0\",\"10.1.0\"]}"),
            _ => Download()
        }));
        Reply Download() { int attempt = Interlocked.Increment(ref downloads); return new(package, Stall: stallFirstDownload && attempt == 1); }
        await using var feedLifetime = feed;
        using var process = Start(cache, creatio.Address, feed.Address + "v3/index.json", runtimeComposition);
        var errors = process.StandardError.ReadToEndAsync();
        int originalPid = process.Id;
        try {
            await using (var client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream), cancellationToken: timeout.Token)) {
                // Act
                if (runtimeComposition) (await Operations(client, timeout.Token)).Should().NotContain("runtime-added", "the running initial release must not contain V2's new command");
                var oldCall = Execute(client, timeout.Token, runtimeComposition ? "release-proof" : "restart");
                await firstStarted.Task.WaitAsync(timeout.Token);
                Volatile.Write(ref published, 1);
                await Until(() => Directory.Exists(installed), timeout.Token);
                if (runtimeComposition) (await Operations(client, timeout.Token)).Should().Contain("runtime-added", "discovery must refresh from the downloaded composition without restarting the client");
                var newCall = Execute(client, timeout.Token, runtimeComposition ? "release-proof" : "restart");
                if (runtimeComposition) {
                    await Task.Delay(150, timeout.Token);
                    Volatile.Read(ref restarts).Should().Be(1, "both runtime generations must share Core's target gate while V1 is held");
                }
                releaseFirst.TrySetResult();
                var oldResult = await oldCall; var newResult = await newCall;
                // Assert
                Version(oldResult).Should().Be("10.0.0.0", "the original operation must retain its actual loaded bundle");
                Version(newResult).Should().Be("10.1.0.0", "a later root must discover the downloaded DLL without restarting");
                if (runtimeComposition) {
                    oldResult.GetProperty("Code").GetString().Should().Be("composition-10.0.0.0", "the old workflow must keep its release's implementation");
                    newResult.GetProperty("Code").GetString().Should().Be("composition-10.1.0.0", "new roots must run the downloaded composition");
                    oldResult.GetProperty("Payload").GetProperty("ChildVersion").GetString().Should().Be("10.0.0.0", "children after publication must stay with the original release");
                    effects.Select(path => path.Split('/').Last()).Should().Equal(["RestartApp", "ClearRedisDb", "ClearRedisDb", "RestartApp"], "V1's sequence must finish before V2's changed sequence begins");
                    (await Execute(client, timeout.Token, "runtime-added")).GetProperty("Code").GetString().Should().Be("new-runtime-command", "the existing MCP connection must execute a newly downloaded command");
                }
                process.HasExited.Should().BeFalse("the server hosting the original MCP connection must stay alive");
                process.Id.Should().Be(originalPid, "a replacement server process cannot satisfy this proof");
                if (cacheCollision) File.ReadAllText(Path.Combine(cache, "10.1.0.0", "bundle.json")).Should().NotContain("RuntimeContractVersion", "the already installed primitive-only version must remain immutable");
                downloads.Should().BeGreaterThan(0, "the update must be acquired through the NuGet HTTP path");
                if (stallFirstDownload) downloads.Should().BeGreaterThan(1, "a body timeout must permit a later successful automatic check");
                TestContext.Out.WriteLine($"Runtime composition={runtimeComposition}; same MCP PID {originalPid}: in-flight V1={Version(oldResult)}, later root V2={Version(newResult)}.");
            }
            // Only after proving the live switch, deliberately terminate the host to test persisted selection.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            (await errors).Should().BeEmpty("background updates must preserve clean protocol output and shutdown");
            await feed.DisposeAsync();
            using var fresh = Start(cache, creatio.Address, null, runtimeComposition);
            var freshErrors = fresh.StandardError.ReadToEndAsync();
            try {
                await using (var client = await McpClient.CreateAsync(new StreamClientTransport(fresh.StandardInput.BaseStream, fresh.StandardOutput.BaseStream), cancellationToken: timeout.Token)) {
                    var retained = await Execute(client, timeout.Token, runtimeComposition ? "runtime-added" : "restart");
                    Version(retained).Should().Be("10.1.0.0", "the complete on-disk bundle must survive a fresh launch without a feed");
                    TestContext.Out.WriteLine($"Fresh MCP PID {fresh.Id}: offline retained version={Version(retained)}.");
                }
                fresh.Kill(entireProcessTree: true);
                await fresh.WaitForExitAsync(timeout.Token);
                (await freshErrors).Should().BeEmpty("offline reuse must not require updater connectivity");
            }
            finally { if (!fresh.HasExited) fresh.Kill(entireProcessTree: true); }
        }
        finally {
            releaseFirst.TrySetResult();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            // Dedicated cache intentionally remains as inspectable evidence; never delete a loaded bundle.
            TestContext.Out.WriteLine("Bundle cache: " + cache);
        }
    }
    private static Process Start(string cache, string target, string? source, bool runtimeComposition = false) {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Product, "mcp", target, "test-user", "netcore" }) start.ArgumentList.Add(argument);
        start.Environment["CLIO10_PASSWORD"] = "test-password";
        start.Environment["CLIO10_BUNDLES"] = cache;
        start.Environment["CLIO10_RUNTIME_COMPOSITION"] = runtimeComposition ? "true" : "false";
        start.Environment.Remove("CLIO10_PRIMITIVE_VERSION");
        if (source is not null) {
            start.Environment["CLIO10_UPDATE_SOURCE"] = source;
            start.Environment["CLIO10_UPDATE_PACKAGE"] = runtimeComposition ? "clio10.runtimebundle" : PackageId;
            start.Environment["CLIO10_UPDATE_INTERVAL_SECONDS"] = "0.1";
        } else start.Environment.Remove("CLIO10_UPDATE_SOURCE");
        return Process.Start(start)!;
    }
    private static async Task<JsonElement> Execute(McpClient client, CancellationToken token, string operation = "restart") {
        var response = await client.CallToolAsync("execute", new Dictionary<string, object?> { ["operation"] = operation }, cancellationToken: token);
        response.IsError.GetValueOrDefault().Should().BeFalse("the loopback restart should be accepted on either compatible bundle");
        using var json = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().Single().Text);
        return json.RootElement.Clone();
    }
    private static async Task<string> Operations(McpClient client, CancellationToken token) {
        var result = await client.CallToolAsync("list-operations", cancellationToken: token);
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
    private static string? Version(JsonElement result) => result.GetProperty("PrimitiveVersion").GetString();
    private static async Task Until(Func<bool> condition, CancellationToken token) {
        while (!condition()) await Task.Delay(25, token);
    }
    private static void CopyBundle(string version, string destination, bool runtimeComposition = false) {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(Path.Combine(runtimeComposition ? RuntimeBundles : Bundles, version))) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }
    private static byte[] Package(string version, string? fault = null, bool runtimeComposition = false) {
        string packageId = runtimeComposition ? "clio10.runtimebundle" : PackageId;
        if (fault is null && Environment.GetEnvironmentVariable(runtimeComposition ? "CLIO10_TEST_RUNTIME_PACKAGE" : "CLIO10_TEST_UPDATE_PACKAGE") is { Length: > 0 } realPackage)
            return File.ReadAllBytes(realPackage);
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: true)) {
            using (var writer = new StreamWriter(zip.CreateEntry(packageId + ".nuspec").Open()))
                writer.Write($"<package><metadata><id>{(fault == "nuspec-id" ? "wrong-id" : packageId)}</id><version>{(fault == "nuspec-version" ? "99.0.0" : version)}</version><authors>Clio tests</authors><description>Complete fixture bundle.</description></metadata></package>");
            foreach (string file in Directory.GetFiles(Path.Combine(runtimeComposition ? RuntimeBundles : Bundles, version))) {
                if (fault == "contracts-version" && Path.GetFileName(file) == "Clio10.Contracts.dll") {
                    zip.CreateEntryFromFile(Path.GetFullPath(Path.Combine(RuntimeBundles, "..", "contracts-future", "Clio10.Contracts.dll")), "bundle/Clio10.Contracts.dll");
                } else if (fault == "incompatible" && Path.GetFileName(file) == "bundle.json") {
                    using var writer = new StreamWriter(zip.CreateEntry("bundle/bundle.json").Open());
                    writer.Write(File.ReadAllText(file).Replace("\"ContractVersion\": 2", "\"ContractVersion\": 99", StringComparison.Ordinal));
                } else zip.CreateEntryFromFile(file, "bundle/" + Path.GetFileName(file));
            }
            if (fault == "traversal") {
                using var writer = new StreamWriter(zip.CreateEntry("bundle/../../escaped.txt").Open()); writer.Write("must not escape");
            }
            if (fault == "duplicate-entry") { using var writer = new StreamWriter(zip.CreateEntry("bundle/bundle.json").Open()); writer.Write("{}"); }
        }
        return data.ToArray();
    }

    private sealed record Reply(byte[] Body, int Status = 200, bool Truncated = false, bool Stall = false) {
        public static Reply Json(string body) => new(Encoding.UTF8.GetBytes(body));
    }
    private sealed class TestHttpServer : IAsyncDisposable {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accept;
        private readonly List<Task> _requests = [];
        private int _disposed;
        public string Address { get; }
        public TestHttpServer(Func<string, Task<Reply>> handle) {
            _listener.Start(); Address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _accept = Accept(handle);
        }
        private async Task Accept(Func<string, Task<Reply>> handle) {
            try { while (!_stop.IsCancellationRequested) { var socket = await _listener.AcceptTcpClientAsync(_stop.Token); _requests.Add(Serve(socket, handle)); } }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        private async Task Serve(TcpClient socket, Func<string, Task<Reply>> handle) {
            using (socket) {
                await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                string request = (await reader.ReadLineAsync(_stop.Token))!; int length = 0;
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Split(':')[1]);
                if (length > 0) await reader.ReadBlockAsync(new char[length].AsMemory(), _stop.Token);
                var reply = await handle(request.Split(' ')[1]);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {reply.Status} OK\r\nSet-Cookie: .ASPXAUTH=proof; Path=/\r\nContent-Length: {reply.Body.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                if (reply.Stall) await Task.Delay(Timeout.Infinite, _stop.Token);
                await stream.WriteAsync(reply.Truncated ? reply.Body.AsMemory(0, reply.Body.Length / 2) : reply.Body, _stop.Token);
            }
        }
        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel(); _listener.Stop();
            await _accept;
            try { await Task.WhenAll(_requests); }
            catch (Exception) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }
    }
}
