namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Acornima;
using Acornima.Ast;

/// <summary>Static list-column metadata parsed out of a Classic section hierarchy.</summary>
/// <param name="Columns">Distinct static column paths in base-to-top declaration order.</param>
/// <param name="UnparsedLayerCount">
/// Number of schema bodies that survived neither parse attempt and were therefore skipped. A non-zero value
/// means the column set may be incomplete, so the caller can report the degradation instead of presenting a
/// partial — or entity-default — answer as if the whole hierarchy had been read.
/// </param>
public sealed record ClassicListColumnParseResult(IReadOnlyList<string> Columns, int UnparsedLayerCount);

/// <summary>Extracts static list-column and entity metadata from Classic section JavaScript bodies.</summary>
public interface IClassicListColumnParser {

	/// <summary>Returns distinct static column paths in their declaration order.</summary>
	/// <param name="schemaBodies">Classic schema bodies ordered from base to top replacing layer.</param>
	/// <returns>Static paths declared in <c>getGridDataColumns</c> or <c>initColumnsConfig</c>, plus the count
	/// of bodies that could not be parsed.</returns>
	ClassicListColumnParseResult ParseColumns(IEnumerable<string> schemaBodies);

	/// <summary>Returns the most-derived entity schema name declared by the section hierarchy.</summary>
	/// <param name="schemaBodies">Classic schema bodies ordered from base to top replacing layer.</param>
	/// <returns>The entity schema name, or <see langword="null"/> when none is declared.</returns>
	string ParseEntityName(IEnumerable<string> schemaBodies);
}

/// <summary>Parses the static Classic section list metadata needed by the default-column resolver.</summary>
internal sealed class ClassicListColumnParser : IClassicListColumnParser {

	// Ordered merge list for ParseColumns. A section that declares both methods surfaces getGridDataColumns
	// first; the rule is documented in clio/docs/commands/get-classic-list-columns.md and pinned by
	// ParseColumns_ShouldMergeGridDataColumnsFirst_WhenOneSchemaDeclaresBothMethods.
	private static readonly string[] ColumnMethodNames = ["getGridDataColumns", "initColumnsConfig"];

	// Markers that identify a Classic section schema object. Deliberately a separate list from
	// ColumnMethodNames: adding a column method must not widen what counts as a schema object, or
	// FindDefineFactorySchemaObject / FindClassicSchemaObject could anchor on a different object in the same
	// body and produce wrong or empty columns with no parse error.
	private static readonly string[] SchemaMarkerNames = [
		"entitySchemaName", "methods", "diff", "getGridDataColumns", "initColumnsConfig"
	];

	/// <inheritdoc />
	public ClassicListColumnParseResult ParseColumns(IEnumerable<string> schemaBodies) {
		ArgumentNullException.ThrowIfNull(schemaBodies);
		var columns = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ObjectExpression[] parsed = schemaBodies
			.Where(body => !string.IsNullOrWhiteSpace(body))
			.Select(ParseSchemaObject)
			.ToArray();
		ObjectExpression[] schemas = parsed.Where(schema => schema is not null).ToArray();
		int unparsedLayerCount = parsed.Length - schemas.Length;
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
		return new ClassicListColumnParseResult(columns, unparsedLayerCount);
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
		SchemaMarkerNames.Any(marker => FindDirectProperty(expression, marker) is not null);

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
