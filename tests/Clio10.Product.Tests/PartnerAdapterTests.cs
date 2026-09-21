using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
namespace Clio10.Product.Tests;

/// <summary>A custom product exposes partner workflows through unchanged generic adapters.</summary>
public sealed class PartnerAdapterTests {
    private static string Host => typeof(PartnerAdapterTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "PartnerHost").Value!;
    [TestCase(false)]
    [TestCase(true)]
    [Description("CLI and MCP execute a separately compiled filesystem workflow with isolated nested arguments and structured results.")]
    public async Task Partner_workflow_reaches_both_adapters(bool mcp) {
        // Arrange
        string left = Path.GetTempFileName(), right = Path.GetTempFileName();
        await File.WriteAllTextAsync(left, "ab");
        await File.WriteAllTextAsync(right, "cde");
        var arguments = new Dictionary<string, object?> { ["files"] = new Dictionary<string, object?> { ["left"] = left, ["right"] = right } };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try {
            string output;
            // Act
            if (mcp) {
                await using var client = await McpClient.CreateAsync(new StdioClientTransport(new() {
                    Command = "dotnet", Arguments = [Host, "mcp", "--local"]
                }), cancellationToken: timeout.Token);
                var discovery = await client.CallToolAsync("list-operations", cancellationToken: timeout.Token);
                discovery.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("partner.compare-texts", "registered partner operations must be discoverable without adapter edits");
                var result = await client.CallToolAsync("execute", new Dictionary<string, object?> {
                    ["operation"] = "partner.compare-texts", ["arguments"] = arguments
                }, cancellationToken: timeout.Token);
                result.IsError.Should().NotBeTrue("the filesystem workflow does not require Creatio authentication");
                output = result.Content.OfType<TextContentBlock>().Single().Text;
            }
            else {
                var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var argument in new[] { Host, "--execute", "partner.compare-texts", "--local", JsonSerializer.Serialize(arguments) }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                try {
                    var read = process.StandardOutput.ReadToEndAsync(timeout.Token);
                    var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                    await process.WaitForExitAsync(timeout.Token);
                    output = await read;
                    (await errors).Should().BeEmpty("normal structured execution should not log errors");
                    process.ExitCode.Should().Be(0, "registered partner operations use the same CLI dispatcher");
                }
                finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            }
            // Assert
            using var json = JsonDocument.Parse(output);
            var payload = json.RootElement.GetProperty("Payload");
            payload.GetProperty("Left").GetProperty("Label").GetString().Should().Be("left", "the first child gets explicitly replaced arguments");
            payload.GetProperty("Right").GetProperty("Label").GetString().Should().Be("right", "the second child must not inherit the first child's input");
            payload.GetProperty("Left").GetProperty("Characters").GetInt32().Should().Be(2, "the primitive read the first real file");
            payload.GetProperty("Right").GetProperty("Characters").GetInt32().Should().Be(3, "structured values must survive the full result path");
        }
        finally { File.Delete(left); File.Delete(right); }
    }
}
