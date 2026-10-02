using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command;

/// <summary>Shared environment selection for read-only dependency operations.</summary>
public abstract class PackageExplorerOptions : RemoteCommandOptions { }

/// <summary>Reads direct or transitive dependencies or dependants.</summary>
[Verb("get-pkg-dependencies", HelpText = "Read package dependencies using Creatio contract v1")]
public sealed class GetPackageDependenciesOptions : PackageExplorerOptions {
	/// <summary>Package name or UId.</summary>
	[Value(0, Required = true, MetaName = "package")]
	public string Package { get; set; }
	/// <summary>Traverse reverse edges.</summary>
	[Option("dependants", HelpText = "List packages depending on this package")]
	public bool Dependants { get; set; }
	/// <summary>Include indirect dependencies.</summary>
	[Option("transitive", HelpText = "Include indirect dependencies")]
	public bool Transitive { get; set; }
}

/// <summary>Finds a shortest directed dependency path.</summary>
[Verb("pkg-dependency-path", HelpText = "Explain a directed dependency path")]
public sealed class PackageDependencyPathOptions : PackageExplorerOptions {
	/// <summary>Depending package name or UId.</summary>
	[Value(0, Required = true, MetaName = "from")]
	public string From { get; set; }
	/// <summary>Dependency package name or UId.</summary>
	[Value(1, Required = true, MetaName = "to")]
	public string To { get; set; }
}

/// <summary>Explains known metadata references.</summary>
[Verb("pkg-dependency-why", HelpText = "Explain known reasons for a package dependency")]
public sealed class PackageDependencyWhyOptions : PackageExplorerOptions {
	/// <summary>Depending package name or UId.</summary>
	[Value(0, Required = true, MetaName = "from")]
	public string From { get; set; }
	/// <summary>Dependency package name or UId.</summary>
	[Value(1, Required = true, MetaName = "to")]
	public string To { get; set; }
	/// <summary>Return individual evidence records.</summary>
	[Option("details", HelpText = "Include individual reasons instead of counts only")]
	public bool Details { get; set; }
	/// <summary>Request scoped removal validation.</summary>
	[Option("check-removal", HelpText = "Also assess removing this direct edge; never removes it")]
	public bool CheckRemoval { get; set; }
}

/// <summary>Searches schema owners or resolves entity context.</summary>
[Verb("find-pkg-by-schema", HelpText = "Find schema owners or resolve a package's entity context")]
public sealed class FindPackageBySchemaOptions : PackageExplorerOptions {
	/// <summary>Literal schema name.</summary>
	[Value(0, Required = true, MetaName = "schema")]
	public string Schema { get; set; }
	/// <summary>Optional schema manager filter.</summary>
	[Option("manager-name", HelpText = "Schema manager, for example EntitySchemaManager")]
	public string ManagerName { get; set; }
	/// <summary>Optional package context.</summary>
	[Option("package-name", HelpText = "Resolve the entity schema in this package, by name or UId")]
	public string Package { get; set; }
	/// <summary>Reference or extension purpose.</summary>
	[Option("purpose", Default = "reference", HelpText = "reference or extend, with --package-name")]
	public string Purpose { get; set; } = "reference";
	/// <summary>Use literal contains instead of exact matching.</summary>
	[Option("contains", HelpText = "Literal partial search; cannot be combined with package context")]
	public bool Contains { get; set; }
	/// <summary>Maximum search rows.</summary>
	[Option("limit", Default = 200, HelpText = "Maximum 1-200 rows; hasMore reports omitted results")]
	public int Limit { get; set; } = 200;
}

/// <summary>Exports the complete server graph.</summary>
[Verb("export-pkg-graph", HelpText = "Export package graph as JSON or DOT")]
public sealed class ExportPackageGraphOptions : PackageExplorerOptions {
	/// <summary>Output format.</summary>
	[Option("format", Default = "json", HelpText = "json or dot")]
	public string Format { get; set; } = "json";
}

/// <summary>Previews platform dependency validation without a write.</summary>
[Verb("check-pkg-dependency", HelpText = "Assess adding or removing a dependency without changing it")]
public sealed class CheckPackageDependencyOptions : PackageExplorerOptions {
	/// <summary>Depending package name or UId.</summary>
	[Value(0, Required = true, MetaName = "from")]
	public string From { get; set; }
	/// <summary>Dependency package name or UId.</summary>
	[Value(1, Required = true, MetaName = "to")]
	public string To { get; set; }
	/// <summary>Change to assess.</summary>
	[Option("action", Default = "add", HelpText = "add or remove; performs no mutation")]
	public string Action { get; set; } = "add";
}

/// <summary>Executes dependency queries and formats platform evidence for people and agents.</summary>
public sealed class PackageExplorerCommand(IPackageExplorerClient client, ILogger logger) : Command<PackageExplorerOptions> {
	/// <inheritdoc />
	public override int Execute(PackageExplorerOptions options) {
		try {
			JObject result;
			if (options is FindPackageBySchemaOptions search && string.IsNullOrWhiteSpace(search.Package)) {
				ValidateSearch(search);
				result = client.Read(ServiceUrlBuilder.KnownRoute.DependencySearchV1, new JObject {
					["schemaName"] = search.Schema, ["managerName"] = search.ManagerName,
					["matchMode"] = search.Contains ? "contains" : "exact", ["limit"] = search.Limit
				});
			} else {
				JObject graph = client.Read(ServiceUrlBuilder.KnownRoute.DependencyGraphV1, new JObject());
				JArray packages = RequireArray(graph, "packages");
				JArray edges = RequireArray(graph, "dependencies");
				result = options switch {
					GetPackageDependenciesOptions dependencies => GetDependencies(graph, packages, edges, dependencies),
					PackageDependencyPathOptions path => GetPath(packages, edges, path),
					PackageDependencyWhyOptions why => GetReasons(packages, why),
					FindPackageBySchemaOptions context => ResolveContext(packages, context),
					CheckPackageDependencyOptions check => CheckDependency(packages, check),
					ExportPackageGraphOptions => graph,
					_ => throw new ArgumentException("Unknown dependency command.")
				};
				if (options is ExportPackageGraphOptions export) {
					if (export.Format is not ("json" or "dot")) {
						throw new ArgumentException("Format must be json or dot.");
					}
					if (export.Format == "dot") {
						logger.WriteInfo(ToDot(packages, edges));
						return 0;
					}
				}
			}
			logger.WriteInfo(result.ToString(Formatting.Indented));
			return 0;
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
			or NotSupportedException or TimeoutException or JsonException or System.Net.Http.HttpRequestException
			or System.Threading.Tasks.TaskCanceledException) {
			logger.WriteError(SensitiveErrorTextRedactor.Redact(exception.Message));
			return 1;
		}
	}

	private static JArray RequireArray(JObject value, string name) => value[name] as JArray
		?? throw new InvalidOperationException("Incomplete dependency response: missing " + name + ".");

	private static JObject Package(JArray packages, string name) {
		JObject[] matches = packages.OfType<JObject>().Where(package =>
			string.Equals((string)package["name"], name, StringComparison.OrdinalIgnoreCase)
			|| string.Equals((string)package["uId"], name, StringComparison.OrdinalIgnoreCase)).ToArray();
		return matches.Length == 1 ? matches[0] : throw new ArgumentException(
			matches.Length == 0 ? $"Package '{name}' not found." : $"Package '{name}' is ambiguous; use its UId.");
	}


	private static JObject GetDependencies(JObject graph, JArray packages, JArray edges, GetPackageDependenciesOptions options) {
		JObject package = Package(packages, options.Package);
		string id = (string)package["uId"];
		var reached = PackageGraphTraversal.Traverse(edges, id, options.Dependants, options.Transitive);
		return new JObject {
			["package"] = package.DeepClone(), ["direction"] = options.Dependants ? "dependants" : "dependencies",
			["transitive"] = options.Transitive, ["graphRevision"] = graph["graphRevision"]?.DeepClone(),
			["packages"] = new JArray(packages.OfType<JObject>().Where(item => (string)item["uId"] != id
				&& reached.ContainsKey((string)item["uId"])).Select(item => item.DeepClone())),
			["cycles"] = graph["cycles"]?.DeepClone(), ["missingDependencies"] = graph["missingDependencies"]?.DeepClone()
		};
	}

	private static JObject GetPath(JArray packages, JArray edges, PackageDependencyPathOptions options) {
		string from = (string)Package(packages, options.From)["uId"];
		string to = (string)Package(packages, options.To)["uId"];
		var parents = PackageGraphTraversal.Traverse(edges, from, false, true);
		var path = new List<string>();
		if (parents.ContainsKey(to)) {
			for (string current = to; current != null; current = parents[current]) { path.Add(current); }
			path.Reverse();
		}
		return new JObject { ["reachable"] = path.Count > 0,
			["path"] = new JArray(path.Select(id => Package(packages, id).DeepClone())) };
	}

	private static JObject Pair(JArray packages, string from, string to) => new() {
		["packageUId"] = Package(packages, from)["uId"]?.DeepClone(),
		["dependOnPackageUId"] = Package(packages, to)["uId"]?.DeepClone()
	};

	private JObject GetReasons(JArray packages, PackageDependencyWhyOptions options) {
		JObject pair = Pair(packages, options.From, options.To);
		JObject result = client.Read(ServiceUrlBuilder.KnownRoute.DependencyReasonsV1, pair);
		JArray reasons = RequireArray(result, "reasons");
		result["reasonCount"] = reasons.Count;
		if (!options.Details) {
			result["reasonCounts"] = new JArray(reasons.OfType<JObject>().GroupBy(reason => (string)reason["kind"])
				.Select(group => new JObject { ["kind"] = group.Key, ["count"] = group.Count() }));
			result.Remove("reasons");
		}
		if (options.CheckRemoval) { result["dropImpact"] = client.Read(ServiceUrlBuilder.KnownRoute.DependencyDropV1, pair); }
		return result;
	}

	private static void ValidateSearch(FindPackageBySchemaOptions options) {
		if (string.IsNullOrWhiteSpace(options.Schema) || options.Limit is < 1 or > 200
				|| options.Purpose is not ("reference" or "extend")) {
			throw new ArgumentException("Supply a schema, limit 1-200, and purpose reference or extend.");
		}
	}

	private JObject ResolveContext(JArray packages, FindPackageBySchemaOptions options) {
		ValidateSearch(options);
		if (options.Contains || options.Limit != 200) {
			throw new ArgumentException("Context resolution requires an exact schema and the default limit.");
		}
		return client.Read(ServiceUrlBuilder.KnownRoute.DependencyResolveV1, new JObject {
			["packageUId"] = Package(packages, options.Package)["uId"]?.DeepClone(),
			["schemaName"] = options.Schema, ["managerName"] = options.ManagerName ?? "EntitySchemaManager",
			["purpose"] = options.Purpose
		});
	}

	private JObject CheckDependency(JArray packages, CheckPackageDependencyOptions options) {
		if (options.Action is not ("add" or "remove")) { throw new ArgumentException("Action must be add or remove."); }
		return client.Read(options.Action == "add" ? ServiceUrlBuilder.KnownRoute.DependencyAddV1
			: ServiceUrlBuilder.KnownRoute.DependencyDropV1, Pair(packages, options.From, options.To));
	}

	private static string ToDot(JArray packages, JArray edges) {
		IEnumerable<string> nodes = packages.OfType<JObject>().Select(package =>
			$"  {JsonConvert.SerializeObject((string)package["uId"])} [label={JsonConvert.SerializeObject((string)package["name"])}];");
		IEnumerable<string> links = edges.OfType<JObject>().Select(edge =>
			$"  {JsonConvert.SerializeObject((string)edge["packageUId"])} -> {JsonConvert.SerializeObject((string)edge["dependOnPackageUId"])};");
		return "digraph dependencies {\n" + string.Join("\n", nodes.Concat(links)) + "\n}";
	}
}
