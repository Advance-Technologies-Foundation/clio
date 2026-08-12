namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Acornima;
using Acornima.Ast;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json.Linq;

/// <summary>Options for the <c>get-classic-list-columns</c> command.</summary>
[Verb("get-classic-list-columns",
	HelpText = "Resolve the effective default column set of a Classic section list without changing Creatio data")]
public class GetClassicListColumnsOptions : EnvironmentOptions {

	/// <summary>Classic section client-unit schema name.</summary>
	[Option("schema-name", Required = true, HelpText = "Classic section schema name, for example 'ContactSectionV2'")]
	public string SchemaName { get; set; }
}

/// <summary>One resolved Classic list column.</summary>
public sealed record ClassicListColumnInfo(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Caption);

/// <summary>Response returned by <c>get-classic-list-columns</c>.</summary>
public sealed class GetClassicListColumnsResponse {

	/// <summary>Whether the section and its list-column fallback were resolved successfully.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>Requested Classic section schema.</summary>
	[JsonPropertyName("sectionSchema")]
	public string SectionSchema { get; set; }

	/// <summary>Entity schema bound to the Classic section.</summary>
	[JsonPropertyName("entity")]
	public string Entity { get; set; }

	/// <summary>Resolution source: <c>schema-default</c>, <c>entity-default</c>, or <c>none</c>.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; }

	/// <summary>Ordered effective default list columns.</summary>
	[JsonPropertyName("columns")]
	public IReadOnlyList<ClassicListColumnInfo> Columns { get; set; } = [];

	/// <summary>Non-fatal resolution details; empty when no details are needed.</summary>
	[JsonPropertyName("notes")]
	public IReadOnlyList<string> Notes { get; set; } = [];

	/// <summary>Failure reason when <see cref="Success"/> is <see langword="false"/>.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; set; }
}

/// <summary>Extracts static list-column and entity metadata from Classic section JavaScript bodies.</summary>
public interface IClassicListColumnParser {

	/// <summary>Returns distinct static column paths in their declaration order.</summary>
	/// <param name="schemaBodies">Classic schema bodies ordered from base to top replacing layer.</param>
	/// <returns>Static paths declared in <c>getGridDataColumns</c> or <c>initColumnsConfig</c>.</returns>
	IReadOnlyList<string> ParseColumns(IEnumerable<string> schemaBodies);

	/// <summary>Returns the most-derived entity schema name declared by the section hierarchy.</summary>
	/// <param name="schemaBodies">Classic schema bodies ordered from base to top replacing layer.</param>
	/// <returns>The entity schema name, or <see langword="null"/> when none is declared.</returns>
	string ParseEntityName(IEnumerable<string> schemaBodies);
}

/// <summary>Parses the static Classic section list metadata needed by the default-column resolver.</summary>
internal sealed class ClassicListColumnParser : IClassicListColumnParser {

	private static readonly string[] ColumnMethodNames = ["getGridDataColumns", "initColumnsConfig"];

	/// <inheritdoc />
	public IReadOnlyList<string> ParseColumns(IEnumerable<string> schemaBodies) {
		ArgumentNullException.ThrowIfNull(schemaBodies);
		var columns = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ObjectExpression[] schemas = schemaBodies
			.Where(body => !string.IsNullOrWhiteSpace(body))
			.Select(ParseSchemaObject)
			.Where(schema => schema is not null)
			.ToArray();
		foreach (string methodName in ColumnMethodNames) {
			Node[] methods = schemas
				.Select(schema => FindFunctionProperty(schema, methodName))
				.Where(method => method is not null)
				.ToArray();
			if (methods.Length == 0) {
				continue;
			}
			int firstEffectiveBody = methods.Length - 1;
			while (firstEffectiveBody > 0 && CallsParent(methods[firstEffectiveBody])) {
				firstEffectiveBody--;
			}
			for (int methodIndex = firstEffectiveBody; methodIndex < methods.Length; methodIndex++) {
				columns.AddRange(FindStaticColumnPaths(methods[methodIndex]).Where(seen.Add));
			}
		}
		return columns;
	}

	/// <inheritdoc />
	public string ParseEntityName(IEnumerable<string> schemaBodies) {
		ArgumentNullException.ThrowIfNull(schemaBodies);
		string entity = null;
		foreach (string body in schemaBodies.Where(body => !string.IsNullOrWhiteSpace(body))) {
			ObjectExpression schema = ParseSchemaObject(body);
			Property property = schema is null ? null : FindDirectProperty(schema, "entitySchemaName");
			if (property?.Value is Literal { Value: string name } && IsSchemaPath(name)) {
				entity = name;
			}
		}
		return entity;
	}

	private static ObjectExpression ParseSchemaObject(string source) {
		try {
			Script script = new Acornima.Parser().ParseScript(source);
			ObjectExpression schema = FindDefineFactorySchemaObject(script);
			if (schema is not null) {
				return schema;
			}
			try {
				script = new Acornima.Parser().ParseScript($"({{{source}}})");
			}
			catch (SyntaxErrorException) {
				return null;
			}
			return FindClassicSchemaObject(script);
		}
		catch (SyntaxErrorException) {
			try {
				return FindClassicSchemaObject(new Acornima.Parser().ParseScript($"({{{source}}})"));
			}
			catch (SyntaxErrorException) {
				return null;
			}
		}
	}

	private static ObjectExpression FindDefineFactorySchemaObject(Node root) {
		CallExpression defineCall = Descendants(root)
			.OfType<CallExpression>()
			.FirstOrDefault(call => call.Callee is Identifier { Name: "define" });
		Node factory = defineCall?.ChildNodes.FirstOrDefault(child => child is IFunction);
		if (factory is null) {
			return null;
		}
		return DescendantsSkippingNestedFunctions(factory)
			.OfType<ReturnStatement>()
			.Select(statement => statement.Argument)
			.OfType<ObjectExpression>()
			.FirstOrDefault(IsClassicSchemaObject);
	}

	private static ObjectExpression FindClassicSchemaObject(Node root) => Descendants(root)
		.OfType<ObjectExpression>()
		.FirstOrDefault(IsClassicSchemaObject);

	private static bool IsClassicSchemaObject(ObjectExpression expression) =>
		FindDirectProperty(expression, "entitySchemaName") is not null ||
		FindDirectProperty(expression, "methods") is not null ||
		FindDirectProperty(expression, "diff") is not null ||
		ColumnMethodNames.Any(methodName => FindDirectProperty(expression, methodName) is not null);

	private static Node FindFunctionProperty(ObjectExpression schema, string methodName) {
		Property property = FindDirectProperty(schema, methodName);
		if (property is null && FindDirectProperty(schema, "methods")?.Value is ObjectExpression methods) {
			property = FindDirectProperty(methods, methodName);
		}
		return property?.Value is IFunction ? property.Value : null;
	}

	private static IReadOnlyList<string> FindStaticColumnPaths(Node method) => DescendantsSkippingNestedFunctions(method)
		.OfType<Property>()
		.Where(property => PropertyName(property) is "path" or "bindTo")
		.Select(property => property.Value)
		.OfType<Literal>()
		.Select(literal => literal.Value as string)
		.Where(IsSchemaPath)
		.ToArray();

	private static bool CallsParent(Node method) => DescendantsSkippingNestedFunctions(method)
		.OfType<CallExpression>()
		.Any(call => call.Callee switch {
			Identifier { Name: "callParent" } => true,
			MemberExpression { Property: Identifier { Name: "callParent" }, Computed: false } => true,
			_ => false
		});

	private static Property FindDirectProperty(ObjectExpression expression, string name) => expression.Properties
		.OfType<Property>()
		.FirstOrDefault(property => string.Equals(PropertyName(property), name, StringComparison.Ordinal));

	private static string PropertyName(Property property) {
		if (property.Computed) return null;
		return property.Key switch {
			Identifier identifier => identifier.Name,
			Literal { Value: string value } => value,
			_ => null
		};
	}

	private static bool IsSchemaPath(string value) => !string.IsNullOrWhiteSpace(value) &&
		char.IsLetter(value[0]) && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '.');

	private static IEnumerable<Node> Descendants(Node root) => Walk(root, skipNestedFunctions: false);

	private static IEnumerable<Node> DescendantsSkippingNestedFunctions(Node root) =>
		Walk(root, skipNestedFunctions: true);

	// Pre-order walk over the AST. Children are buffered into a single reused list and pushed back-to-front so the
	// stack yields them in declaration order — Enumerable.Reverse would allocate a fresh buffer for every node, and
	// these walks run once per schema layer per column method over whole Classic section bodies.
	private static IEnumerable<Node> Walk(Node root, bool skipNestedFunctions) {
		var stack = new Stack<Node>();
		var children = new List<Node>();
		stack.Push(root);
		while (stack.Count > 0) {
			Node current = stack.Pop();
			yield return current;
			children.Clear();
			foreach (Node child in current.ChildNodes) {
				if (skipNestedFunctions && child is IFunction && !ReferenceEquals(child, root)) continue;
				children.Add(child);
			}
			for (int index = children.Count - 1; index >= 0; index--) {
				stack.Push(children[index]);
			}
		}
	}
}

/// <summary>Resolves the effective default Classic list columns through read-only Creatio APIs.</summary>
public interface IClassicListColumnResolver {

	/// <summary>Resolves the requested Classic section schema.</summary>
	/// <param name="sectionSchemaName">Classic section client-unit schema name.</param>
	/// <returns>A successful result including its resolution source.</returns>
	GetClassicListColumnsResponse Resolve(string sectionSchemaName);
}

/// <summary>Reads the Classic section hierarchy and resolves its default list-column source.</summary>
internal sealed class ClassicListColumnResolver(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPageDesignerHierarchyClient hierarchyClient,
	IRemoteEntitySchemaColumnManager columnManager,
	IClassicListColumnParser parser) : IClassicListColumnResolver {

	internal const string SchemaDefaultSource = "schema-default";
	internal const string EntityDefaultSource = "entity-default";
	internal const string NoneSource = "none";

	/// <inheritdoc />
	public GetClassicListColumnsResponse Resolve(string sectionSchemaName) {
		if (string.IsNullOrWhiteSpace(sectionSchemaName)) {
			throw new ArgumentException("schema-name is required", nameof(sectionSchemaName));
		}
		string normalizedName = sectionSchemaName.Trim();
		if (!PageSchemaMetadataHelper.IsValidSchemaName(normalizedName)) {
			throw new ArgumentException(PageSchemaMetadataHelper.SchemaNameFormatError, nameof(sectionSchemaName));
		}

		IReadOnlyList<PageDesignerHierarchySchema> hierarchy = ResolveHierarchy(normalizedName);
		string[] bodies = hierarchy
			.Reverse()
			.Select(schema => schema.Body)
			.Where(body => !string.IsNullOrWhiteSpace(body))
			.ToArray();
		string entity = parser.ParseEntityName(bodies);
		if (string.IsNullOrWhiteSpace(entity)) {
			throw new InvalidOperationException($"Classic section '{normalizedName}' does not declare entitySchemaName.");
		}

		EntitySchemaPropertiesInfo properties = columnManager.GetSchemaProperties(
			new GetEntitySchemaPropertiesOptions { SchemaName = entity });
		IReadOnlyList<string> schemaColumns = parser.ParseColumns(bodies);
		if (schemaColumns.Count > 0) {
			return Success(normalizedName, entity, SchemaDefaultSource,
				BuildColumnInfo(schemaColumns, properties.Columns), []);
		}
		if (!string.IsNullOrWhiteSpace(properties.PrimaryDisplayColumnName)) {
			return Success(normalizedName, entity, EntityDefaultSource,
				BuildColumnInfo([properties.PrimaryDisplayColumnName], properties.Columns),
				["The section schema does not define static list columns; using the entity primary display column."]);
		}
		return Success(normalizedName, entity, NoneSource, [],
			["The section schema does not define static list columns and the entity has no primary display column."]);
	}

	private IReadOnlyList<PageDesignerHierarchySchema> ResolveHierarchy(string schemaName) {
		(JToken metadata, string metadataError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			applicationClient, serviceUrlBuilder, schemaName,
			("UId", "UId"), ("PackageUId", "SysPackage.UId"));
		if (metadata is null) {
			throw new InvalidOperationException(metadataError ?? $"Classic section schema '{schemaName}' was not found.");
		}
		string schemaUId = metadata["UId"]?.ToString();
		string packageUId = metadata["PackageUId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
			throw new InvalidOperationException($"Classic section schema '{schemaName}' metadata is incomplete.");
		}
		string designPackageUId;
		try {
			designPackageUId = hierarchyClient.GetDesignPackageUId(schemaUId);
		}
		catch (Exception) {
			// Best-effort, mirroring GetClassicPageSourcesCommand.ResolveHierarchyBaseToTop: the designer call
			// parses its response as JSON, so an expired session (HTML error page) surfaces as a parser/transport
			// exception rather than InvalidOperationException. The schema's own package is a valid anchor.
			designPackageUId = packageUId;
		}
		IReadOnlyList<PageDesignerHierarchySchema> initial =
			hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		if (initial.Count == 0) {
			throw new InvalidOperationException($"Classic section schema '{schemaName}' hierarchy is empty.");
		}
		string rootSchemaUId = initial
			.LastOrDefault(schema => string.Equals(schema.Name, schemaName, StringComparison.OrdinalIgnoreCase))?.UId;
		if (string.IsNullOrWhiteSpace(rootSchemaUId) ||
			string.Equals(rootSchemaUId, schemaUId, StringComparison.OrdinalIgnoreCase)) {
			return initial;
		}
		IReadOnlyList<PageDesignerHierarchySchema> full =
			hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
		return full.Count > 0 ? full : initial;
	}

	private static IReadOnlyList<ClassicListColumnInfo> BuildColumnInfo(
		IEnumerable<string> paths,
		IReadOnlyList<EntitySchemaPropertyColumnInfo> metadata) {
		var captions = (metadata ?? [])
			.Where(column => !string.IsNullOrWhiteSpace(column.Name))
			.GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.First().Title, StringComparer.OrdinalIgnoreCase);
		return paths.Select(path => new ClassicListColumnInfo(path,
			captions.TryGetValue(path, out string caption) ? caption : null)).ToArray();
	}

	private static GetClassicListColumnsResponse Success(
		string sectionSchema,
		string entity,
		string source,
		IReadOnlyList<ClassicListColumnInfo> columns,
		IReadOnlyList<string> notes) => new() {
			Success = true,
			SectionSchema = sectionSchema,
			Entity = entity,
			Source = source,
			Columns = columns,
			Notes = notes
		};
}

/// <summary>Prints the effective default columns of a Classic section list as JSON.</summary>
public class GetClassicListColumnsCommand(IClassicListColumnResolver resolver, ILogger logger)
	: Command<GetClassicListColumnsOptions> {

	/// <summary>Resolves the list-column result without writing to the target environment.</summary>
	/// <param name="options">Command options containing the section schema name.</param>
	/// <param name="response">Resolved response or a failure envelope.</param>
	/// <returns><see langword="true"/> when resolution completed successfully.</returns>
	public virtual bool TryResolve(
		GetClassicListColumnsOptions options,
		out GetClassicListColumnsResponse response) {
		string schemaName = options?.SchemaName;
		try {
			ArgumentNullException.ThrowIfNull(options);
			response = resolver.Resolve(schemaName);
			return true;
		}
		catch (Exception exception) {
			response = new GetClassicListColumnsResponse {
				Success = false,
				SectionSchema = schemaName,
				Columns = [],
				Notes = [],
				Error = exception.Message
			};
			return false;
		}
	}

	/// <inheritdoc />
	public override int Execute(GetClassicListColumnsOptions options) {
		bool success = TryResolve(options, out GetClassicListColumnsResponse response);
		logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}
}
