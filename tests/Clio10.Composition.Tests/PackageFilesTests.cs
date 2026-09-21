using System.Text.Json;
using Clio10.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Tests;

public sealed partial class ServiceWorkflowTests {
    [TestCase(true)]
    [TestCase(false)]
    [Description("Package listing retains platform routing, query escaping, path normalization and deterministic ordering.")]
    public async Task Package_file_listing(bool netcore) {
        // Arrange
        var primitive = Primitive("[\"z.txt\",\"/Files\\\\A.cs\"]");
        await using var provider = Build(primitive, netcore);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("show-package-file-content",
            Arguments: new Dictionary<string, object?> { ["package"] = " A&B " }));
        // Assert
        result.Accepted.Should().BeTrue("a JSON string array satisfies the listing contract");
        Requests(primitive).Single().RelativePath.Should().Be((netcore ? "" : "0/") +
            "rest/CreatioApiGateway/GetPackageFilesDirectoryContent?packageName=A%26B", "query values cannot become URL syntax");
        JsonSerializer.Serialize(result.Payload).Should().Be("{\"package-name\":\"A\\u0026B\",\"files\":[\"Files/A.cs\",\"z.txt\"],\"count\":2}",
            "callers receive portable, normalized structured results");
    }
    [Test]
    [Description("Reading a file preserves its text and never performs an unsolicited project-file read.")]
    public async Task Package_file_read() {
        // Arrange
        var primitive = Primitive(JsonSerializer.Serialize("café 🧪"));
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("show-package-file-content",
            Arguments: new Dictionary<string, object?> { ["package"] = "Pkg", ["file"] = "src\\a #.cs" }));
        // Assert
        Requests(primitive).Single().RelativePath.Should().EndWith("filePath=src%2Fa%20%23.cs", "package-relative paths must be query-encoded");
        result.Accepted.Should().BeTrue("JSON text is the file response contract");
        ((IReadOnlyDictionary<string, object?>)result.Payload!)["content"].Should().Be("café 🧪", "file content must be preserved verbatim");
        ((IReadOnlyDictionary<string, object?>)result.Payload!)["content-length"].Should().Be(7, "the legacy field counts UTF-16 code units, not encoded bytes");
    }
    [TestCase("")]
    [TestCase("  ")]
    [Description("Blank file arguments retain the legacy listing behavior rather than requesting a nameless file.")]
    public async Task Package_file_blank_lists(string file) {
        // Arrange
        var primitive = Primitive("[]");
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("show-package-file-content",
            Arguments: new Dictionary<string, object?> { ["package"] = "Pkg", ["file"] = file }));
        // Assert
        result.Accepted.Should().BeTrue("legacy blank file input means list package files");
        Requests(primitive).Single().RelativePath.Should().Contain("GetPackageFilesDirectoryContent", "blank input must not request file contents");
    }
    [TestCase("../secret")]
    [TestCase("C:\\secret")]
    [TestCase("/secret")]
    [TestCase("a/./b")]
    [Description("Unsafe package paths fail before authentication or service calls.")]
    public async Task Package_file_path_validation(string file) {
        // Arrange
        var primitive = Primitive("\"text\"");
        await using var provider = Build(primitive);
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("show-package-file-content",
            Arguments: new Dictionary<string, object?> { ["package"] = "Pkg", ["file"] = file }));
        // Assert
        result.Code.Should().Be("invalid-arguments", "paths must remain package-relative on every OS");
        Requests(primitive).Should().BeEmpty("validation precedes I/O");
    }
    [TestCase("{}", false)]
    [TestCase("[null]", false)]
    [TestCase("\"text\"", false)]
    [TestCase("[]", true)]
    [TestCase("null", true)]
    [Description("Listing and content response shapes cannot be substituted for each other.")]
    public async Task Package_file_response_validation(string body, bool read) {
        // Arrange
        var primitive = Primitive(body);
        await using var provider = Build(primitive);
        var arguments = new Dictionary<string, object?> { ["package"] = "Pkg" };
        if (read) arguments["file"] = "file.txt";
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("show-package-file-content", Arguments: arguments));
        // Assert
        result.Code.Should().Be("invalid-service-response", "HTTP success alone cannot establish the expected data contract");
        result.Accepted.Should().BeFalse("invalid data is a failure");
    }
}
