using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>Exercises native registration artifacts and workspace identity validation.</summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class RegisterProcessElementCommandTests : BaseCommandTests<RegisterProcessElementOptions> {
	private readonly Guid _taskUId = Guid.Parse("90810b59-c2aa-4133-8b6b-61f57b12143c");
	private readonly Guid _packageUId = Guid.Parse("a0c87bb3-c673-4868-b9f9-0fdc5a069bc2");
	private RegisterProcessElementCommand _command;
	private string _root;
	private string _package;

	/// <inheritdoc />
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<RegisterProcessElementCommand>();
		_root = Path.GetFullPath("registration-workspace");
		_package = Path.Combine(_root, "packages", "UsrExample");
		Write(Path.Combine(_root, ".clio", "workspaceSettings.json"), new { Packages = new[] { "UsrExample" } });
		Write(Path.Combine(_package, "descriptor.json"), new { Descriptor = new { Name = "UsrExample", UId = _packageUId } });
		Write(Path.Combine(_package, "Schemas", "UsrTask", "descriptor.json"), new {
			Descriptor = new { Name = "UsrTask", UId = _taskUId, ManagerName = "ProcessUserTaskSchemaManager" }
		});
		Write(Path.Combine(_package, "Schemas", "UsrTask", "metadata.json"), new {
			MetaData = new { Schema = new { A2 = "UsrTask", UId = _taskUId, B6 = _packageUId, ManagerName = "ProcessUserTaskSchemaManager" } }
		});
	}

	/// <summary>The MCP adapter must preserve all inputs without requiring a remote environment.</summary>
	[Test]
	[Description("Maps every MCP argument to the offline registration command options.")]
	public void Tool_MapsAllArguments_WithoutAnEnvironment() {
		// Arrange
		RegisterProcessElementTool tool = Container.GetRequiredService<RegisterProcessElementTool>();
		RegisterProcessElementArgs args = new(_root, "UsrExample", _taskUId, "Format text");
		// Act
		var result = tool.RegisterProcessElement(args);
		// Assert
		result.ExitCode.Should().Be(0, because: "the adapter must execute the same validated local generator");
		string[] scripts = FileSystem.Directory.GetFiles(Path.Combine(_package, "SqlScripts"), "*.sql", SearchOption.AllDirectories);
		scripts.Should().HaveCount(2, because: "the workspace and package arguments must select the intended output directory");
		foreach (string script in scripts) {
			string sql = FileSystem.File.ReadAllText(script);
			sql.Should().Contain(args.UserTaskUId.ToString(), because: "the adapter must preserve the task identity");
			sql.Should().Contain(args.Caption, because: "the adapter must preserve the caption");
		}
	}

	/// <summary>Both database scripts must preserve literal captions and stable identities on retries.</summary>
	[Test]
	[Description("Generates both native after-package SQL dialects and preserves every byte on a repeated invocation.")]
	public void Execute_GeneratesBothDialects_AndPreservesRetry() {
		// Arrange
		RegisterProcessElementOptions options = Options();
		options.Caption = "O'Brien \\\"UId\\\" 日本語";
		// Act
		int first = _command.Execute(options);
		string[] paths = FileSystem.Directory.GetFiles(Path.Combine(_package, "SqlScripts"), "*", SearchOption.AllDirectories);
		var original = paths.ToDictionary(path => path, FileSystem.File.ReadAllText);
		int second = _command.Execute(options);
		// Assert
		first.Should().Be(0, because: "a valid workspace task must be registrable offline");
		second.Should().Be(0, because: "identical registration requests must be idempotent");
		paths.Should().HaveCount(4, because: "each dialect needs one descriptor and one SQL file");
		paths.ToDictionary(path => path, FileSystem.File.ReadAllText).Should().BeEquivalentTo(original,
			because: "retry must preserve script UIds, timestamps and content");
		foreach (string path in paths.Where(path => path.EndsWith(".sql", StringComparison.Ordinal))) {
			string sql = FileSystem.File.ReadAllText(path);
			sql.Should().Contain("NOT EXISTS", because: "installation must not duplicate or overwrite an existing registration");
			sql.Should().Contain(_taskUId.ToString(), because: "registration must target the requested schema identity");
			sql.Should().Contain(_packageUId.ToString(), because: "registration must match the owning package identity");
			sql.Should().Contain("O''Brien", because: "caption apostrophes must stay inside a SQL literal");
			sql.Should().Contain("日本語", because: "Unicode captions must survive generation");
			string descriptorPath = Path.Combine(Path.GetDirectoryName(path)!, "descriptor.json");
			JsonNode descriptor = JsonNode.Parse(FileSystem.File.ReadAllText(descriptorPath))!["SqlScript"]!;
			descriptor["InstallType"]!.GetValue<int>().Should().Be(1, because: "registration runs after package installation");
			descriptor["DBEngineType"]!.GetValue<int>().Should().Be(path.Contains("PostgreSql", StringComparison.Ordinal) ? 2 : 0,
				because: "each script must be limited to its native database engine");
			if (path.Contains("MsSql", StringComparison.Ordinal)) {
				sql.Should().Contain("N'O''Brien", because: "SQL Server requires Unicode string literals for these captions");
				sql.Should().Contain("[SysProcessUserTask]", because: "SQL Server identifiers must not depend on QUOTED_IDENTIFIER");
			}
		}
	}

	/// <summary>Invalid ownership must be rejected before creating files.</summary>
	[TestCase("UId")]
	[TestCase("B6")]
	[TestCase("ManagerName")]
	[TestCase("A2")]
	[Description("Rejects mismatches between the task descriptor, metadata and package without creating registration artifacts.")]
	public void Execute_RejectsMismatchedMetadata(string property) {
		// Arrange
		string path = Path.Combine(_package, "Schemas", "UsrTask", "metadata.json");
		JsonNode metadata = JsonNode.Parse(FileSystem.File.ReadAllText(path))!;
		metadata["MetaData"]!["Schema"]![property] = Guid.NewGuid().ToString();
		FileSystem.File.WriteAllText(path, metadata.ToJsonString());
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(1, because: "mismatched identities must not generate deployable SQL");
		FileSystem.Directory.Exists(Path.Combine(_package, "SqlScripts")).Should().BeFalse(
			because: "all ownership validation must finish before the first write");
	}

	/// <summary>A changed retry cannot replace an authored registration.</summary>
	[Test]
	[Description("Preserves existing registration files when a retry supplies a conflicting caption.")]
	public void Execute_RejectsConflict_WithoutOverwriting() {
		// Arrange
		RegisterProcessElementOptions options = Options();
		_command.Execute(options);
		string[] paths = FileSystem.Directory.GetFiles(Path.Combine(_package, "SqlScripts"), "*", SearchOption.AllDirectories);
		var original = paths.ToDictionary(path => path, FileSystem.File.ReadAllText);
		options.Caption = "Different caption";
		// Act
		int result = _command.Execute(options);
		// Assert
		result.Should().Be(1, because: "conflicting generated content must require an explicit artifact edit");
		paths.ToDictionary(path => path, FileSystem.File.ReadAllText).Should().BeEquivalentTo(original,
			because: "a failed retry must preserve existing script identities and captions");
	}

	/// <summary>A path-like package cannot escape the selected workspace.</summary>
	[TestCase("../outside")]
	[TestCase("..\\outside")]
	[TestCase("UnknownPackage")]
	[Description("Rejects path traversal and packages absent from workspace settings.")]
	public void Execute_RejectsInvalidPackage(string packageName) {
		// Arrange
		RegisterProcessElementOptions options = Options();
		options.PackageName = packageName;
		// Act
		int result = _command.Execute(options);
		// Assert
		result.Should().Be(1, because: "registration must remain within a declared workspace package");
		FileSystem.Directory.Exists(Path.Combine(_package, "SqlScripts")).Should().BeFalse(
			because: "invalid package requests must not write files");
	}

	/// <summary>Interrupted artifact generation can resume without rotating existing identities.</summary>
	[TestCase(".sql")]
	[TestCase("descriptor.json")]
	[Description("Recreates a missing registration artifact and leaves every remaining artifact unchanged.")]
	public void Execute_RepairsMissingArtifact(string suffix) {
		// Arrange
		_command.Execute(Options());
		string[] paths = FileSystem.Directory.GetFiles(Path.Combine(_package, "SqlScripts"), "*", SearchOption.AllDirectories);
		string missing = paths.First(p => p.EndsWith(suffix, StringComparison.Ordinal));
		var remaining = paths.Where(p => p != missing).ToDictionary(p => p, FileSystem.File.ReadAllText);
		FileSystem.File.Delete(missing);
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(0, because: "missing generated artifacts can be recreated on retry");
		FileSystem.File.Exists(missing).Should().BeTrue(because: "the interrupted artifact must be restored");
		remaining.Keys.ToDictionary(p => p, FileSystem.File.ReadAllText).Should().BeEquivalentTo(remaining,
			because: "existing artifact identities and content must not change during recovery");
	}

	/// <summary>Duplicate task identities are ambiguous even when both descriptors agree.</summary>
	[Test]
	[Description("Rejects duplicate task UIds before writing registration artifacts.")]
	public void Execute_RejectsDuplicateTaskUId() {
		// Arrange
		string original = Path.Combine(_package, "Schemas", "UsrTask");
		string duplicate = Path.Combine(_package, "Schemas", "Duplicate");
		FileSystem.Directory.CreateDirectory(duplicate);
		foreach (string path in FileSystem.Directory.GetFiles(original)) {
			FileSystem.File.Copy(path, Path.Combine(duplicate, Path.GetFileName(path)));
		}
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(1, because: "two task descriptors with one UId are ambiguous");
		FileSystem.Directory.Exists(Path.Combine(_package, "SqlScripts")).Should().BeFalse(
			because: "the duplicate must be detected before any registration file is written");
	}

	/// <summary>A wrong native engine descriptor must not be silently adopted.</summary>
	[Test]
	[Description("Preserves all existing artifacts when a native descriptor conflicts with its dialect.")]
	public void Execute_RejectsConflictingDescriptor() {
		// Arrange
		_command.Execute(Options());
		string descriptorPath = FileSystem.Directory.GetFiles(Path.Combine(_package, "SqlScripts"), "descriptor.json", SearchOption.AllDirectories).First();
		JsonNode descriptor = JsonNode.Parse(FileSystem.File.ReadAllText(descriptorPath))!;
		descriptor["SqlScript"]!["DBEngineType"] = 99;
		FileSystem.File.WriteAllText(descriptorPath, descriptor.ToJsonString());
		var original = FileSystem.Directory.GetFiles(Path.Combine(_package, "SqlScripts"), "*", SearchOption.AllDirectories)
			.ToDictionary(p => p, FileSystem.File.ReadAllText);
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(1, because: "an existing descriptor must match the requested dialect");
		original.Keys.ToDictionary(p => p, FileSystem.File.ReadAllText).Should().BeEquivalentTo(original,
			because: "conflicting native descriptors must never be overwritten implicitly");
	}

	private RegisterProcessElementOptions Options() => new() {
		WorkspacePath = _root, PackageName = "UsrExample", UserTaskUId = _taskUId, Caption = "Format text"
	};

	private void Write(string path, object content) {
		FileSystem.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		FileSystem.File.WriteAllText(path, JsonSerializer.Serialize(content));
	}
}
