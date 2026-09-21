using Clio10.Composition;
using Clio10.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Exercises archive selection through real Composition, Core and filesystem capabilities.</summary>
public sealed class PackageArchiveTests {
    [TestCase(false)]
    [TestCase(true)]
    [Description("Selected packages round trip through ZIP or a directory batch, with independent ignore rules per package.")]
    public async Task Batch_round_trip(bool directoryInput) {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-workflow-batch-").FullName;
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<IClioComposition>();
        try {
            string source = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
            foreach (string name in new[] { "First", "Second" }) {
                string package = Directory.CreateDirectory(Path.Combine(source, name)).FullName;
                Directory.CreateDirectory(Path.Combine(package, "Files"));
                await File.WriteAllTextAsync(Path.Combine(package, "descriptor.json"), "{}");
                await File.WriteAllTextAsync(Path.Combine(package, "Files", "optional.txt"), name);
            }
            await File.WriteAllTextAsync(Path.Combine(source, "First", "clioignore"), "optional.txt");
            string zip = Path.Combine(root, "batch.zip");
            string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            // Act
            var packed = await composition.ExecuteAsync(new("generate-pkg-zip", Arguments: new Dictionary<string, object?> {
                ["package-path"] = source, ["packages"] = "First, Second", ["destination-path"] = zip
            }));
            string input = zip;
            if (directoryInput) {
                input = Path.Combine(root, "archives");
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, input);
                await File.WriteAllTextAsync(Path.Combine(input, "ignored.txt"), "not an archive");
            }
            var result = await composition.ExecuteAsync(new("extract-pkg-zip", Arguments: new Dictionary<string, object?> {
                ["archive-path"] = input, ["destination-path"] = output
            }));
            // Assert
            packed.Accepted.Should().BeTrue("selected package names are valid and relative to the supplied root");
            result.Accepted.Should().BeTrue("both supported batch input forms use the same validated publication path");
            result.AcceptedSteps.Should().HaveCount(2, "the batch returns one completed receipt for each published package");
            File.Exists(Path.Combine(output, "First", "Files", "optional.txt")).Should().BeFalse("the first package excludes this file");
            (await File.ReadAllTextAsync(Path.Combine(output, "Second", "Files", "optional.txt"))).Should().Be("Second", "ignore state must not leak between packages in the same workflow scope");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase("../escape")]
    [TestCase("First,first")]
    [TestCase(", ,")]
    [Description("Invalid or duplicate batch selections are rejected before attempting any source or destination work.")]
    public async Task Invalid_batch_selection(string names) {
        // Arrange
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("generate-pkg-zip", Arguments: new Dictionary<string, object?> {
            ["packages"] = names
        }));
        // Assert
        result.Code.Should().Be("invalid-arguments", "batch selection cannot escape the root or publish two packages to the same destination");
    }

    [TestCase("generate-pkg-zip")]
    [TestCase("extract-pkg-zip")]
    [Description("An unavailable output directory is distinguished from a missing package or input archive.")]
    public async Task Missing_destination_is_not_reported_as_missing_source(string operation) {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-missing-output-").FullName;
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        try {
            string missing = Path.Combine(root, "missing");
            var arguments = operation == "generate-pkg-zip" ? new Dictionary<string, object?> {
                ["package-path"] = root, ["destination-path"] = Path.Combine(missing, "out.zip")
            } : new Dictionary<string, object?> { ["archive-path"] = Path.Combine(root, "input.zip"), ["destination-path"] = missing };
            // Act
            var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: arguments));
            // Assert
            result.Code.Should().Be("archive-destination-unavailable", "the client should repair the destination rather than retrying or replacing its source");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    [Description("Package compression selects only vendor package content and extraction reconstructs those files.")]
    public async Task Package_round_trip() {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-package-archive-").FullName;
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<IClioComposition>();
        try {
            string source = Directory.CreateDirectory(Path.Combine(root, "packages", "Example")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "Files"));
            Directory.CreateDirectory(Path.Combine(source, "Bin"));
            await File.WriteAllTextAsync(Path.Combine(source, "descriptor.json"), "{\"Descriptor\":{\"Name\":\"Example\"}}");
            await File.WriteAllTextAsync(Path.Combine(source, "Files", "code.cs"), "source");
            await File.WriteAllTextAsync(Path.Combine(source, "Bin", "code.pdb"), "debug");
            await File.WriteAllTextAsync(Path.Combine(source, "Example.csproj"), "developer project");
            string archive = Path.Combine(root, "Example.gz");
            string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            // Act
            var pack = await composition.ExecuteAsync(new("generate-pkg-zip", Arguments: new Dictionary<string, object?> {
                ["package-path"] = source, ["destination-path"] = archive, ["skip-pdb"] = true
            }));
            var extract = await composition.ExecuteAsync(new("extract-pkg-zip", Arguments: new Dictionary<string, object?> {
                ["archive-path"] = archive, ["destination-path"] = output
            }));
            // Assert
            pack.Accepted.Should().BeTrue("local archive work must not require an HTTP environment");
            extract.Accepted.Should().BeTrue("the selected package content is a valid legacy archive");
            (await File.ReadAllTextAsync(Path.Combine(output, "Example", "Files", "code.cs"))).Should().Be("source", "selected binary contents must round trip through real primitives");
            Directory.GetFiles(Path.Combine(output, "Example"), "*", SearchOption.AllDirectories).Should().HaveCount(2, "PDBs and root development project files are excluded by composition policy");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("Package and workspace ignore rules exclude package-relative files without requiring both rule files.")]
    public async Task Ignore_file_filters_pack(bool workspace) {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-package-ignore-").FullName;
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        try {
            string source = Directory.CreateDirectory(Path.Combine(root, "packages", "Example")).FullName;
            string ignoreRoot = workspace ? Directory.CreateDirectory(Path.Combine(root, ".clio")).FullName : source;
            await File.WriteAllTextAsync(Path.Combine(ignoreRoot, "clioignore"), "private.txt");
            Directory.CreateDirectory(Path.Combine(source, "Files"));
            await File.WriteAllTextAsync(Path.Combine(source, "descriptor.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(source, "Files", "private.txt"), "excluded");
            await File.WriteAllTextAsync(Path.Combine(source, "Files", "public.txt"), "included");
            string archive = Path.Combine(root, "out.gz");
            // Act
            var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("generate-pkg-zip", Arguments:
                new Dictionary<string, object?> { ["package-path"] = source, ["destination-path"] = archive }));
            // Assert
            result.Accepted.Should().BeTrue("either supported ignore location is sufficient");
            ((IReadOnlyDictionary<string, object?>)result.Payload!)["files"].Should().Be(2, "only descriptor and the public file should be selected");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    [Description("Workspace, package and nested ignore rules have scoped precedence and never duplicate selected files.")]
    public async Task Nested_ignore_rules_are_scoped() {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-package-rules-").FullName;
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<IClioComposition>();
        try {
            string source = Directory.CreateDirectory(Path.Combine(root, "packages", "Example")).FullName;
            foreach (string directory in new[] { "Files", "Files/nested", "Files/blocked", "Data", "Resources" })
                Directory.CreateDirectory(Path.Combine(source, directory));
            Directory.CreateDirectory(Path.Combine(root, ".clio"));
            await File.WriteAllTextAsync(Path.Combine(root, ".clio", "clioignore"), "*.tmp\n!*.log\n");
            await File.WriteAllTextAsync(Path.Combine(source, "clioignore"), "!Files/keep.tmp\nFiles/blocked/\n*.log\n");
            await File.WriteAllTextAsync(Path.Combine(source, "Files", "clioignore"), "/secret.txt\n*.cache\n!blocked/restore.txt\n");
            await File.WriteAllTextAsync(Path.Combine(source, "Data", "clioignore"), "/different.txt\n");
            string[] included = ["descriptor.json", "Files/keep.tmp", "Files/nested/secret.txt", "Data/secret.txt", "Resources/a.cache"];
            string[] excluded = ["Files/drop.tmp", "Files/secret.txt", "Files/a.cache", "Files/nested/a.cache", "Files/blocked/restore.txt", "Data/different.txt", "Files/private.log"];
            foreach (string file in included.Concat(excluded)) await File.WriteAllTextAsync(Path.Combine(source, file), file);
            string archive = Path.Combine(root, "Example.gz");
            string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            // Act
            var packed = await composition.ExecuteAsync(new("generate-pkg-zip", Arguments: new Dictionary<string, object?> {
                ["package-path"] = source, ["destination-path"] = archive
            }));
            var extracted = await composition.ExecuteAsync(new("extract-pkg-zip", Arguments: new Dictionary<string, object?> {
                ["archive-path"] = archive, ["destination-path"] = output
            }));
            // Assert
            packed.Accepted.Should().BeTrue("nested rules must not create duplicate archive entries");
            extracted.Accepted.Should().BeTrue("selected contents form a valid archive");
            string[] actual = Directory.GetFiles(Path.Combine(output, "Example"), "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(Path.Combine(output, "Example"), x).Replace('\\', '/')).ToArray();
            actual.Should().BeEquivalentTo(included.Concat(["Files/clioignore", "Data/clioignore"]),
                "nested rules apply in their directory and cannot resurrect a file beneath an excluded parent");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase("generate-pkg-zip", "package-path", "package-file-not-found")]
    [TestCase("extract-pkg-zip", "archive-path", "archive-path-not-found")]
    [Description("Missing package and archive inputs are distinguished from failed publication.")]
    public async Task Missing_input_has_not_found_code(string operation, string inputKey, string expectedCode) {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-archive-missing-").FullName;
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        try {
            // Act
            var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: new Dictionary<string, object?> {
                [inputKey] = Path.Combine(root, "missing"), ["destination-path"] = root
            }));
            // Assert
            result.Code.Should().Be(expectedCode, "a missing source is actionable before any publication attempt");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("A single branch supplies package contents; an additional empty branch makes the layout ambiguous.")]
    public async Task Branch_layout_requires_one_directory(bool additionalEmptyBranch) {
        // Arrange
        string root = Directory.CreateTempSubdirectory("clio10-package-branch-").FullName;
        var services = new ServiceCollection();
        services.AddClioComposition(new CompositionOptions());
        await using var provider = services.BuildServiceProvider();
        try {
            string source = Directory.CreateDirectory(Path.Combine(root, "packages", "Example")).FullName;
            string branch = Directory.CreateDirectory(Path.Combine(source, "branches", "7.8.0")).FullName;
            await File.WriteAllTextAsync(Path.Combine(branch, "descriptor.json"), "{}");
            if (additionalEmptyBranch) Directory.CreateDirectory(Path.Combine(source, "branches", "empty"));
            string archive = Path.Combine(root, "Example.gz");
            // Act
            var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("generate-pkg-zip", Arguments:
                new Dictionary<string, object?> { ["package-path"] = source, ["destination-path"] = archive }));
            // Assert
            result.Accepted.Should().Be(!additionalEmptyBranch, "branch selection must count directories even when they have no files");
            File.Exists(archive).Should().Be(!additionalEmptyBranch, "ambiguous layouts must not publish an archive");
        }
        finally { Directory.Delete(root, true); }
    }
}
