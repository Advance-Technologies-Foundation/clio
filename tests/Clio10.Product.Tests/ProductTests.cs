using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio10.Product.Tests;

/// <summary>Exercises the shipping entry point, adapters, composition and transport together.</summary>
public sealed class ProductTests {
    private static string ProductPath => Environment.GetEnvironmentVariable("CLIO10_TEST_PRODUCT") ??
        typeof(ProductTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "ProductPath").Value!;

    [TestCase("restart", false, "RestartApp")]
    [TestCase("flush-redis", false, "ClearRedisDb")]
    [TestCase("restart", true, "")]
    [Description("A real MCP client discovers and invokes the product's tools against an isolated local target.")]
    public async Task Mcp_round_trip(string tool, bool rejectLogin, string endpoint) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string target = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var requests = new List<string>();
        var server = ServeAsync(listener, requests, rejectLogin, timeout.Token);
        var transport = new StdioClientTransport(new() {
            Command = "dotnet",
            Arguments = [ProductPath, "mcp", target, "test-user", "netcore"],
            EnvironmentVariables = new Dictionary<string, string?> { ["CLIO10_PASSWORD"] = "test-password" }
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        // Act
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("execute", new Dictionary<string, object?> { ["operation"] = tool }, cancellationToken: timeout.Token);
        await server;
        // Assert
        tools.Select(x => x.Name).Should().BeEquivalentTo(new[] { "list-operations", "execute" }, "the MCP adapter owns both command shapes");
        result.IsError.Should().Be(rejectLogin, "composition failure must be surfaced as an MCP error");
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain(
            rejectLogin ? "authentication-rejected" : "http-accepted", "MCP must preserve the structured outcome");
        requests.Count.Should().Be(rejectLogin ? 1 : 2, "failed authentication must prevent the destructive request");
        if (!rejectLogin) requests[1].Should().Contain(endpoint, "the selected tool must reach its matching workflow");
    }

    [TestCase("restart")]
    [TestCase("flush-redis")]
    [Description("The product delegates CLI command parsing and reports the real composed HTTP result.")]
    public async Task Cli_round_trip(string command) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string target = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var requests = new List<string>();
        var server = ServeAsync(listener, requests, false, timeout.Token);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { ProductPath, "--execute", command, target, "test-user", "netcore" }) start.ArgumentList.Add(arg);
        start.Environment["CLIO10_PASSWORD"] = "test-password";
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
            process.ExitCode.Should().Be(0, "HTTP acceptance is the CLI success condition in this prototype");
            output.Should().Contain("http-accepted", "the client must serialize composition's result");
            error.Should().BeEmpty("the successful path should not produce errors");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static async Task ServeAsync(TcpListener listener, List<string> requests, bool rejectLogin, CancellationToken token) {
        for (int i = 0; i < (rejectLogin ? 1 : 2); i++) {
            using var socket = await listener.AcceptTcpClientAsync(token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var headers = new StringBuilder();
            int length = 0;
            while (await reader.ReadLineAsync(token) is { Length: > 0 } line) {
                headers.AppendLine(line);
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Split(':')[1]);
            }
            if (length > 0) await reader.ReadBlockAsync(new char[length].AsMemory(), token);
            requests.Add(headers.ToString());
            string body = i == 0 ? (rejectLogin ? "{\"Code\":1}" : "{\"Code\":0}") : "{}";
            byte[] response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nSet-Cookie: .ASPXAUTH=probe-session; Path=/\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(response, token);
        }
    }
}
