using Clio10.PrimitiveContracts;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Clio10.Contracts;
using Clio10.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Exercises actual local HTTP without contacting Creatio.</summary>
public sealed class TransportTests {
    [TestCase(Operation.Restart, "RestartApp")]
    [TestCase(Operation.FlushRedis, "ClearRedisDb")]
    [Description("Real HTTP transport carries login cookies and CSRF into the composed operation.")]
    public async Task Session_flows_through_real_transport(string operation, string endpoint) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new List<string>();
        var server = ServeAsync(listener, requests, timeout.Token);
        var services = new ServiceCollection();
        services.AddClioComposition(new(new Uri($"http://127.0.0.1:{port}/"), "local-test", "local-test"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        // Act
        var result = await scope.ServiceProvider.GetRequiredService<IClioComposition>()
            .ExecuteAsync(new(operation), cancellationToken: timeout.Token);
        await server;
        // Assert
        result.Code.Should().Be("http-accepted", "both local HTTP responses were received");
        requests[1].Should().Contain($"/ServiceModel/AppInstallerService.svc/{endpoint}", "composition selects the operation endpoint");
        requests[1].Should().Contain("BPMCSRF: probe-token", "the transport must forward the CSRF cookie as a header");
        requests[1].Should().Contain("Cookie:", "authentication cookies must survive between primitive calls");
        requests[1].Should().Contain(".ASPXAUTH=probe-session", "the operation uses the authenticated session");
    }

    [TestCase(503, "{}", 200, "authentication-http-failure")]
    [TestCase(200, "<html>proxy</html>", 200, "authentication-rejected")]
    [TestCase(200, "{\"Code\":0}", 500, "operation-http-failure")]
    [Description("Real SDK responses retain structured failure semantics for login and operation errors.")]
    public async Task Sdk_failure_responses_are_bounded(int loginStatus, string loginBody, int operationStatus, string expected) {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var requests = new List<string>();
        var server = ServeAsync(listener, requests, timeout.Token, loginStatus, loginBody, operationStatus);
        var services = new ServiceCollection();
        services.AddClioComposition(new(new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"), "test", "test"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        // Act
        var result = await scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new(Operation.Restart), cancellationToken: timeout.Token);
        await server;
        // Assert
        result.Code.Should().Be(expected, "SDK failures must preserve the composition result boundary");
        if (operationStatus == 500) result.Response.Should().Be("{}", "received operation responses must be retained");
        requests.Count.Should().Be(loginStatus == 200 && loginBody.Contains("Code") ? 2 : 1, "failed authentication must prevent dispatch");
    }
    [Test]
    [Description("Nested operations reuse initial authentication within the pinned HTTP session.")]
    public async Task Session_authenticates_once() {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var requests = new List<string>();
        var server = ServeAsync(listener, requests, timeout.Token);
        var services = new ServiceCollection();
        services.AddClioComposition(new(new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"), "test", "test"));
        await using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<Clio10.Core.IClioCore>().RunAsync("default",
            new PrimitiveRequirement(new(10,0,0,0), new(11,0,0,0), Capabilities: ["http"]), async (context, token) => {
                var http = context.Get<IClioPrimitive>();
                await http.LoginAsync(token);
                await http.LoginAsync(token);
                return await http.ExecuteAsync(new("POST", "operation", "{}"), token);
            }, timeout.Token);
        await server;
        // Assert
        result.StatusCode.Should().Be(200, "the authenticated primitive remains usable after repeated ensure calls");
        requests.Count(x => x.Contains("AuthService.svc/Login")).Should().Be(1, "initial authentication belongs to the session rather than each nested step");
    }
    private static async Task ServeAsync(TcpListener listener, List<string> requests, CancellationToken token, int loginStatus = 200, string loginBody = "{\"Code\":0}", int operationStatus = 200) {
        for (int i = 0; i < (loginStatus == 200 && loginBody.Contains("Code") ? 2 : 1); i++) {
            using var socket = await listener.AcceptTcpClientAsync(token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var header = new StringBuilder();
            int length = 0;
            while (await reader.ReadLineAsync(token) is { Length: > 0 } line) {
                header.AppendLine(line);
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    length = int.Parse(line.Split(':')[1]);
            }
            if (length > 0) {
                var body = new char[length];
                await reader.ReadBlockAsync(body.AsMemory(), token);
            }
            requests.Add(header.ToString());
            string bodyText = i == 0 ? loginBody : "{}";
            string cookies = i == 0 ? "Set-Cookie: BPMCSRF=probe-token; Path=/\r\nSet-Cookie: .ASPXAUTH=probe-session; Path=/\r\n" : "";
            byte[] response = Encoding.UTF8.GetBytes($"HTTP/1.1 {(i == 0 ? loginStatus : operationStatus)} Response\r\n{cookies}Content-Length: {bodyText.Length}\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n{bodyText}");
            await stream.WriteAsync(response, token);
        }
    }
}
