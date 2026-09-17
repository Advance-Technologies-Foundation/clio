using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Clio.Common;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>Options for scaffolding a Classic process parameter page in a workspace.</summary>
[Verb("create-user-task-page", HelpText = "Create and associate a Classic user-task parameter page in a workspace")]
public sealed class CreateUserTaskPageOptions : EnvironmentOptions {
	/// <inheritdoc />
	internal override bool RequiredEnvironment => false;
	/// <summary>Explicit workspace root.</summary>
	[Option("workspace-path", Required = true)] public string WorkspacePath { get; set; }
	/// <summary>Package owning the existing task and the new page.</summary>
	[Option("package-name", Required = true)] public string PackageName { get; set; }
	/// <summary>Existing user-task UId.</summary>
	[Option("user-task-uid", Required = true)] public Guid UserTaskUId { get; set; }
	/// <summary>New, unique client schema name.</summary>
	[Option("page-name", Required = true)] public string PageName { get; set; }
	/// <summary>Page caption stored in the requested culture.</summary>
	[Option("caption", Required = true)] public string Caption { get; set; }
	/// <summary>Resource culture for captions and any supplied icons.</summary>
	[Option("culture", Default = "en-US")] public string Culture { get; set; } = "en-US";
	/// <summary>Optional SVG for the small toolbox image.</summary>
	[Option("small-icon-path")] public string SmallIconPath { get; set; }
	/// <summary>Optional SVG for the diagram image.</summary>
	[Option("large-icon-path")] public string LargeIconPath { get; set; }
	/// <summary>Optional SVG for the properties header image.</summary>
	[Option("title-icon-path")] public string TitleIconPath { get; set; }
}

/// <summary>Creates native workspace artifacts for one Classic user-task parameter page.</summary>
public interface IUserTaskPageScaffolder {
	/// <summary>Validates inputs, writes a new editable page and associates it with the task.</summary>
	/// <param name="options">Workspace identity, page caption and optional icon files.</param>
	/// <returns>The created JavaScript file path.</returns>
	string Create(CreateUserTaskPageOptions options);
}

/// <summary>CLI adapter for Classic parameter-page scaffolding.</summary>
public sealed class CreateUserTaskPageCommand(IUserTaskPageScaffolder scaffolder, ILogger logger)
	: Command<CreateUserTaskPageOptions> {
	/// <inheritdoc />
	public override int Execute(CreateUserTaskPageOptions options) {
		try {
			logger.WriteInfo($"Created Classic parameter page: {scaffolder.Create(options)}");
			logger.WriteInfo("Deploy the workspace/package, then verify mappings after saving and reopening the process.");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>Scaffolds the native Classic page shape demonstrated by the custom-element reference.</summary>
public sealed class UserTaskPageScaffolder(IFileSystem files) : IUserTaskPageScaffolder {
	private const string ParentName = "ProcessFlowElementPropertiesPage";
	private const string ParentUId = "0f347363-31e5-4222-a82e-dcfeda34cbb6";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	private static readonly Regex NamePattern = new(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.None, TimeSpan.FromSeconds(1));

	/// <inheritdoc />
	public string Create(CreateUserTaskPageOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateName(options.PackageName, "package-name");
		ValidateName(options.PageName, "page-name");
		if (string.IsNullOrWhiteSpace(options.WorkspacePath) || options.UserTaskUId == Guid.Empty
			|| string.IsNullOrWhiteSpace(options.Caption)) {
			throw new ArgumentException("workspace-path, user-task-uid and caption are required.");
		}
		string culture = System.Globalization.CultureInfo.GetCultureInfo(options.Culture).Name;
		if (string.IsNullOrEmpty(culture)) { throw new ArgumentException("A specific resource culture is required."); }
		string workspace = files.Path.GetFullPath(options.WorkspacePath);
		JsonNode settings = Read(files.Path.Combine(workspace, ".clio", "workspaceSettings.json"));
		if (!(settings["Packages"]?.AsArray().Any(p => p?.GetValue<string>() == options.PackageName) ?? false)) {
			throw new InvalidOperationException("The package must belong to the workspace.");
		}
		string packagePath = files.Path.Combine(workspace, "packages", options.PackageName);
		JsonNode package = Read(files.Path.Combine(packagePath, "descriptor.json"))["Descriptor"];
		if (package?["Name"]?.GetValue<string>() != options.PackageName
			|| !Guid.TryParse(package?["UId"]?.GetValue<string>(), out Guid packageUId) || packageUId == Guid.Empty) {
			throw new InvalidOperationException("The package descriptor identity is invalid.");
		}
		string schemasPath = files.Path.Combine(packagePath, "Schemas");
		string[] descriptors = files.Directory.GetFiles(schemasPath, "descriptor.json", SearchOption.AllDirectories);
		string[] matches = descriptors.Where(p => Read(p)["Descriptor"]?["UId"]?.GetValue<string>()
			.Equals(options.UserTaskUId.ToString("D"), StringComparison.OrdinalIgnoreCase) == true).ToArray();
		if (matches.Length != 1) { throw new InvalidOperationException("Exactly one matching user task must exist in the package."); }
		JsonNode taskDescriptorRoot = Read(matches[0]);
		JsonNode taskDescriptor = taskDescriptorRoot["Descriptor"];
		string taskName = taskDescriptor["Name"]?.GetValue<string>();
		ValidateName(taskName, "task name");
		string taskMetadataPath = files.Path.Combine(files.Path.GetDirectoryName(matches[0]), "metadata.json");
		JsonNode taskRoot = Read(taskMetadataPath);
		JsonNode task = taskRoot["MetaData"]?["Schema"];
		if (taskDescriptor["ManagerName"]?.GetValue<string>() != "ProcessUserTaskSchemaManager"
			|| task?["ManagerName"]?.GetValue<string>() != "ProcessUserTaskSchemaManager"
			|| task?["A2"]?.GetValue<string>() != taskName
			|| Guid.Parse(task["UId"].GetValue<string>()) != options.UserTaskUId
			|| Guid.Parse(task["B6"].GetValue<string>()) != packageUId) {
			throw new InvalidOperationException("Task descriptor, metadata and package identities must agree.");
		}
		string existingPage = task["FK11"]?.GetValue<string>();
		if (!string.IsNullOrEmpty(existingPage) && existingPage != Guid.Empty.ToString()) {
			throw new InvalidOperationException("The user task already has a parameter page. Edit that page instead of replacing it.");
		}
		string pagePath = files.Path.Combine(schemasPath, options.PageName);
		if (files.Directory.Exists(pagePath) || files.Directory.GetDirectories(files.Path.Combine(workspace, "packages"))
			.Any(p => files.Directory.Exists(files.Path.Combine(p, "Schemas", options.PageName)))) {
			throw new InvalidOperationException("The page name already exists in the workspace; existing pages are never overwritten.");
		}
		JsonArray parameters = task["FJ1"]?.AsArray() ?? throw new InvalidOperationException("Task parameter metadata is missing.");
		string[] inputs = parameters.Where(p => IsInput(p)).Select(p => p["A2"].GetValue<string>()).ToArray();
		foreach (string name in inputs) { ValidateName(name, "parameter name"); }
		if (inputs.Any(name => name is "UserTaskContainer" or "EditorsContainer")) {
			throw new InvalidOperationException("An input parameter name conflicts with a Classic page layout container.");
		}
		if (inputs.Distinct(StringComparer.Ordinal).Count() != inputs.Length) {
			throw new InvalidOperationException("Parameter names must be unique.");
		}
		string taskResourcePath = files.Path.Combine(packagePath, "Resources", taskName + ".ProcessUserTask", "resource." + culture + ".xml");
		XDocument taskResources = files.File.Exists(taskResourcePath) ? ReadXml(files.File.ReadAllText(taskResourcePath)) : NewResources(culture);
		XElement taskItems = GetItems(taskResources);
		bool hasIcons = false;
		foreach ((string name, string path) in new[] { ("SmallSvgImage", options.SmallIconPath), ("LargeSvgImage", options.LargeIconPath), ("TitleSvgImage", options.TitleIconPath) }) {
			if (string.IsNullOrWhiteSpace(path)) { continue; }
			if (files.FileInfo.New(path).Length > 1024 * 1024) { throw new ArgumentException("An SVG icon must be at most 1 MiB."); }
			byte[] bytes = files.File.ReadAllBytes(path);
			ValidateSvg(bytes);
			XElement item = taskItems.Elements("Item").SingleOrDefault(i => (string)i.Attribute("Name") == name);
			item?.Remove();
			taskItems.Add(new XElement("Item", new XAttribute("Name", name), new XAttribute("Type", "Image"),
				new XAttribute("ContentType", "Data"), new XAttribute("FileExtension", ".svg"), new XAttribute("Value", Convert.ToBase64String(bytes))));
			hasIcons = true;
		}
		string pageResourcePath = files.Path.Combine(packagePath, "Resources", options.PageName + ".ClientUnit", "resource." + culture + ".xml");
		if (files.Directory.Exists(files.Path.GetDirectoryName(pageResourcePath))) {
			throw new InvalidOperationException("Resources already exist for the requested page; they were preserved.");
		}
		Guid pageUId = Guid.NewGuid();
		string stamp = $"/Date({DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})/";
		XDocument pageResources = NewResources(culture);
		GetItems(pageResources).Add(new XElement("Item", new XAttribute("Name", "Caption"), new XAttribute("Value", options.Caption)));
		var artifacts = new Dictionary<string, string> {
			[files.Path.Combine(pagePath, "descriptor.json")] = JsonSerializer.Serialize(new { Descriptor = new { UId = pageUId, Name = options.PageName,
				ModifiedOnUtc = stamp, Parent = new { UId = ParentUId, Name = ParentName }, ManagerName = "ClientUnitSchemaManager", Caption = options.Caption } }, JsonOptions),
			[files.Path.Combine(pagePath, "metadata.json")] = $"= MetaData.Schema.UId \"{pageUId}\"\n= MetaData.Schema.A2 \"{options.PageName}\"\n= MetaData.Schema.A5 \"{packageUId}\"\n= MetaData.Schema.HD1 \"{ParentUId}\"\n",
			[files.Path.Combine(pagePath, "properties.json")] = "{\"Properties\":{\"OptionalProperties\":\"{}\",\"SchemaType\":\"EditViewModelSchema\"}}",
			[files.Path.Combine(pagePath, options.PageName + ".js")] = BuildBody(options.PageName, inputs, taskItems),
			[pageResourcePath] = pageResources.ToString()
		};
		// All identities, resource inputs and conflicts are checked before creating any artifacts.
		foreach ((string path, string content) in artifacts) {
			files.Directory.CreateDirectory(files.Path.GetDirectoryName(path));
			using var stream = files.FileStream.New(path, FileMode.CreateNew, FileAccess.Write);
			using var writer = new StreamWriter(stream);
			writer.Write(content);
		}
		if (hasIcons) {
			files.Directory.CreateDirectory(files.Path.GetDirectoryName(taskResourcePath));
			files.File.WriteAllText(taskResourcePath, taskResources.ToString());
		}
		task["FK11"] = pageUId.ToString();
		taskDescriptor["ModifiedOnUtc"] = stamp;
		files.File.WriteAllText(taskMetadataPath, taskRoot.ToJsonString(JsonOptions));
		files.File.WriteAllText(matches[0], taskDescriptorRoot.ToJsonString(JsonOptions));
		return files.Path.Combine(pagePath, options.PageName + ".js");
	}

	private static bool IsInput(JsonNode parameter) {
		int direction = parameter?["L12"]?.GetValue<int>() ?? 2;
		if (direction is < 0 or > 3) { throw new InvalidOperationException("Unsupported parameter direction metadata."); }
		return direction is 0 or 2;
	}
	private static void ValidateName(string name, string argument) {
		if (string.IsNullOrWhiteSpace(name) || name.Length > 250 || !NamePattern.IsMatch(name)) {
			throw new ArgumentException($"{argument} must be a valid schema identifier.");
		}
	}
	private JsonNode Read(string path) => JsonNode.Parse(files.File.ReadAllText(path)) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
	private static XDocument ReadXml(string value) {
		using var reader = XmlReader.Create(new StringReader(value), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
		return XDocument.Load(reader);
	}
	private static XDocument NewResources(string culture) => new(new XElement("Resources", new XAttribute("Culture", culture), new XElement("Group", new XAttribute("Type", "String"), new XElement("Items"))));
	private static XElement GetItems(XDocument resource) => resource.Root?.Elements("Group").Single(g => (string)g.Attribute("Type") == "String").Element("Items")
		?? throw new InvalidOperationException("Expected a native String resource group with Items.");
	private static void ValidateSvg(byte[] bytes) {
		if (bytes.Length > 1024 * 1024) { throw new ArgumentException("An SVG icon must be at most 1 MiB."); }
		XDocument svg = ReadXml(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
		if (svg.Root?.Name != XName.Get("svg", "http://www.w3.org/2000/svg")) { throw new ArgumentException("An icon must be an SVG document."); }
		string[] allowedElements = ["svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "title", "desc", "defs", "use"];
		if (svg.DescendantNodes().OfType<XProcessingInstruction>().Any()
			|| svg.Descendants().Any(e => e.Name.NamespaceName != "http://www.w3.org/2000/svg" || !allowedElements.Contains(e.Name.LocalName) || e.Attributes().Any(a =>
			a.Name == XNamespace.Xml + "base" ||
			a.Name.LocalName == "style" || a.Value.Contains("url(", StringComparison.OrdinalIgnoreCase)
			|| a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
			|| a.Name.LocalName == "href" && !a.Value.StartsWith('#')))) {
			throw new ArgumentException("SVG icons must use static basic shapes without styles, animation, event handlers or external references.");
		}
	}
	private static string BuildBody(string pageName, string[] inputs, XElement resources) {
		string Q(string value) => JsonSerializer.Serialize(value);
		string attributes = string.Join(",\n", inputs.Select(name => $"{Q(name)}: {{dataValueType: Terrasoft.DataValueType.MAPPING, type: Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN, initMethod: \"initPropertySilent\", doAutoSave: true}}"));
		var diff = new List<string> { "{operation: \"insert\", name: \"UserTaskContainer\", parentName: \"EditorsContainer\", propertyName: \"items\", values: {itemType: Terrasoft.ViewItemType.GRID_LAYOUT, items: []}}" };
		for (int index = 0; index < inputs.Length; index++) {
			string name = inputs[index];
			string caption = (string)resources.Elements("Item").FirstOrDefault(i => (string)i.Attribute("Name") == $"Parameters.{name}.Caption")?.Attribute("Value") ?? name;
			diff.Add($"{{operation: \"insert\", name: {Q(name)}, parentName: \"UserTaskContainer\", propertyName: \"items\", values: {{caption: {Q(caption)}, layout: {{column: 0, row: {index}, colSpan: 24}}, controlConfig: {{autocomplete: {Q(name + "Mapping")}}}, wrapClass: [\"top-caption-control\"]}}}}");
		}
		return $"/** Inherits {ParentName}. Customize this Classic page after scaffolding. */\ndefine({Q(pageName)}, [\"terrasoft\"], function(Terrasoft) {{\nreturn {{attributes: {{\n{attributes}\n}}, diff: /**SCHEMA_DIFF*/[\n{string.Join(",\n", diff)}\n]/**SCHEMA_DIFF*/}};\n}});\n";
	}
}
