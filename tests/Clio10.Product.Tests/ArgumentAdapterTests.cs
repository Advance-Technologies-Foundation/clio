using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
namespace Clio10.Product.Tests;

/// <summary>Exercises JSON conversion in actual CLI and MCP processes.</summary>
public sealed class ArgumentAdapterTests {
    private static string Host => typeof(ArgumentAdapterTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "PartnerHost").Value!;

    [TestCase(false)]
    [TestCase(true)]
    [Description("Decimal input preserves precision through both generic adapters.")]
    public async Task Decimal_roundtrip(bool mcp) {
        // Arrange
        const decimal expected = 0.1234567890123456789012345678m;
        // Act
        var result = await Run(mcp, "test.echo", "{\"number\":0.1234567890123456789012345678}");
        // Assert
        result.GetProperty("Payload").GetProperty("number").GetDecimal().Should().Be(expected, "JSON must preserve supported decimal precision");
    }

    [TestCase(false, "{\"number\":1e400}")]
    [TestCase(true, "{\"number\":1e400}")]
    [TestCase(false, "{\"nested\":{\"x\":1,\"x\":2}}")]
    [TestCase(true, "{\"nested\":{\"x\":1,\"x\":2}}")]
    [TestCase(false, "{\"number\":1,\"number\":2}")]
    [TestCase(true, "{\"number\":1,\"number\":2}")]
    [Description("Invalid numeric or duplicate-key JSON fails at the adapter boundary without running an operation.")]
    public async Task Malformed_portable_input(bool mcp, string input) {
        // Arrange
        const string operation = "test.echo";
        // Act
        var result = await Run(mcp, operation, input, invalidJson: true);
        // Assert
        result.GetProperty("Code").GetString().Should().Be("invalid-arguments", "input conversion failures need a consistent failure category");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("An empty file path returns a structured domain error through either adapter.")]
    public async Task Invalid_file_path(bool mcp) {
        // Arrange
        const string input = "{\"path\":\"\",\"label\":\"sample\"}";
        // Act
        var result = await Run(mcp, "partner.inspect-text", input, rejected: true);
        // Assert
        result.GetProperty("Code").GetString().Should().Be("invalid-path", "bad file input must not crash the host");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("Unexpected workflow failures have a safe unknown-outcome response without exception details.")]
    public async Task Workflow_exception_is_bounded(bool mcp) {
        // Arrange
        const string operation = "test.throw";
        // Act
        var result = await Run(mcp, operation, "{}", rejected: true);
        // Assert
        result.GetProperty("Code").GetString().Should().Be("unexpected-failure", "composition identifies an unexpected failure during execution without claiming transport knowledge");
        result.GetRawText().Should().NotContain("test-secret", "unexpected exception details may contain credentials");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("Serialization failure preserves the completed operation receipt and explicitly forbids automatic retry.")]
    public async Task Serialization_failure_preserves_receipt(bool mcp) {
        // Arrange
        const string operation = "test.invalid-payload";
        // Act
        var result = await Run(mcp, operation, "{}");
        // Assert
        result.GetProperty("Accepted").GetBoolean().Should().BeTrue("reporting failure cannot undo known acceptance");
        result.GetProperty("Code").GetString().Should().Be("completed", "the known outcome must survive serialization failure");
        result.GetProperty("ReportingError").GetString().Should().Be("result-serialization-failed", "clients must distinguish execution from reporting");
        result.GetProperty("AcceptedSteps")[0].GetString().Should().Be("completed-step", "partial receipts remain valuable when payload encoding fails");
        result.GetProperty("RetryAdvice").GetString().Should().Be("do-not-retry-automatically", "repeating an accepted operation may duplicate its effects");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("A broken extension fails at activation without disabling metadata discovery or healthy operations.")]
    public async Task Broken_constructor_is_isolated(bool mcp) {
        // Arrange
        const string operation = "test.broken";
        // Act
        var result = await Run(mcp, operation, "{}", rejected: true);
        var healthy = await Run(mcp, "test.echo", "{}");
        // Assert
        result.GetProperty("Code").GetString().Should().Be("workflow-unavailable", "activation failed before entering the workflow body");
        healthy.GetProperty("Accepted").GetBoolean().Should().BeTrue("unrelated registrations must remain usable");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("Accidentally embedding a known environment record must not serialize its password.")]
    public async Task Environment_password_is_redacted(bool mcp) {
        // Arrange
        const string operation = "test.environment";
        // Act
        var result = await Run(mcp, operation, "{}");
        // Assert
        result.GetRawText().Should().NotContain("test-secret", "adapter presentation must redact known credential records");
        result.GetProperty("Payload").GetProperty("Environment").TryGetProperty("Password", out _).Should().BeFalse("passwords are omitted from environment output");
    }

    private static async Task<JsonElement> Run(bool mcp, string operation, string input, bool invalidJson = false, bool rejected = false) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string output;
        if (mcp) {
            await using var client = await McpClient.CreateAsync(new StdioClientTransport(new() {
                Command = "dotnet", Arguments = [Host, "mcp", "--local"]
            }), cancellationToken: timeout.Token);
            var discovery = await client.CallToolAsync("list-operations", cancellationToken: timeout.Token);
            discovery.IsError.GetValueOrDefault().Should().BeFalse("discovery must not activate broken workflow constructors");
            using var arguments = JsonDocument.Parse(input);
            var result = await client.CallToolAsync("execute", new Dictionary<string, object?> {
                ["operation"] = operation, ["arguments"] = arguments.RootElement
            }, cancellationToken: timeout.Token);
            result.IsError.GetValueOrDefault().Should().Be(invalidJson || rejected, "MCP must classify rejected execution as an error");
            output = result.Content.OfType<TextContentBlock>().Single().Text;
        }
        else {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { Host, "--execute", operation, "--local", input }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try {
                var read = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                output = await read;
                process.ExitCode.Should().Be(invalidJson ? 2 : rejected ? 1 : 0, "CLI distinguishes malformed input from workflow rejection");
                if (invalidJson) {
                    (await errors).Should().Contain("Invalid arguments JSON", "malformed input is reported without exception details");
                    output = "{\"Code\":\"invalid-arguments\"}";
                }
                else (await errors).Should().BeEmpty("results belong on the structured output channel");
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }
}
