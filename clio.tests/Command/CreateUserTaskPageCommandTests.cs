using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>Verifies native page artifacts and preservation of existing task contracts.</summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class CreateUserTaskPageCommandTests : BaseCommandTests<CreateUserTaskPageOptions> {
	private readonly Guid _taskUId = Guid.Parse("90810b59-c2aa-4133-8b6b-61f57b12143c");
	private readonly Guid _packageUId = Guid.Parse("a0c87bb3-c673-4868-b9f9-0fdc5a069bc2");
	private CreateUserTaskPageCommand _command;
	private string _root;
	private string _package;
	private string MetadataPath => Path.Combine(_package, "Schemas", "UsrTask", "metadata.json");
	private string ResourcePath => Path.Combine(_package, "Resources", "UsrTask.ProcessUserTask", "resource.en-US.xml");

	/// <inheritdoc />
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreateUserTaskPageCommand>();
		_root = Path.GetFullPath("task-page-workspace");
		_package = Path.Combine(_root, "packages", "UsrExample");
		Write(Path.Combine(_root, ".clio", "workspaceSettings.json"), new { Packages = new[] { "UsrExample" } });
		Write(Path.Combine(_package, "descriptor.json"), new { Descriptor = new { Name = "UsrExample", UId = _packageUId } });
		Write(Path.Combine(_package, "Schemas", "UsrTask", "descriptor.json"), new {
			Descriptor = new { Name = "UsrTask", UId = _taskUId, ManagerName = "ProcessUserTaskSchemaManager" }
		});
		Write(MetadataPath, new { MetaData = new { Schema = new {
			A2 = "UsrTask", UId = _taskUId, B6 = _packageUId, ManagerName = "ProcessUserTaskSchemaManager",
			FK1 = "return true;", FJ1 = new object[] {
				new { A2 = "Input", UId = Guid.NewGuid(), L12 = 0 },
				new { A2 = "Output", UId = Guid.NewGuid(), L12 = 1 },
				new { A2 = "Both", UId = Guid.NewGuid(), L12 = 2 },
				new { A2 = "Internal", UId = Guid.NewGuid(), L12 = 3 },
				new { A2 = "DefaultBoth", UId = Guid.NewGuid() }
			}
		} } });
		FileSystem.Directory.CreateDirectory(Path.GetDirectoryName(ResourcePath)!);
		FileSystem.File.WriteAllText(ResourcePath, "<Resources Culture=\"en-US\"><Group Type=\"String\"><Items><Item Name=\"Parameters.Input.Caption\" Value=\"User input\"/><Item Name=\"Unrelated\" Value=\"Keep me\"/></Items></Group></Resources>");
	}

	/// <summary>Direction controls editor visibility while parameter metadata remains unchanged.</summary>
	[Test]
	[Description("Scaffolds input/variable editors and native inheritance without changing task parameter identities or direction.")]
	public void Execute_CreatesPageAndAssociation_PreservingTaskContract() {
		// Arrange
		string parameters = JsonNode.Parse(FileSystem.File.ReadAllText(MetadataPath))!["MetaData"]!["Schema"]!["FJ1"]!.ToJsonString();
		string originalResources = FileSystem.File.ReadAllText(ResourcePath);
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(0, because: "the owned task is valid for page scaffolding");
		string body = FileSystem.File.ReadAllText(Path.Combine(_package, "Schemas", "UsrTaskPage", "UsrTaskPage.js"));
		body.Should().Contain("InputMapping", because: "input parameters need mapping editors");
		body.Should().Contain("BothMapping", because: "explicit variables allow input mappings");
		body.Should().Contain("DefaultBothMapping", because: "omitted L12 defaults to Variable in Creatio");
		body.Should().NotContain("OutputMapping", because: "output-only parameters have no input editor");
		body.Should().NotContain("InternalMapping", because: "internal parameters are not designer inputs");
		body.Should().Contain("User input", because: "existing localized parameter captions should populate the scaffold");
		JsonNode task = JsonNode.Parse(FileSystem.File.ReadAllText(MetadataPath))!["MetaData"]!["Schema"]!;
		task["FJ1"]!.ToJsonString().Should().Be(parameters, because: "parameter identities and directions are not page-tool responsibilities");
		task["FK1"]!.GetValue<string>().Should().Be("return true;", because: "execution code must remain untouched");
		JsonNode page = JsonNode.Parse(FileSystem.File.ReadAllText(Path.Combine(_package, "Schemas", "UsrTaskPage", "descriptor.json")))!["Descriptor"]!;
		task["FK11"]!.GetValue<string>().Should().Be(page["UId"]!.GetValue<string>(), because: "the native task association must point to the created page");
		page["Parent"]!["Name"]!.GetValue<string>().Should().Be("ProcessFlowElementPropertiesPage", because: "mapping editors rely on the native process page base");
		FileSystem.File.ReadAllText(ResourcePath).Should().Be(originalResources, because: "omitting icons must not rewrite task resources");
	}

	/// <summary>Each combination of optional image slots is independent.</summary>
	[TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
	[TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
	[Description("Maps all MCP inputs and independently supplies any combination of native SVG image slots.")]
	public void Tool_MapsArguments_AndPreservesOtherResources(int iconMask) {
		// Arrange
		string icon = Path.Combine(_root, "icon.svg");
		FileSystem.File.WriteAllText(icon, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\"><path d=\"M1 1L15 15\"/></svg>");
		CreateUserTaskPageArgs args = new(_root, "UsrExample", _taskUId, "UsrTaskPage", "Task parameters", "en-US",
			(iconMask & 1) != 0 ? icon : null, (iconMask & 2) != 0 ? icon : null, (iconMask & 4) != 0 ? icon : null);
		// Act
		var result = Container.GetRequiredService<CreateUserTaskPageTool>().Create(args);
		// Assert
		result.ExitCode.Should().Be(0, because: "every optional icon combination is supported");
		XDocument resources = XDocument.Parse(FileSystem.File.ReadAllText(ResourcePath));
		resources.Descendants("Item").Single(e => (string)e.Attribute("Name")! == "Unrelated").Attribute("Value")!.Value
			.Should().Be("Keep me", because: "adding icons must preserve unrelated resources");
		string[] slots = ["SmallSvgImage", "LargeSvgImage", "TitleSvgImage"];
		for (int index = 0; index < slots.Length; index++) {
			resources.Descendants("Item").Any(e => (string)e.Attribute("Name")! == slots[index]).Should()
				.Be((iconMask & (1 << index)) != 0, because: "only the requested resource slots should be created");
		}
		FileSystem.File.ReadAllText(Path.Combine(_package, "Resources", "UsrTaskPage.ClientUnit", "resource.en-US.xml"))
			.Should().Contain("Task parameters", because: "the MCP caption and culture must reach the native resource file");
	}

	/// <summary>Conflicting pages are never replaced implicitly.</summary>
	[Test]
	[Description("A repeated create refuses the existing association and preserves all workspace files.")]
	public void Execute_RefusesExistingPage_WithoutChangingFiles() {
		// Arrange
		_command.Execute(Options());
		var original = FileSystem.Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, FileSystem.File.ReadAllText);
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(1, because: "create is deliberately not an update operation");
		original.Keys.ToDictionary(p => p, FileSystem.File.ReadAllText).Should().BeEquivalentTo(original,
			because: "a retry must preserve customized pages, task metadata and resources");
	}

	/// <summary>SVG validation happens before any workspace writes.</summary>
	[TestCase("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
	[TestCase("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>")]
	[TestCase("<svg xmlns=\"http://www.w3.org/2000/svg\"><use href=\"https://example.com/image.svg\"/></svg>")]
	[TestCase("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///secret'>]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")]
	[TestCase("<not-svg/>")]
	[TestCase("<?xml-stylesheet type=\"text/css\" href=\"https://example.invalid/icon.css\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
	[TestCase("<svg xmlns=\"http://www.w3.org/2000/svg\" xml:base=\"https://example.invalid/icon.svg\"><use href=\"#shape\"/></svg>")]
	[Description("Rejects active, external or invalid SVG input without creating a page or modifying its task.")]
	public void Execute_RejectsInvalidSvg_BeforeWrites(string svg) {
		// Arrange
		CreateUserTaskPageOptions options = Options();
		options.SmallIconPath = Path.Combine(_root, "invalid.svg");
		FileSystem.File.WriteAllText(options.SmallIconPath, svg);
		string original = FileSystem.File.ReadAllText(MetadataPath);
		// Act
		int result = _command.Execute(options);
		// Assert
		result.Should().Be(1, because: "unsafe or malformed SVG must fail validation");
		FileSystem.Directory.Exists(Path.Combine(_package, "Schemas", "UsrTaskPage")).Should().BeFalse(because: "all icon validation must precede writes");
		FileSystem.File.ReadAllText(MetadataPath).Should().Be(original, because: "failed validation must not associate an uncreated page");
	}

	/// <summary>Layout names must not become conflicting parameter controls.</summary>
	[TestCase("UserTaskContainer")]
	[TestCase("EditorsContainer")]
	[Description("Rejects a parameter name that would collide with a generated or inherited container before any writes.")]
	public void Execute_RejectsContainerCollision_WithoutWrites(string name) {
		// Arrange
		JsonNode metadata = JsonNode.Parse(FileSystem.File.ReadAllText(MetadataPath))!;
		metadata["MetaData"]!["Schema"]!["FJ1"]![0]!["A2"] = name;
		FileSystem.File.WriteAllText(MetadataPath, metadata.ToJsonString());
		var original = FileSystem.Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, FileSystem.File.ReadAllText);
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(1, because: "a conflicting editor would make the generated page invalid");
		FileSystem.Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, FileSystem.File.ReadAllText)
			.Should().BeEquivalentTo(original, because: "validation failures must preserve every workspace artifact");
	}

	/// <summary>Identity and native metadata failures cannot leave partial page artifacts.</summary>
	[TestCase("owner")]
	[TestCase("manager")]
	[TestCase("direction")]
	[TestCase("page")]
	[TestCase("resources")]
	[Description("Rejects mismatched task ownership, invalid metadata and existing page artifacts without changing workspace files.")]
	public void Execute_RejectsInvalidWorkspace_BeforeWrites(string failure) {
		// Arrange
		JsonNode root = JsonNode.Parse(FileSystem.File.ReadAllText(MetadataPath))!;
		JsonNode schema = root["MetaData"]!["Schema"]!;
		switch (failure) {
			case "owner": schema["B6"] = Guid.NewGuid().ToString(); break;
			case "manager": schema["ManagerName"] = "EntitySchemaManager"; break;
			case "direction": schema["FJ1"]![0]!["L12"] = 99; break;
			case "page": Write(Path.Combine(_package, "Schemas", "UsrTaskPage", "descriptor.json"), new { Keep = true }); break;
			case "resources": Write(Path.Combine(_package, "Resources", "UsrTaskPage.ClientUnit", "keep.json"), new { Keep = true }); break;
		}
		FileSystem.File.WriteAllText(MetadataPath, root.ToJsonString());
		var original = FileSystem.Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, FileSystem.File.ReadAllText);
		// Act
		int result = _command.Execute(Options());
		// Assert
		result.Should().Be(1, because: "the invalid workspace must be rejected before scaffolding");
		FileSystem.Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, FileSystem.File.ReadAllText)
			.Should().BeEquivalentTo(original, because: "validation must preserve the complete workspace");
	}

	private CreateUserTaskPageOptions Options() => new() { WorkspacePath = _root, PackageName = "UsrExample", UserTaskUId = _taskUId, PageName = "UsrTaskPage", Caption = "Task parameters" };
	private void Write(string path, object content) {
		FileSystem.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		FileSystem.File.WriteAllText(path, JsonSerializer.Serialize(content));
	}
}
