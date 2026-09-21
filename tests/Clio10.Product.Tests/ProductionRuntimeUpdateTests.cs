using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio10.Product.Tests;

public sealed partial class AutomaticUpdateTests {
    [Test, Explicit("Requires CLIO10_TEST_PRODUCT, CLIO10_TEST_RELEASE_BASELINE and CLIO10_TEST_RELEASE_PACKAGE artifacts.")]
    [Description("An installed old host discovers a real newly ported command after background NuGet acquisition and uses it without replacing its MCP connection.")]
    public async Task Production_runtime_adds_package_file_command() {
        // Arrange
        string baseline = Environment.GetEnvironmentVariable("CLIO10_TEST_RELEASE_BASELINE")!;
        string packagePath = Environment.GetEnvironmentVariable("CLIO10_TEST_RELEASE_PACKAGE")!;
        string cache = Path.Combine(Path.GetTempPath(), "clio10-production-update-" + Guid.NewGuid());
        string initial = Path.Combine(cache, "10.8.0.0");
        Directory.CreateDirectory(initial);
        foreach (string file in Directory.GetFiles(baseline)) File.Copy(file, Path.Combine(initial, Path.GetFileName(file)));
        byte[] package = await File.ReadAllBytesAsync(packagePath);
        string hostHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Product)));
        int available = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var target = new TestHttpServer(path => Task.FromResult(Reply.Json(
            path.Contains("Login", StringComparison.Ordinal) ? "{\"Code\":0}" :
            path == "/rest/CreatioApiGateway/GetPackageFilesDirectoryContent?packageName=Pkg" ? "[\"Files/source.cs\"]" :
            path == "/rest/CreatioApiGateway/GetPackageFileContent?packageName=Pkg&filePath=Files%2Fsource.cs" ? "\"public class Demo {}\"" :
            "{\"success\":false}")));
        TestHttpServer? feed = null;
        feed = new TestHttpServer(path => Task.FromResult(path switch {
            "/v3/index.json" => Reply.Json(JsonSerializer.Serialize(new { resources = new[] { new Dictionary<string, string> {
                ["@id"] = feed!.Address + "flat/", ["@type"] = "PackageBaseAddress/3.0.0" } } })),
            "/flat/clio10.runtimebundle/index.json" => Reply.Json(Volatile.Read(ref available) == 0 ?
                "{\"versions\":[\"10.8.0\"]}" : "{\"versions\":[\"10.8.0\",\"10.9.0\"]}"),
            "/flat/clio10.runtimebundle/10.9.0/clio10.runtimebundle.10.9.0.nupkg" => new Reply(package),
            _ => new Reply([], 404)
        }));
        await using var feedLifetime = feed;
        using var process = Start(cache, target.Address, feed.Address + "v3/index.json", true);
        var errors = process.StandardError.ReadToEndAsync();
        try {
            await using (var client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream), cancellationToken: timeout.Token)) {
                (await Operations(client, timeout.Token)).Should().NotContain("show-package-file-content", "the old runtime must establish the feature was absent before update");
                // Act
                Volatile.Write(ref available, 1);
                await Until(() => Directory.Exists(Path.Combine(cache, "10.9.0.0")), timeout.Token);
                string catalog = await Operations(client, timeout.Token);
                var list = await PackageFiles(client, false, timeout.Token);
                var content = await PackageFiles(client, true, timeout.Token);
                // Assert
                catalog.Should().Contain("show-package-file-content", "the existing discovery tool must describe the newly downloaded command");
                AssertPackageFiles(list, content);
                process.HasExited.Should().BeFalse("the same MCP client and pipes must survive runtime activation");
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Product))).Should().Be(hostHash, "the installed host must remain unchanged");
                TestContext.Out.WriteLine($"Production runtime 10.8 -> 10.9, same MCP process {process.Id}, host SHA256 {hostHash}, cache {cache}");
            }
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token); await errors; }
        await feed.DisposeAsync();
        using var offline = Start(cache, target.Address, null, true);
        var offlineErrors = offline.StandardError.ReadToEndAsync();
        try {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(offline.StandardInput.BaseStream, offline.StandardOutput.BaseStream), cancellationToken: timeout.Token);
            AssertPackageFiles(await PackageFiles(client, false, timeout.Token), await PackageFiles(client, true, timeout.Token));
        }
        finally { if (!offline.HasExited) offline.Kill(entireProcessTree: true); await offline.WaitForExitAsync(timeout.Token); await offlineErrors; }
    }
    private static async Task<JsonElement> PackageFiles(McpClient client, bool read, CancellationToken token) {
        var arguments = new Dictionary<string, object?> { ["package"] = "Pkg" };
        if (read) arguments["file"] = "Files/source.cs";
        var response = await client.CallToolAsync("execute", new Dictionary<string, object?> {
            ["operation"] = "show-package-file-content", ["arguments"] = arguments
        }, cancellationToken: token);
        response.IsError.GetValueOrDefault().Should().BeFalse("the actual port must accept the loopback gate response");
        using var document = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().Single().Text);
        return document.RootElement.Clone();
    }
    private static void AssertPackageFiles(JsonElement list, JsonElement content) {
        Version(list).Should().Be("10.9.0.0", "the old installed host must route to the new production runtime");
        Version(content).Should().Be("10.9.0.0", "the read operation must also run in the new release");
        list.GetProperty("Payload").GetProperty("files")[0].GetString().Should().Be("Files/source.cs", "listing must cross the full layered HTTP path");
        content.GetProperty("Payload").GetProperty("content").GetString().Should().Be("public class Demo {}", "file contents must cross the runtime boundary as portable data");
    }
}
