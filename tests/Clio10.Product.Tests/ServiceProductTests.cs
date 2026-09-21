using System.Diagnostics;
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

/// <summary>Exercises ported workflows through the product, adapters, Core and actual SDK HTTP transport.</summary>
public sealed class ServiceProductTests {
    private static string ProductPath => Environment.GetEnvironmentVariable("CLIO10_TEST_PRODUCT") ??
        typeof(ServiceProductTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "ProductPath").Value!;

    [TestCase("build-workspace", "{\"modified-items\":true}", "ServiceModel/WorkspaceExplorerService.svc/Build", "{\"success\":true}", false)]
    [TestCase("generate-source-code", "{\"required\":true}", "ServiceModel/WorkspaceExplorerService.svc/GenerateRequiredSchemasSources", "{\"success\":true}", false)]
    [TestCase("call-service", "{\"method\":\"PATCH\",\"service-path\":\"rest/Custom/Method\",\"body\":\"{}\"}", "rest/Custom/Method", "{\"success\":false}", true)]
    [TestCase("get-user-culture", "{}", "ServiceModel/ApplicationInfoService.svc/GetApplicationInfo", "{\"applicationInfo\":{\"sysValues\":{\"userCulture\":{\"displayValue\":\"en-US\"}}}}", false)]
    [TestCase("last-compilation-log", "{}", "api/ConfigurationStatus/GetLastCompilationResult", "{\"errors\":[]}", false)]
    [TestCase("show-package-file-content", "{\"package\":\"Pkg\"}", "rest/CreatioApiGateway/GetPackageFilesDirectoryContent?packageName=Pkg", "[\"Files/source.cs\"]", false)]
    [TestCase("show-package-file-content", "{\"package\":\"Pkg\",\"file\":\"Files/source.cs\"}", "rest/CreatioApiGateway/GetPackageFileContent?packageName=Pkg&filePath=Files%2Fsource.cs", "\"source\"", false)]
    [TestCase("ping-app", "{}", "GET / HTTP/1.1", "<html>application</html>", false)]
    [TestCase("call-service", "{\"method\":\"GET\",\"service-path\":\"odata/$metadata\"}", "odata/$metadata", "<edmx:Edmx xmlns:edmx=\"http://docs.oasis-open.org/odata/ns/edmx\"/>", false)]
    [TestCase("activate-pkg", "{\"package-name\":\"Custom\"}", "ServiceModel/PackageService.svc/ActivatePackage", "{\"success\":true,\"packagesActivationResults\":[{\"packageName\":\"Custom\",\"success\":true}]}", false)]
    [TestCase("activate-pkg", "{\"package-name\":\"Custom\"}", "ServiceModel/PackageService.svc/ActivatePackage", "{\"success\":true,\"packagesActivationResults\":[{\"packageName\":\"Custom\",\"success\":false}]}", true)]
    [Description("Stable MCP discovery and execute tools route migrated operations through the real SDK transport.")]
    public async Task Mcp_service_port(string operation, string arguments, string endpoint, string response, bool error) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string target = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var requests = new List<string>();
        var server = ServeAsync(listener, requests, response, timeout.Token);
        var transport = new StdioClientTransport(new() {
            Command = "dotnet", Arguments = [ProductPath, "mcp", target, "test-user", "netcore"],
            EnvironmentVariables = new Dictionary<string, string?> { ["CLIO10_PASSWORD"] = "test-password" }
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        using var json = JsonDocument.Parse(arguments);
        // Act
        var catalog = await client.CallToolAsync("list-operations", cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("execute", new Dictionary<string, object?> {
            ["operation"] = operation, ["arguments"] = json.RootElement.Clone()
        }, cancellationToken: timeout.Token);
        await server;
        // Assert
        catalog.Content.OfType<TextContentBlock>().Single().Text.Should().Contain(operation, "ported operations must be discoverable through the stable tool");
        result.IsError.Should().Be(error, "Composition's interpretation must survive the MCP adapter");
        requests.Should().HaveCount(2, "the product logs in once and dispatches exactly once");
        requests[1].Should().Contain(endpoint, "the real SDK must receive the correct migrated service route");
        if (operation == "get-user-culture") result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("en-US", "structured profile data reaches the caller");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("The conventional CLI verb uses a named environment or direct credentials without primitive knowledge.")]
    public async Task Named_cli(bool settings) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string target = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var requests = new List<string>();
        var server = ServeAsync(listener, requests, "{\"success\":true}", timeout.Token);
        var path = Path.Combine(Path.GetTempPath(), "clio10-cli-" + Guid.NewGuid().ToString("N") + ".json");
        var start = Start();
        start.ArgumentList.Add("compile");
        if (settings) {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { lab = new { BaseUri = target, UserName = "test-user", Password = "test-password", IsNetCore = false } }), timeout.Token);
            foreach (string argument in new[] { "-e", "lab", "--settings", path }) start.ArgumentList.Add(argument);
        }
        else foreach (string argument in new[] { "--uri", target, "--login", "test-user", "--platform", "framework" }) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--modified-items");
        using var process = Process.Start(start)!;
        try {
            // Act
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            await server;
            // Assert
            process.ExitCode.Should().Be(0, "a successful migrated compile is a successful CLI operation");
            output.Should().Contain("completed", "the CLI presents the structured result from Composition");
            output.Should().NotContain("test-password", "connection secrets are never presentation data");
            error.Should().BeEmpty("valid named options must be accepted");
            requests[1].Should().Contain("/0/ServiceModel/WorkspaceExplorerService.svc/Build", "Core resolves the selected target's platform");
        }
        finally {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase("build-workspace", "--unknown")]
    [TestCase("build-workspace", "--modified-items=banana")]
    [TestCase("dataservice", "--body=not-json")]
    [Description("Invalid named options fail locally with exit code two, without authenticating.")]
    public async Task Invalid_named_options(string operation, string option) {
        // Arrange
        var start = Start();
        start.ArgumentList.Add(operation);
        start.ArgumentList.Add(option);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try {
            // Act
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await output;
            await error;
            // Assert
            process.ExitCode.Should().Be(2, "the adapter validates named flags using the discovered schema before execution");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Test]
    [Description("Packaged CLI aliases and positional package paths execute both metadata operations without an environment.")]
    public async Task Package_metadata_cli() {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("clio10-package-cli-");
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "descriptor.json"), "{\"Descriptor\":{\"UId\":\"keep-me\",\"PackageVersion\":\"1.0\"}}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try {
            foreach (var arguments in new[] { new[] { "spv", directory.FullName, "-v", "2.3.4" }, new[] { "gpv", directory.FullName } }) {
                var start = Start();
                start.Environment.Remove("CLIO10_PASSWORD");
                foreach (string argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                try {
                    // Act
                    var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                    var error = process.StandardError.ReadToEndAsync(timeout.Token);
                    await process.WaitForExitAsync(timeout.Token);
                    // Assert
                    process.ExitCode.Should().Be(0, "local package operations must not demand an HTTP target");
                    (await output).Should().Contain("2.3.4", "both the edit and subsequent read return the requested version");
                    (await error).Should().BeEmpty("documented aliases and positional syntax are valid");
                }
                finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestCase(true, "add-package-dependency")]
    [TestCase(false, "add-package-dependency")]
    [TestCase(true, "deactivate-pkg")]
    [TestCase(false, "deactivate-pkg")]
    [Description("Both MCP and a legacy CLI alias execute a multi-step package workflow through the real HTTP SDK.")]
    public async Task Package_dependency_adapters(bool mcp, string operation) {
        // Arrange
        const string targetId = "11111111-1111-1111-1111-111111111111";
        const string dependencyId = "22222222-2222-2222-2222-222222222222";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string target = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var requests = new List<string>();
        bool dependencyEdit = operation == "add-package-dependency";
        var responses = new List<string> {
            "{\"Code\":0}",
            JsonSerializer.Serialize(new { success = true, rows = new[] {
                new { Name = "Custom", UId = targetId, Maintainer = "ATF", Version = "1.0" },
                new { Name = "Base", UId = dependencyId, Maintainer = "ATF", Version = "1.0" } } }),
        };
        if (dependencyEdit) responses.Add(JsonSerializer.Serialize(new { success = true, package = new { uId = targetId, name = "Custom", description = "retain", dependsOnPackages = Array.Empty<object>() } }));
        responses.Add("{\"success\":true,\"compilationRequired\":true}");
        var server = ServeResponsesAsync(listener, requests, responses.ToArray(), timeout.Token);
        string output;
        // Act
        if (mcp) {
            var transport = new StdioClientTransport(new() {
                Command = "dotnet", Arguments = [ProductPath, "mcp", target, "test-user", "netcore"],
                EnvironmentVariables = new Dictionary<string, string?> { ["CLIO10_PASSWORD"] = "test-password" }
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
            var arguments = new Dictionary<string, object?> { ["package-name"] = "Custom" };
            if (dependencyEdit) arguments["dependencies"] = "Base";
            var result = await client.CallToolAsync("execute", new Dictionary<string, object?> {
                ["operation"] = operation, ["arguments"] = arguments
            }, cancellationToken: timeout.Token);
            output = result.Content.OfType<TextContentBlock>().Single().Text;
        }
        else {
            var start = Start();
            string[] command = dependencyEdit ? ["add-pkg-dep", "--package-name", "Custom", "--dependencies", "Base"] : ["dpkg", "Custom"];
            foreach (string argument in command.Concat(["--uri", target, "--login", "test-user"])) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                output = await stdout;
                (await stderr).Should().BeEmpty("the documented alias and options must parse");
                process.ExitCode.Should().Be(0, "the complete package save was accepted");
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        await server;
        // Assert
        output.Should().Contain(dependencyEdit ? "save-package-properties" : "deactivate-package", "the accepted mutation receipt must reach either adapter");
        requests.Should().HaveCount(dependencyEdit ? 4 : 3, "only dependency editing needs to read a full descriptor before mutation");
        requests[^1].Should().Contain(dependencyEdit ? "SavePackageProperties" : "DeactivatePackage", "the public surface must reach the real SDK request");
    }

    private static ProcessStartInfo Start() {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(ProductPath);
        start.Environment["CLIO10_PASSWORD"] = "test-password";
        foreach (string name in new[] { "CLIO10_SETTINGS", "CLIO10_BUNDLES", "CLIO10_RUNTIME_COMPOSITION", "CLIO10_PRIMITIVE_VERSION" }) start.Environment.Remove(name);
        return start;
    }
    private static Task ServeAsync(TcpListener listener, List<string> requests, string body, CancellationToken token) =>
        ServeResponsesAsync(listener, requests, ["{\"Code\":0}", body], token);
    private static async Task ServeResponsesAsync(TcpListener listener, List<string> requests, string[] bodies, CancellationToken token) {
        for (int index = 0; index < bodies.Length; index++) {
            using var socket = await listener.AcceptTcpClientAsync(token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
            var headers = new StringBuilder();
            int length = 0;
            while (await reader.ReadLineAsync(token) is { Length: > 0 } line) {
                headers.AppendLine(line);
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Split(':')[1]);
            }
            if (length > 0) await reader.ReadBlockAsync(new char[length].AsMemory(), token);
            requests.Add(headers.ToString());
            var payload = Encoding.UTF8.GetBytes(bodies[index]);
            var header = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nSet-Cookie: .ASPXAUTH=probe-session; Path=/\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(payload, token);
        }
    }
}
