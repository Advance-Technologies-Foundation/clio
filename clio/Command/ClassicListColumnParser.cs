namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Acornima;
using Acornima.Ast;

/// <summary>Static list-column metadata parsed out of a Classic section hierarchy.</summary>
/// <param name="Columns">Distinct static column paths in base-to-top declaration order.</param>
/// <param name="UnparsedLayerCount">
/// Number of schema bodies that were skipped, for either reason in <paramref name="UnanchoredLayerCount"/>'s
/// note. A non-zero value means the column set may be incomplete, so the caller can report the degradation
/// instead of presenting a partial — or entity-default — answer as if the whole hierarchy had been read.
/// </param>
/// <param name="UnanchoredLayerCount">
/// How many of <paramref name="UnparsedLayerCount"/> parsed as valid JavaScript but exposed no anchorable
/// Classic schema object (a factory assigning to a local, say). Kept apart from the genuine syntax errors
/// because the two need different wording: "could not be parsed as JavaScript" is an affirmatively wrong
/// statement about a body that parsed fine, and it points a consumer at the wrong problem.
/// </param>
/// <param name="ColumnOrigins">
/// Which method declared each path in <paramref name="Columns"/>, keyed case-insensitively:
/// <c>getGridDataColumns</c>, <c>initColumnsConfig</c>, or <c>both</c>. The two are NOT interchangeable in
/// Classic — <c>initColumnsConfig</c> describes what the grid RENDERS, <c>getGridDataColumns</c> what the
/// section LOADS — and the flattened merge cannot express that on its own. Carrying the origin lets a
/// consumer take the rendered set, the loaded set, or the union under its own fidelity rules instead of
/// inheriting this parser's merge order as if it were a ruling.
/// </param>
/// <param name="DeclaresBothColumnMethods">
/// <see langword="true"/> when the hierarchy declares both column methods, which is exactly the case where
/// the flattened list is an approximation: overlapping paths take their order from <c>getGridDataColumns</c>,
/// and a full <c>initColumnsConfig</c> override does not suppress ancestor <c>getGridDataColumns</c> columns.
/// The resolver turns this into a response note so the approximation is visible at runtime, not only in the doc.
/// </param>
/// <param name="SubtractiveLayerCount">
/// How many effective layers compose their parent and then <c>delete</c> a key off the composed object. The
/// composition here is additive only, so such a layer contributes no literal of its own and the column it
/// removes SURVIVES in the reported set. Counting it lets the resolver say so at runtime instead of
/// presenting a confidently wrong set under <c>source: "schema-default"</c>. Subtraction is not applied —
/// see the composition section of clio/docs/commands/get-classic-list-columns.md.
/// </param>
public sealed record ClassicListColumnParseResult(
	IReadOnlyList<string> Columns,
	int UnparsedLayerCount,
	int UnanchoredLayerCount = 0,
	IReadOnlyDictionary<string, string> ColumnOrigins = null,
	bool DeclaresBothColumnMethods = false,
	int SubtractiveLayerCount = 0);

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

	/// <summary>Origin recorded for a path that BOTH column methods declare.</summary>
	internal const string BothColumnMethods = "both";

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
		var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		int declaringMethodCount = 0;
		int subtractiveLayerCount = 0;
		LayerParse[] parsed = schemaBodies
			.Where(body => !string.IsNullOrWhiteSpace(body))
			.Select(ParseLayer)
			.ToArray();
		ObjectExpression[] schemas = parsed.Where(layer => layer.Schema is not null)
			.Select(layer => layer.Schema).ToArray();
		int unparsedLayerCount = parsed.Length - schemas.Length;
		int unanchoredLayerCount = parsed.Count(layer => layer.Schema is null && !layer.SyntaxError);
		foreach (string methodName in ColumnMethodNames) {
			Node[] methods = schemas
				.Select(schema => FindFunctionProperty(schema, methodName))
				.Where(method => method is not null)
				.ToArray();
			if (methods.Length == 0) {
				continue;
			}
			declaringMethodCount++;
			int firstEffectiveBody = methods.Length - 1;
			while (firstEffectiveBody > 0 && CallsParent(methods[firstEffectiveBody])) {
				firstEffectiveBody--;
			}
			for (int methodIndex = firstEffectiveBody; methodIndex < methods.Length; methodIndex++) {
				if (RemovesComposedColumns(methods[methodIndex])) {
					subtractiveLayerCount++;
				}
				foreach (string path in FindStaticColumnPaths(methods[methodIndex])) {
					// Order and membership stay exactly as before — first declaration wins, getGridDataColumns
					// first. What changes is that a path the SECOND method also declares is still recorded here
					// rather than dropped with the duplicate, so `both` is distinguishable from `loaded only`.
					if (seen.Add(path)) {
						columns.Add(path);
					}
					origins[path] = origins.TryGetValue(path, out string existing) && existing != methodName
						? BothColumnMethods
						: methodName;
				}
			}
		}
		return new ClassicListColumnParseResult(columns, unparsedLayerCount, unanchoredLayerCount, origins,
			declaringMethodCount == ColumnMethodNames.Length, subtractiveLayerCount);
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

	/// <summary>One schema body's parse outcome, keeping the two failure reasons apart.</summary>
	/// <param name="Schema">The anchored Classic schema object, or <see langword="null"/>.</param>
	/// <param name="SyntaxError">
	/// <see langword="true"/> only when the body itself is not valid JavaScript. A body that parses cleanly
	/// but exposes no anchorable schema object is a DIFFERENT failure, and reporting it as a syntax error
	/// sends the caller looking for a broken body that is not broken.
	/// </param>
	private readonly record struct LayerParse(ObjectExpression Schema, bool SyntaxError);

	private static LayerParse ParseLayer(string source) {
		// Acornima's stack guard raises InsufficientExecutionStackException on deeply nested input rather than
		// a SyntaxErrorException — McpServer/Tools/PageBodySyntaxValidator catches it explicitly for this same
		// input class. Without it here, one truncated or deeply nested layer fails the WHOLE read with a raw
		// CLR message instead of being counted and surfaced through the skipped-layers note.
		try {
			Script script = new Acornima.Parser().ParseScript(source);
			ObjectExpression schema = FindDefineFactorySchemaObject(script);
			if (schema is not null) {
				return new LayerParse(schema, false);
			}
			// The body is valid JavaScript; the re-wrap is only a second way to anchor a bare property list.
			try {
				script = new Acornima.Parser().ParseScript($"({{{source}}})");
			}
			catch (SyntaxErrorException) {
				return new LayerParse(null, false);
			}
			catch (InsufficientExecutionStackException) {
				return new LayerParse(null, false);
			}
			return new LayerParse(FindClassicSchemaObject(script), false);
		}
		catch (SyntaxErrorException) {
			try {
				return new LayerParse(
					FindClassicSchemaObject(new Acornima.Parser().ParseScript($"({{{source}}})")), true);
			}
			catch (SyntaxErrorException) {
				return new LayerParse(null, true);
			}
			catch (InsufficientExecutionStackException) {
				return new LayerParse(null, true);
			}
		}
		catch (InsufficientExecutionStackException) {
			return new LayerParse(null, true);
		}
	}

	private static ObjectExpression ParseSchemaObject(string source) => ParseLayer(source).Schema;

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

	// Scope limitation, deliberate and pinned by a test: only a `callParent` CALL counts. A Classic override
	// written as an arrow function still calls callParent the same way, so the shape of the override itself is
	// irrelevant here — what would NOT be detected is an override that composes its parent by some other means
	// (Ext.callParent aliased through a local, say). Such a layer reads as a full override and truncates the walk.
	private static bool CallsParent(Node method) => DescendantsSkippingNestedFunctions(method)
		.OfType<CallExpression>()
		.Any(call => call.Callee switch {
			Identifier { Name: "callParent" } => true,
			MemberExpression { Property: Identifier { Name: "callParent" }, Computed: false } => true,
			_ => false
		});

	// Detects a SUBTRACTIVE override — `var c = this.callParent(arguments); delete c.StartDate; return c;`. The
	// composition in ParseColumns is additive only, so such a layer declares no literal of its own and the
	// removed column survives in the reported set. Subtraction is deliberately NOT applied: the shape is
	// attested in a real Classic DETAIL schema, but whether a Classic SECTION uses it is unverified, and
	// teaching the walk to subtract is a behaviour change on unverified need. Counting it instead makes the
	// degradation visible through a note rather than silent.
	//
	// Any `delete` on a member expression inside an effective column method counts. Narrowing it to the exact
	// identifier that received callParent's result would need alias tracking, and would quietly stop counting
	// the shapes it fails to follow — over-counting produces a cautious note, under-counting produces the
	// silence this exists to remove.
	private static bool RemovesComposedColumns(Node method) => DescendantsSkippingNestedFunctions(method)
		.OfType<UnaryExpression>()
		.Any(expression => expression.Operator == Operator.Delete && expression.Argument is MemberExpression);

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
