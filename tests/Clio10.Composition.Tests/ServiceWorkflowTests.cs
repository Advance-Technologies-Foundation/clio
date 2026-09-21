using Clio10.PrimitiveContracts;
using System.Text.Json;
using Clio10.Contracts;
using Clio10.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Behavioral ports run through Composition and real Core, with controlled external responses.</summary>
public sealed partial class ServiceWorkflowTests {
    [TestCase("ping-app", true)]
    [TestCase("call-service", false)]
    [Description("A redirect establishes reachability only for ping; it cannot establish successful service execution.")]
    public async Task Redirect_semantics(string operation, bool accepted) {
        // Arrange
        var primitive = Primitive("");
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(302, ""));
        await using var provider = Build(primitive);
        var arguments = operation == "call-service" ? new Dictionary<string, object?> { ["service-path"] = "rest/Custom/Method" } : new();
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: arguments));
        // Assert
        result.Accepted.Should().Be(accepted, "ping asks about reachability while a service call needs an execution response");
        Requests(primitive).Should().HaveCount(1, "Composition must not follow a redirect to another origin");
    }
    [TestCase(true, "GET", "")]
    [TestCase(false, "POST", "0/ping")]
    [Description("Default ping retains legacy platform routing and does not return the application's HTML to callers.")]
    public async Task Ping_platform_route(bool netcore, string method, string path) {
        // Arrange
        var primitive = Primitive("<html>application page</html>");
        await using var provider = Build(primitive, netcore);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("ping-app"));
        // Assert
        Requests(primitive).Single().RelativePath.Should().Be(path, "legacy .NET ping probes the application root, not the Framework ping service");
        Requests(primitive).Single().Method.Should().Be(method, "the transport differs by platform");
        result.Code.Should().Be("http-reachable", "a page response proves reachability, not full service readiness");
        JsonSerializer.Serialize(result.Payload).Should().NotContain("application page", "page content is not useful diagnostic output");
    }
    [TestCase("build-workspace", "{}", "ServiceModel/WorkspaceExplorerService.svc/Rebuild")]
    [TestCase("build-workspace", "{\"modified-items\":true}", "ServiceModel/WorkspaceExplorerService.svc/Build")]
    [TestCase("generate-source-code", "{}", "ServiceModel/WorkspaceExplorerService.svc/GenerateAllSchemasSources")]
    [TestCase("generate-source-code", "{\"required\":true}", "ServiceModel/WorkspaceExplorerService.svc/GenerateRequiredSchemasSources")]
    [TestCase("generate-source-code", "{\"modified\":true,\"required\":true}", "ServiceModel/WorkspaceExplorerService.svc/GenerateModifiedSchemasSources")]
    [TestCase("generate-source-code", "{\"background\":true,\"modified\":true}", "ServiceModel/WorkspaceExplorerService.svc/GenerateAllSchemasSourcesInBackground")]
    [TestCase("restore-configuration", "{}", "ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup")]
    [TestCase("last-compilation-log", "{}", "api/ConfigurationStatus/GetLastCompilationResult")]
    [Description("Ports preserve the endpoint and precedence policy on both platform route conventions.")]
    public async Task Vendor_routes(string operation, string arguments, string path) {
        foreach (bool netcore in new[] { true, false }) {
            // Arrange
            var primitive = Primitive("{\"success\":true}");
            await using var provider = Build(primitive, netcore);
            // Act
            var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: Arguments(arguments)));
            // Assert
            result.Accepted.Should().BeTrue("the fixture contains the success envelope required by the port");
            Requests(primitive).Single().RelativePath.Should().Be((netcore ? "" : "0/") + path, "routing follows the target platform, not the developer machine");
            primitive.ReceivedCalls().Select(x => x.GetMethodInfo().Name).Should().Equal(new[] { "LoginAsync", "ExecuteAsync" },
                "every workflow authenticates before executing exactly once");
        }
    }

    [TestCase("SELECT", "SelectQuery")]
    [TestCase("insert", "InsertQuery")]
    [TestCase("UPDATE", "UpdateQuery")]
    [TestCase("delete", "DeleteQuery")]
    [Description("DataService preserves the caller-owned query body, including unknown server-owned fields.")]
    public async Task Raw_dataservice(string type, string route) {
        // Arrange
        var primitive = Primitive("{\"success\":true,\"rows\":[]}");
        await using var provider = Build(primitive);
        var body = new Dictionary<string, object?> { ["server-owned-field"] = new object?[] { 42L, "value" } };
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("dataservice", Arguments:
            new Dictionary<string, object?> { ["type"] = type, ["body"] = body }));
        // Assert
        result.Accepted.Should().BeTrue("the endpoint explicitly accepted the request");
        Requests(primitive).Single().Should().Be(new PrimitiveRequest("POST", "DataService/json/SyncReply/" + route, JsonSerializer.Serialize(body)),
            "the port chooses only the route and leaves the server's schema intact");
    }

    [TestCase("{\"success\":false,\"errorInfo\":{\"message\":\"remote-secret\"}}", "service-rejected")]
    [TestCase("{}", "invalid-service-response")]
    [TestCase("<html>proxy</html>", "invalid-service-response")]
    [TestCase("broken-json", "invalid-service-response")]
    [Description("A successful HTTP status cannot disguise a rejected or malformed build response.")]
    public async Task Build_response_failure(string body, string code) {
        // Arrange
        var primitive = Primitive(body);
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("build-workspace"));
        // Assert
        result.Code.Should().Be(code, "the service contract determines success after transport acceptance");
        result.Accepted.Should().BeFalse("an unproven build must not be reported as completed");
        result.Payload.Should().BeNull("remote error prose must not become a trusted diagnostic");
    }

    [TestCase("GET", "odata/Contact", "{\"@odata.context\":\"metadata\",\"success\":false}", true)]
    [TestCase("POST", "rest/Custom/Method", "{\"success\":false}", false)]
    [TestCase("GET", "odata/Contact", "{\"Message\":\"not found\"}", false)]
    [TestCase("POST", "rest/Custom/Method", "{\"Message\":\"OK\"}", true)]
    [TestCase("DELETE", "odata/Contact", "", true)]
    [TestCase("PUT", "rest/Custom/Method", "plain text", true)]
    [TestCase("PATCH", "rest/Custom/Method", "<html>expired session</html>", false)]
    [TestCase("GET", "odata/$metadata", "<?xml version=\"1.0\"?><edmx:Edmx xmlns:edmx=\"http://docs.oasis-open.org/odata/ns/edmx\"/>", true)]
    [TestCase("GET", "rest/Custom/Method", "<!DOCTYPE x SYSTEM \"file:///private\"><x/>", false)]
    [TestCase("GET", "rest/Custom/Method", "<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\">error</html>", false)]
    [Description("Service and OData responses use their own error semantics; writes are sent once.")]
    public async Task Service_response_context(string method, string path, string body, bool accepted) {
        // Arrange
        var primitive = Primitive(body);
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("call-service", Arguments:
            new Dictionary<string, object?> { ["method"] = method, ["service-path"] = path }));
        // Assert
        result.Accepted.Should().Be(accepted, "the same field can mean business data or an error depending on its endpoint");
        Requests(primitive).Should().HaveCount(1, "the adapter must never retry arbitrary writes");
    }

    [TestCase("//other/service")]
    [TestCase("https://other/service")]
    [TestCase("../outside")]
    [TestCase("%2e%2e/outside")]
    [TestCase("rest\\other")]
    [TestCase("rest/x?q=%5C")]
    [TestCase("rest/x?q=/../value")]
    [Description("Invalid service routes fail before login and cannot escape the selected application.")]
    public async Task Invalid_route(string path) {
        // Arrange
        var primitive = Primitive("{}");
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("call-service", Arguments:
            new Dictionary<string, object?> { ["service-path"] = path }));
        // Assert
        result.Code.Should().Be("invalid-arguments", "route validation belongs before network activity");
        primitive.ReceivedCalls().Should().BeEmpty("an invalid route cannot cause authentication or external work");
    }

    [Test]
    [Description("Repeated optional application aliases normalize to one framework prefix.")]
    public async Task Normalizes_application_alias() {
        // Arrange
        var primitive = Primitive("{}");
        await using var provider = Build(primitive, false);
        // Act
        await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("call-service", Arguments:
            new Dictionary<string, object?> { ["service-path"] = "/0/0/rest/Custom/Method" }));
        // Assert
        Requests(primitive).Single().RelativePath.Should().Be("0/rest/Custom/Method", "user input must not duplicate the application alias");
    }

    [TestCase("build-workspace", "outcome-unknown")]
    [TestCase("last-compilation-log", "transport-failure")]
    [Description("An interrupted write is uncertain, whereas an interrupted read reports its transport failure.")]
    public async Task Interrupted_response(string operation, string code) {
        // Arrange
        var primitive = Primitive("{}");
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(null, "", "transport-failure"));
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation));
        // Assert
        result.Code.Should().Be(code, "callers need to distinguish a safe read retry from an uncertain write");
        Requests(primitive).Should().HaveCount(1, "the workflow leaves retry decisions to the caller");
    }

    [Test]
    [Description("Culture comes from the authenticated user's profile, never the system language.")]
    public async Task Profile_culture() {
        // Arrange
        var primitive = Primitive("{\"applicationInfo\":{\"sysValues\":{\"primaryCulture\":\"de-DE\",\"userCulture\":{\"displayValue\":\"en-US\"}}}}");
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("get-user-culture"));
        // Assert
        ((IReadOnlyDictionary<string, object?>)result.Payload!)["culture"].Should().Be("en-US", "the user's profile can differ from the system default");
    }

    [TestCase("call-service", 60000)]
    [TestCase("generate-source-code", 3600000)]
    [TestCase("build-workspace", 100000)]
    [Description("Ported services retain their legacy default request budgets through the primitive contract.")]
    public async Task Default_deadlines(string operation, int timeout) {
        // Arrange
        var primitive = Primitive("{\"success\":true}");
        await using var provider = Build(primitive);
        var arguments = operation == "call-service" ? new Dictionary<string, object?> { ["service-path"] = "rest/Custom/Method" } : new();
        // Act
        await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: arguments));
        // Assert
        Requests(primitive).Single().TimeoutMilliseconds.Should().Be(timeout, "long source generation must not inherit the SDK's shorter default");
    }

    [TestCase(0, false)]
    [TestCase(-1, false)]
    [TestCase(250, true)]
    [Description("Caller request deadlines are validated before network work and forwarded without unit conversion.")]
    public async Task Explicit_deadline(int timeout, bool valid) {
        // Arrange
        var primitive = Primitive("{\"success\":true}");
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("build-workspace", Arguments:
            new Dictionary<string, object?> { ["timeout"] = timeout }));
        // Assert
        result.Accepted.Should().Be(valid, "only positive millisecond deadlines are supported");
        if (valid) Requests(primitive).Single().TimeoutMilliseconds.Should().Be(timeout, "milliseconds are not seconds");
        else primitive.ReceivedCalls().Should().BeEmpty("an invalid budget must fail before authentication");
    }

    [Test]
    [Description("A definite SDK authentication rejection retains its classification even for a write.")]
    public async Task Definite_authentication_failure() {
        // Arrange
        var primitive = Primitive("");
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(null, "", "authentication-rejected"));
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("build-workspace"));
        // Assert
        result.Code.Should().Be("authentication-rejected", "a definite rejection is different from a lost service response");
        Requests(primitive).Should().HaveCount(1, "the caller decides whether to supply new credentials");
    }

    [Test]
    [Description("Package names are filtered case-insensitively and sorted while retaining explicit queried fields.")]
    public async Task Package_catalog() {
        // Arrange
        var primitive = Primitive("{\"success\":true,\"rows\":[{\"Name\":\"ZooPkg\",\"UId\":\"id1\",\"Maintainer\":\"ATF\",\"Version\":\"1\"},{\"Name\":\"other\",\"UId\":\"id2\",\"Maintainer\":\"ATF\",\"Version\":\"1\"},{\"Name\":\"AlphaPkg\",\"UId\":\"id3\",\"Maintainer\":\"ATF\",\"Version\":\"2\"}]}");
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("list-packages", Arguments:
            new Dictionary<string, object?> { ["filter"] = "PKG" }));
        // Assert
        ((IReadOnlyDictionary<string, object?>[])result.Payload!).Select(x => x["Name"]).Should().Equal(new[] { "AlphaPkg", "ZooPkg" },
            "the legacy name filter and ordering are retained in Composition");
        using var query = JsonDocument.Parse(Requests(primitive).Single().Body!);
        query.RootElement.GetProperty("rootSchemaName").GetString().Should().Be("SysPackage", "the catalog cannot query another schema");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("File request bodies support literal variables while explicit inline content takes precedence.")]
    public async Task File_request(bool inline) {
        // Arrange
        var primitive = Primitive("{ \"result\" : 42 }");
        var files = Substitute.For<IFileSystemPrimitive>();
        files.ReadTextAsync("input.json", Arg.Any<CancellationToken>()).Returns("file {{value}}");
        var writer = Substitute.For<IFileWriterPrimitive>();
        await using var provider = Build(primitive, files: files, writer: writer);
        var arguments = new Dictionary<string, object?> { ["service-path"] = "rest/Custom/Method", ["input"] = "input.json",
            ["destination"] = "output.json", ["variables"] = new Dictionary<string, object?> { ["value"] = "$1\\literal" } };
        if (inline) arguments["body"] = "inline {{value}}";
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("call-service", Arguments: arguments));
        // Assert
        result.Accepted.Should().BeTrue("the service and destination write both succeeded");
        Requests(primitive).Single().Body.Should().Be((inline ? "inline " : "file ") + "$1\\literal", "variables are literal replacements, not regex replacement patterns");
        files.ReceivedCalls().Count().Should().Be(inline ? 0 : 1, "inline content wins without reading an unnecessary file");
        writer.ReceivedCalls().Single().GetArguments()[1].Should().Be("{ \"result\" : 42 }", "destination files preserve the raw response, including JSON quoting and whitespace");
    }

    [Test]
    [Description("Rejected service responses never overwrite a destination file.")]
    public async Task Rejection_preserves_destination() {
        // Arrange
        var writer = Substitute.For<IFileWriterPrimitive>();
        await using var provider = Build(Primitive("{\"success\":false}"), writer: writer);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("call-service", Arguments:
            new Dictionary<string, object?> { ["service-path"] = "rest/Custom/Method", ["destination"] = "output.json" }));
        // Assert
        result.Accepted.Should().BeFalse("an HTTP 200 response can still reject an operation");
        writer.ReceivedCalls().Should().BeEmpty("a rejected response must leave the previous destination intact");
    }

    [Test]
    [Description("Local publication failure preserves the known remote acceptance so callers do not blindly repeat writes.")]
    public async Task Output_failure_preserves_receipt() {
        // Arrange
        var writer = Substitute.For<IFileWriterPrimitive>();
        writer.WriteTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("disk failed")));
        var primitive = Primitive("{\"success\":true}");
        await using var provider = Build(primitive, writer: writer);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("call-service", Arguments:
            new Dictionary<string, object?> { ["service-path"] = "rest/Custom/Method", ["destination"] = "output.json" }));
        // Assert
        result.Code.Should().Be("output-write-failed", "remote execution and local publication are different outcomes");
        result.AcceptedSteps.Should().Contain("service-call", "callers must know the remote action already succeeded");
        Requests(primitive).Should().HaveCount(1, "a disk failure must not retry a remote side effect");
    }

    [TestCase("{\"Code\":\"custom-value\"}")]
    [TestCase("{\"value\":1e999}")]
    [Description("Opaque service data remains portable even when fields resemble envelopes or numbers exceed floating point range.")]
    public async Task Opaque_data_is_portable(string body) {
        // Arrange
        await using var provider = Build(Primitive(body));
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("call-service", Arguments:
            new Dictionary<string, object?> { ["service-path"] = "rest/Custom/Method" }));
        // Assert
        result.Accepted.Should().BeTrue("ordinary fields must not throw while probing a different envelope shape");
        Action serialize = () => JsonSerializer.Serialize(result.Payload);
        serialize.Should().NotThrow("portable results cannot contain infinity");
    }

    private static IClioPrimitive Primitive(string body) {
        var primitive = Substitute.For<IClioPrimitive>();
        primitive.LoginAsync(Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(200, "{\"Code\":0}"));
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(200, body));
        primitive.ClearReceivedCalls();
        return primitive;
    }
    private static PrimitiveRequest[] Requests(IClioPrimitive primitive) => primitive.ReceivedCalls()
        .Where(x => x.GetMethodInfo().Name == "ExecuteAsync").Select(x => (PrimitiveRequest)x.GetArguments()[0]!).ToArray();
    private static IReadOnlyDictionary<string, object?> Arguments(string json) {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => (object?)(x.Value.ValueKind == JsonValueKind.True));
    }
    private static ServiceProvider Build(IClioPrimitive primitive, bool netcore = true, IFileSystemPrimitive? files = null, IFileWriterPrimitive? writer = null) {
        var services = new ServiceCollection();
        var session = Substitute.For<IPrimitiveSession>();
        session.GetCapability("http").Returns(primitive);
        session.GetCapability("filesystem").Returns(files ?? Substitute.For<IFileSystemPrimitive>());
        session.GetCapability("file-writer").Returns(writer ?? Substitute.For<IFileWriterPrimitive>());
        var bundle = Substitute.For<IPrimitiveBundle>();
        bundle.Version.Returns(new Version(10, 0, 0, 0));
        bundle.ContractVersion.Returns(2);
        bundle.Capabilities.Returns(new[] { "http", "filesystem", "file-writer" });
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(session);
        services.AddSingleton(bundle);
        services.AddClioComposition(new(new Uri("http://127.0.0.1:1/"), "user", "password", netcore, IncludeDefaultPrimitives: false));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
