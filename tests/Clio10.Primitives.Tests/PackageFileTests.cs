using Clio10.PrimitiveContracts;
using System.Text.Json;
using Clio10.Composition;
using Clio10.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Primitives.Tests;

/// <summary>Tests package file changes through the default composition and actual filesystem primitives.</summary>
public sealed class PackageFileTests {
    [TestCase("1.2.3.4-rc", "1.2.3.4-rc")]
    [TestCase(" 1.2.3 ", "1.2.3")]
    [Description("Package version and install timestamp move together while unrelated metadata is preserved.")]
    public async Task Updates_descriptor_and_reads_it_back(string input, string expected) {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("clio10-metadata-");
        var path = Path.Combine(directory.FullName, "descriptor.json");
        const string uid = "73c47d28-7d4e-47a2-b3a6-ed149b79fdf8";
        const long oldTimestamp = 1900000000000;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Descriptor = new {
            UId = uid, Name = "MyPackage", Description = "Пример été <demo> & value", PackageVersion = "1.0", ModifiedOnUtc = $"/Date({oldTimestamp})/", PartnerMetadata = new { Keep = true } } }));
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        try {
            // Act
            var composition = provider.GetRequiredService<IClioComposition>();
            var result = await composition.ExecuteAsync(new("set-pkg-version", Arguments: new Dictionary<string, object?> {
                ["package-path"] = directory.FullName, ["package-version"] = input
            }));
            var read = await composition.ExecuteAsync(new("get-pkg-version", Arguments: new Dictionary<string, object?> { ["package-path"] = directory.FullName }));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var descriptor = document.RootElement.GetProperty("Descriptor");
            // Assert
            result.Accepted.Should().BeTrue("the local workflow should require neither a URL nor Creatio credentials");
            ((IReadOnlyDictionary<string, object?>)read.Payload!)["packageVersion"].Should().Be(expected, "the next invocation reads the persisted version");
            descriptor.GetProperty("UId").GetString().Should().Be(uid, "version edits must not change package identity");
            descriptor.GetProperty("PartnerMetadata").GetProperty("Keep").GetBoolean().Should().BeTrue("unknown partner metadata survives the edit");
            descriptor.GetProperty("ModifiedOnUtc").GetString().Should().NotBe($"/Date({oldTimestamp})/", "Creatio only rewrites its recorded version when this timestamp changes");
            Directory.GetFiles(directory.FullName, ".clio-write-*").Should().BeEmpty("successful publication cleans its staging file");
            (await File.ReadAllTextAsync(path)).Should().Contain("Пример été <demo> & value", "localized metadata should remain readable in the descriptor file");
        }
        finally { directory.Delete(true); }
    }

    [TestCase("bad-version")]
    [TestCase("")]
    [Description("Invalid versions do not alter the existing package descriptor.")]
    public async Task Invalid_version_preserves_file(string version) {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("clio10-invalid-version-");
        var path = Path.Combine(directory.FullName, "descriptor.json");
        const string original = "{\"Descriptor\":{\"PackageVersion\":\"1.0\"}}";
        await File.WriteAllTextAsync(path, original);
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        try {
            // Act
            var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("set-pkg-version", Arguments:
                new Dictionary<string, object?> { ["package-path"] = directory.FullName, ["package-version"] = version }));
            // Assert
            result.Accepted.Should().BeFalse("a version must parse before any file is edited");
            (await File.ReadAllTextAsync(path)).Should().Be(original, "invalid input cannot move the timestamp or erase a version");
        }
        finally { directory.Delete(true); }
    }

    [Test]
    [Description("Cancelled file publication preserves the original content and leaves no staged file.")]
    public async Task Cancelled_write_preserves_destination() {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("clio10-cancel-write-");
        var path = Path.Combine(directory.FullName, "original.txt");
        await File.WriteAllTextAsync(path, "original");
        var services = new ServiceCollection();
        services.AddScoped<IFileWriterPrimitive, FileWriterPrimitive>();
        await using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try {
            // Act
            Func<Task> act = () => provider.GetRequiredService<IFileWriterPrimitive>().WriteTextAsync(path, "replacement", cancellation.Token);
            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>("a cancelled operation must not publish new data");
            (await File.ReadAllTextAsync(path)).Should().Be("original", "cancellation cannot truncate the destination");
            Directory.GetFiles(directory.FullName).Should().HaveCount(1, "cancellation must not leave temporary data behind");
        }
        finally { directory.Delete(true); }
    }

    [Test]
    [Description("A volume-root output path fails as an argument error that callers can preserve in partial execution receipts.")]
    public async Task Root_is_not_a_file() {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IFileWriterPrimitive, FileWriterPrimitive>();
        await using var provider = services.BuildServiceProvider();
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        // Act
        Func<Task> act = () => provider.GetRequiredService<IFileWriterPrimitive>().WriteTextAsync(root, "content", CancellationToken.None);
        // Assert
        await act.Should().ThrowAsync<ArgumentException>("a root is not a file destination, and this failure must remain catchable by Composition");
    }
}
