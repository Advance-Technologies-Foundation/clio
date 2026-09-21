using Clio10.PrimitiveContracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio10.Composition;
using Clio10.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Exercises package edits through Composition and Core with controlled service responses.</summary>
public sealed class PackageDependencyTests {
    private const string Target = "11111111-1111-1111-1111-111111111111";
    private const string Dependency = "22222222-2222-2222-2222-222222222222";
    private static string Properties(string dependencies = "[]") => $$$$"""{"success":true,"package":{"uId":"{{{{Target}}}}","name":"Custom","dependsOnPackages":{{{{dependencies}}}},"description":"Сохранить","installBehavior":3,"unknown":{"precise":123456789012345678901234567890}}} """;

    [TestCase(true)]
    [TestCase(false)]
    [Description("Adding dependencies preserves the complete descriptor, uses bare service bodies and keeps one identity and bundle.")]
    public async Task Add_preserves_properties(bool netcore) {
        // Arrange
        var primitive = Primitive(Properties());
        await using var provider = Build(primitive, netcore);
        // Act
        var result = await Run(provider, "add-package-dependency", "base:2.5,Base:9.0");
        // Assert
        result.Accepted.Should().BeTrue("both names resolve to the same installed dependency identity");
        var requests = Requests(primitive);
        requests.Should().HaveCount(3, "one catalog read, property read and save complete the workflow");
        requests[1].Body.Should().Be(JsonSerializer.Serialize(Target), "GetPackageProperties expects a bare quoted UId");
        requests[2].RelativePath.Should().Be((netcore ? "" : "0/") + "ServiceModel/PackageService.svc/SavePackageProperties", "the target controls its application alias");
        var saved = JsonNode.Parse(requests[2].Body!)!;
        saved["description"]!.GetValue<string>().Should().Be("Сохранить", "a dependency edit must not erase unrelated properties");
        saved["unknown"]!["precise"]!.ToJsonString().Should().Be("123456789012345678901234567890", "opaque server numbers must survive without floating point rounding");
        saved["dependsOnPackages"]!.AsArray().Should().HaveCount(1, "dependency identity prevents duplicates in the same request");
        saved["dependsOnPackages"]![0]!["version"]!.GetValue<string>().Should().Be("2.5", "the first explicit version is used for a new dependency");
        primitive.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "LoginAsync").Should().Be(1, "the child and parent share the same authenticated session");
        result.AcceptedSteps.Should().Contain("save-package-properties", "callers need a receipt for the accepted mutation");
        JsonSerializer.Serialize(result.Payload).Should().Contain("compilationRequired", "the save result tells callers whether compilation is needed");
    }

    [TestCase("remove-package-dependency", "Missing", 2)]
    [TestCase("remove-package-dependency", "BASE", 3)]
    [TestCase("add-package-dependency", "Base:9.0", 3)]
    [Description("Removal is case-insensitive and avoids no-op writes; adding an existing UId preserves its metadata.")]
    public async Task Existing_dependency(string operation, string names, int calls) {
        // Arrange
        var primitive = Primitive(Properties($$"""[{"uId":"{{Dependency}}","name":"Base","version":"1.0","custom":true}]"""));
        await using var provider = Build(primitive);
        // Act
        var result = await Run(provider, operation, names);
        // Assert
        result.Accepted.Should().BeTrue("already present or absent names have defined no-op behavior");
        Requests(primitive).Should().HaveCount(calls, "only a changed removal should save");
        if (operation.StartsWith("add", StringComparison.Ordinal)) {
            var saved = JsonNode.Parse(Requests(primitive)[^1].Body!)!;
            saved["dependsOnPackages"]![0]!["version"]!.GetValue<string>().Should().Be("1.0", "an existing dependency is not silently upgraded");
            saved["dependsOnPackages"]![0]!["custom"]!.GetValue<bool>().Should().BeTrue("existing dependency metadata belongs to the server");
        }
    }

    [TestCase("Missing", "dependency-not-found", 1)]
    [TestCase(":bad", "invalid-arguments", 0)]
    [TestCase(" , ", "invalid-arguments", 0)]
    [Description("Invalid input or a missing dependency never reaches package persistence.")]
    public async Task Invalid_dependency(string names, string code, int calls) {
        // Arrange
        var primitive = Primitive(Properties());
        await using var provider = Build(primitive);
        // Act
        var result = await Run(provider, "add-package-dependency", names);
        // Assert
        result.Code.Should().Be(code, "input and catalog failures must be explicit");
        Requests(primitive).Should().HaveCount(calls, "validation must stop before a write");
    }

    [TestCase("{\"success\":true,\"package\":null}")]
    [TestCase("{\"success\":true,\"package\":{\"uId\":\"22222222-2222-2222-2222-222222222222\"}}")]
    [TestCase("{\"success\":true,\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"dependsOnPackages\":[null]}}")]
    [Description("Malformed or mismatched package properties cannot overwrite an unrelated package.")]
    public async Task Invalid_properties(string body) {
        // Arrange
        var primitive = Primitive(body);
        await using var provider = Build(primitive);
        // Act
        var result = await Run(provider, "add-package-dependency", "Base");
        // Assert
        result.Code.Should().Be("invalid-package-response", "an envelope alone does not validate the editable package");
        Requests(primitive).Should().HaveCount(2, "the workflow must stop before persistence");
    }

    [TestCase(false, "service-rejected")]
    [TestCase(true, "outcome-unknown")]
    [Description("A rejected or interrupted save is not reported as success and is never retried.")]
    public async Task Failed_save(bool interrupted, string code) {
        // Arrange
        var primitive = Primitive(Properties(), interrupted ? new(null, "", "transport-failure") : new(200, "{\"success\":false}"));
        await using var provider = Build(primitive);
        // Act
        var result = await Run(provider, "add-package-dependency", "Base");
        // Assert
        result.Code.Should().Be(code, "a lost write response is different from a known rejection");
        Requests(primitive).Should().HaveCount(3, "uncertain writes must not be replayed by composition");
    }

    [TestCase(true)]
    [TestCase(false)]
    [Description("Deactivation resolves a name and posts its UId rather than the package name.")]
    public async Task Deactivate_uses_identity(bool netcore) {
        // Arrange
        var primitive = Primitive(Properties());
        await using var provider = Build(primitive, netcore);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("deactivate-pkg", Arguments:
            new Dictionary<string, object?> { ["package-name"] = "Custom" }));
        // Assert
        result.Accepted.Should().BeTrue("the package catalog resolves a valid installed identity");
        Requests(primitive).Should().HaveCount(2, "deactivation needs only the catalog and mutation after login");
        Requests(primitive)[1].Body.Should().Be(JsonSerializer.Serialize(Target), "the platform expects a quoted UId");
        Requests(primitive)[1].RelativePath.Should().Be((netcore ? "" : "0/") + "ServiceModel/PackageService.svc/DeactivatePackage", "the application's platform determines the alias");
    }

    [TestCase("{\"success\":true,\"packagesActivationResults\":[{\"success\":true,\"packageName\":\"Base\"}]}", true, "completed", 1)]
    [TestCase("{\"success\":true,\"packagesActivationResults\":[{\"success\":true,\"packageName\":\"Base\"},{\"success\":false,\"packageName\":\"Custom\"}]}", false, "package-activation-failed", 1)]
    [TestCase("{\"success\":true}", false, "invalid-activation-response", 0)]
    [TestCase("{\"success\":true,\"packagesActivationResults\":[{\"success\":true,\"packageName\":\"Base\"},null]}", false, "invalid-activation-response", 1)]
    [Description("Activation uses a quoted name and exposes per-package failures despite a successful outer envelope.")]
    public async Task Activation_results(string body, bool accepted, string code, int receipts) {
        // Arrange
        var primitive = Primitive(Properties(), new(200, body));
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("activate-pkg", Arguments:
            new Dictionary<string, object?> { ["package-name"] = "Custom" }));
        // Assert
        result.Accepted.Should().Be(accepted, "every reported activation must succeed before reporting complete success");
        result.Code.Should().Be(code, "partial activation is a useful business outcome rather than a console warning");
        (result.AcceptedSteps?.Count ?? 0).Should().Be(receipts, "already accepted packages must remain visible after another package fails");
        Requests(primitive).Single().Body.Should().Be("\"Custom\"", "ActivatePackage expects a name, unlike DeactivatePackage");
    }

    [TestCase("add-package-dependency")]
    [TestCase("remove-package-dependency")]
    [TestCase("deactivate-pkg")]
    [Description("A missing target package stops after the catalog read, before any package service call.")]
    public async Task Missing_target(string operation) {
        // Arrange
        var primitive = Primitive(Properties());
        await using var provider = Build(primitive);
        var arguments = new Dictionary<string, object?> { ["package-name"] = "Absent" };
        if (operation != "deactivate-pkg") arguments["dependencies"] = "Base";
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: arguments));
        // Assert
        result.Code.Should().Be("package-not-found", "a missing target is different from a missing dependency");
        Requests(primitive).Should().HaveCount(1, "there is no valid target for any subsequent read or write");
    }

    [Test]
    [Description("Framework activation uses its application alias while preserving the name-based request body.")]
    public async Task Framework_activation() {
        // Arrange
        var primitive = Primitive(Properties(), new(200, "{\"success\":true,\"packagesActivationResults\":[{\"success\":true,\"packageName\":\"Custom\"}]}"));
        await using var provider = Build(primitive, false);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("activate-pkg", Arguments:
            new Dictionary<string, object?> { ["package-name"] = "Custom" }));
        // Assert
        result.Accepted.Should().BeTrue("the package activation result establishes acceptance");
        Requests(primitive).Single().RelativePath.Should().Be("0/ServiceModel/PackageService.svc/ActivatePackage", "Framework services use the application alias");
    }

    private static Task<OperationResult> Run(ServiceProvider provider, string operation, string names) =>
        provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: new Dictionary<string, object?> {
            ["package-name"] = "custom", ["dependencies"] = names
        }));
    private static PrimitiveRequest[] Requests(IClioPrimitive primitive) => primitive.ReceivedCalls()
        .Where(x => x.GetMethodInfo().Name == "ExecuteAsync").Select(x => (PrimitiveRequest)x.GetArguments()[0]!).ToArray();
    private static IClioPrimitive Primitive(string properties, PrimitiveResponse? save = null) {
        var primitive = Substitute.For<IClioPrimitive>();
        primitive.LoginAsync(Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(200, "{\"Code\":0}"));
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(call => {
            var request = call.Arg<PrimitiveRequest>()!;
            return request.RelativePath.EndsWith("SelectQuery", StringComparison.Ordinal)
                ? new(200, $$"""{"success":true,"rows":[{"Name":"Custom","UId":"{{Target}}","Maintainer":"ATF","Version":"1.0"},{"Name":"Base","UId":"{{Dependency}}","Maintainer":"ATF","Version":"7.0"}]}""")
                : request.RelativePath.EndsWith("GetPackageProperties", StringComparison.Ordinal) ? new(200, properties)
                : save ?? new PrimitiveResponse(200, "{\"success\":true,\"compilationRequired\":true}");
        });
        primitive.ClearReceivedCalls();
        return primitive;
    }
    private static ServiceProvider Build(IClioPrimitive primitive, bool netcore = true) {
        var services = new ServiceCollection();
        var session = Substitute.For<IPrimitiveSession>();
        session.GetCapability("http").Returns(primitive);
        var bundle = Substitute.For<IPrimitiveBundle>();
        bundle.Version.Returns(new Version(10, 0, 0, 0));
        bundle.ContractVersion.Returns(2);
        bundle.Capabilities.Returns(new[] { "http" });
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(session);
        services.AddSingleton(bundle);
        services.AddClioComposition(new(new Uri("http://127.0.0.1:1/"), "user", "password", netcore, IncludeDefaultPrimitives: false));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
