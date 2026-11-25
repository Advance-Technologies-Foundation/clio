using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Acornima.Ast;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Code-quality rules of the page-body lint pass. They read the page-schema sections (<c>handlers</c>,
/// <c>converters</c>, <c>validators</c>) of the object the page factory returns, and - through
/// <see cref="LintClientModule"/> - a client module's body, where a page's logic lives (validate-page lints one module
/// body per call; the toolkit gate follows a page's imports to every module). Every one of them is a
/// <see cref="LintSeverity.Warning"/>: the page works, it is just hard to maintain.
///
/// The rule ids and thresholds are shared with the Classic-to-Freedom migration engine of the CAADT toolkit,
/// which applies the same rules as a blocking gate on the pages it builds. A rule changed here is changed
/// there too, or the save-time warning and the migration gate start to disagree.
/// </summary>
internal static partial class PageBodyAstLinter {

	#region Rule identifiers and thresholds

	internal const string RuleFunctionTooLong = "function-too-long";
	internal const string RuleHandlerTooManyNestedFunctions = "handler-too-many-nested-functions";
	internal const string RuleDuplicateFunctionInPage = "duplicate-function-in-page";
	internal const string RuleCatchLogOnly = "catch-log-only";
	internal const string RuleNumericComparisonType = "numeric-comparison-type";
	internal const string RuleSequentialIndependentAwaits = "sequential-independent-awaits";
	internal const string RuleGuidLiteralDataReference = "guid-literal-data-reference";
	internal const string RuleHandlerWithoutNext = "handler-without-next";

	/// <summary>A function in a section longer than this many lines is reported.</summary>
	internal const int MaxFunctionLines = 60;

	/// <summary>A handler declaring more named functions than this inside its body is reported.</summary>
	internal const int MaxNestedFunctionsPerHandler = 3;

	/// <summary>
	/// A block running more awaited model loads than this one after another, none using another's result, is
	/// reported.
	/// </summary>
	internal const int MaxSequentialIndependentAwaits = 2;

	// A named helper smaller than this many AST nodes (`v => v`) is not worth moving to a module, so two copies
	// of it are not reported.
	private const int MinDuplicateFunctionNodes = 8;

	private const string ModuleHelperAdvice =
		"Move shared helpers to a client module declared in SCHEMA_DEPS (MCP guide: shared-client-logic).";

	#endregion

	#region Entry point

	private static void CheckCodeQuality(HashSet<Node> sections, LintFindingBudget budget) {
		Dictionary<string, List<NamedFunction>> helpersByShape = new(StringComparer.Ordinal);
		foreach (Property section in sections.OfType<Property>()) {
			CheckSectionNode(section.Value, new QualityScope(IsElementId: false, InModule: false, InConstantData: false), depth: 0, budget);
			if (TryGetStaticPropertyName(section) == "handlers" && section.Value is ArrayExpression handlers) {
				foreach (Node entry in handlers.Elements) {
					if (entry is ObjectExpression handlerEntry && TryGetHandlerFunction(handlerEntry, out IFunction handler)) {
						CheckHandler(handler, helpersByShape, budget);
						CheckHandlerCallsNext(handler, RequestOf(handlerEntry), budget);
					}
				}
			}
		}
		ReportDuplicateHelpers(helpersByShape, budget);
	}

	/// <summary>
	/// Lints a client module (<c>define("Name", [deps], function(...) { ... })</c>) with the code-quality rules that
	/// apply outside a page section: function length, log-only catch blocks, bare numeric comparison types and
	/// sequential independent loads, and record ids. A record id held by a `const` whose value is data (a literal, an
	/// object or array literal, or <c>Object.freeze(...)</c> of one) is not reported: that is the named constant the
	/// rule asks for. Returns null when the script is not a client module, so the caller can lint it as a page instead.
	/// </summary>
	public static IReadOnlyList<PageBodyLintFinding> LintClientModule(Script ast) {
		if (ast is null || FindModuleFactory(ast) is not { } factory) {
			return null;
		}
		LintFindingBudget budget = new();
		CheckSectionNode(factory.Body, new QualityScope(IsElementId: false, InModule: true, InConstantData: false), depth: 0, budget);
		CheckSequentialAwaits(factory.Body, depth: 0, budget);
		budget.AppendSummaries();
		return budget.Findings;
	}

	// The config sections only a page factory returns. `handlers`, `converters` and `validators` (SectionPropertyNames)
	// are also names a shared module may export, so one of them alone does not make a page.
	private static readonly HashSet<string> PageConfigKeys = new(StringComparer.Ordinal) {
		"viewConfigDiff", "viewConfig", "viewModelConfigDiff", "viewModelConfig", "modelConfigDiff", "modelConfig"
	};

	// The factory of the script's top-level `define(...)` call; null when the script has none, or when the factory
	// returns page sections (a page body is not a client module).
	private static IFunction FindModuleFactory(Script ast) {
		foreach (Statement statement in ast.Body) {
			if (statement is ExpressionStatement {
					Expression: CallExpression {Callee: Identifier {Name: DefineGlobalName}} call
				}) {
				IFunction factory = call.Arguments.OfType<IFunction>().FirstOrDefault();
				return factory is null || ReturnsPageSections(factory) ? null : factory;
			}
		}
		return null;
	}

	// A `define` whose factory returns page sections is a page, even with its `/**SCHEMA_*/` markers lost, and must be
	// validated as one: the returned object (found the way the page rules find it) has a page config section, or two
	// or more of handlers / converters / validators. A module exporting only `{ handlers: [...] }` stays a module.
	private static bool ReturnsPageSections(IFunction factory) =>
		ReturnedObjectLiterals(factory).Any(returned => {
			List<string> keys = returned.Properties.OfType<Property>()
				.Select(TryGetStaticPropertyName).Where(key => key is not null).ToList();
			return keys.Any(PageConfigKeys.Contains) || keys.Count(key => Array.IndexOf(SectionPropertyNames, key) >= 0) >= 2;
		});

	private static bool TryGetHandlerFunction(ObjectExpression entry, out IFunction handler) {
		handler = null;
		foreach (Node element in entry.Properties) {
			if (TryGetEntryProperty(element, out Property prop, out string key) && key == "handler"
				&& prop.Value is IFunction function) {
				handler = function;
				return true;
			}
		}
		return false;
	}

	// The request a handler entry answers, or null when it is not a string literal.
	private static string RequestOf(ObjectExpression entry) {
		foreach (Node element in entry.Properties) {
			if (TryGetEntryProperty(element, out Property prop, out string key) && key == "request"
				&& prop.Value is Literal {Value: string request}) {
				return request;
			}
		}
		return null;
	}

	#endregion

	#region Section-wide rules

	// The keys that make an object a page element (a list column, a field) rather than data: its `id` is a
	// generated local id, which only has to be unique on the page, not a record id.
	private static readonly HashSet<string> ElementKeys = new(StringComparer.Ordinal) {
		"code", "path", "caption", "title", "width"
	};

	// Where a node sits for the single-node rules: `IsElementId` marks the value of an element-shaped object's `id`;
	// `InConstantData` marks the value of a client module's `const` data declaration, where a named record id belongs.
	private readonly record struct QualityScope(bool IsElementId, bool InModule, bool InConstantData);

	// One walk over a section for the rules that look at single nodes. An element-shaped object's `id` is told apart
	// from a record id used as data; an `id` beside `displayValue` or `type` is data and is reported.
	private static void CheckSectionNode(Node node, QualityScope scope, int depth, LintFindingBudget budget) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		switch (node) {
			case Property {Value: Literal} elementIdProperty when scope.IsElementId:
				// The flag belongs to this property's value: walk only that, so no other key inherits it.
				CheckSectionNode(elementIdProperty.Value, scope, depth + 1, budget);
				return;
			case IFunction function:
				CheckFunctionLength(node, function, budget);
				break;
			case CatchClause catchClause:
				CheckCatchLogOnly(catchClause, budget);
				break;
			case Property {Value: Literal {Value: double}} prop when TryGetStaticPropertyName(prop) == "comparisonType":
				budget.TryAdd(RuleNumericComparisonType, LintSeverity.Warning,
					prop.Location.Start.Line, prop.Location.Start.Column + 1,
					() => "`comparisonType` is a bare number; use the named `sdk.ComparisonType.<Name>` value so the "
						+ "operator can be read and checked (MCP guide: page-schema-handlers).");
				break;
			case Literal {Value: string text} when !scope.InConstantData && !scope.IsElementId && ODataKeyFormatter.IsGuid(text):
				budget.TryAdd(RuleGuidLiteralDataReference, LintSeverity.Warning,
					node.Location.Start.Line, node.Location.Start.Column + 1,
					() => $"Record id `{text}` is hard-coded in page code; read it from the project's constants "
						+ "module (declared in SCHEMA_DEPS) or a system setting, so the reference has a name and one "
						+ "place to change.");
				break;
		}
		bool elementObject = node is ObjectExpression obj && IsElementObject(obj);
		// Code inside constant data (a function stored in it) is code again, and its record ids are reported.
		bool constantData = scope.InConstantData && node is not IFunction;
		bool constantDeclaration = scope.InModule && node is VariableDeclaration {Kind: VariableDeclarationKind.Const};
		foreach (Node child in node.ChildNodes) {
			bool childIsElementId = elementObject && child is Property {Value: Literal} idProperty
				&& TryGetStaticPropertyName(idProperty) == "id";
			bool childIsConstantData = constantData
				|| (constantDeclaration && child is VariableDeclarator {Init: var init} && IsConstantData(init));
			CheckSectionNode(child, scope with {IsElementId = childIsElementId, InConstantData = childIsConstantData},
				depth + 1, budget);
		}
	}

	// A declaration's value that is data, not code: a literal, an object or array literal, or `Object.freeze(...)` of
	// one.
	private static bool IsConstantData(Node init) =>
		init switch {
			Literal or ObjectExpression or ArrayExpression => true,
			CallExpression {
				Callee: MemberExpression {Computed: false, Object: Identifier {Name: "Object"}, Property: Identifier {Name: "freeze"}},
				Arguments: [var frozen]
			} => frozen is ObjectExpression or ArrayExpression,
			_ => false
		};

	private static bool IsElementObject(ObjectExpression obj) =>
		obj.Properties.OfType<Property>().Any(p => TryGetStaticPropertyName(p) is { } key && ElementKeys.Contains(key));

	private static void CheckFunctionLength(Node node, IFunction function, LintFindingBudget budget) {
		int lines = node.Location.End.Line - node.Location.Start.Line + 1;
		if (lines <= MaxFunctionLines) {
			return;
		}
		string name = function.Id?.Name;
		budget.TryAdd(RuleFunctionTooLong, LintSeverity.Warning,
			node.Location.Start.Line, node.Location.Start.Column + 1,
			() => $"Function{(name is null ? string.Empty : $" `{name}`")} is {lines} lines long (limit "
				+ $"{MaxFunctionLines}); split it into named requests or module functions, one behavior each.");
	}

	// A catch block that is empty or only writes to the console swallows the error: the caller carries on as if
	// the operation had worked.
	private static void CheckCatchLogOnly(CatchClause catchClause, LintFindingBudget budget) {
		if (!catchClause.Body.Body.All(IsConsoleCall)) {
			return;
		}
		budget.TryAdd(RuleCatchLogOnly, LintSeverity.Warning,
			catchClause.Location.Start.Line, catchClause.Location.Start.Column + 1,
			() => "This catch block only logs (or ignores) the error, so the page carries on as if the operation "
				+ "had worked. Show the user a message, stop the action, or let the error reach the platform.");
	}

	private static bool IsConsoleCall(Statement statement) =>
		statement is ExpressionStatement {
			Expression: CallExpression {Callee: MemberExpression {Object: Identifier {Name: "console"}}}
		};

	#endregion

	#region Handler rules

	private static void CheckHandler(IFunction handler, Dictionary<string, List<NamedFunction>> helpersByShape,
		LintFindingBudget budget) {
		List<NamedFunction> helpers = new();
		CollectNamedFunctions(handler.Body, helpers, insideHelper: false, depth: 0);
		// The handler's own variables: its parameters and what its body declares outside the functions in it. A
		// sibling helper is not one of them - a helper calling another moves to the module with it.
		HashSet<string> handlerNames = new(StringComparer.Ordinal);
		foreach (Node param in handler.Params) {
			CollectBindingNames(param, handlerNames);
		}
		CollectDeclaredNames(handler.Body, handlerNames, intoFunctions: false, depth: 0);
		handlerNames.ExceptWith(helpers.Select(h => h.Name));
		foreach (NamedFunction helper in helpers) {
			// A helper that reads a variable of its own handler (`$context`, `scopes`, a local) is a binding of that
			// handler: it cannot be declared once in a module without changing its signature, so its copies are
			// not reported as copies.
			// A helper nested in another helper is part of that helper: it moves (or is copied) with it.
			if (helper.Nested || helper.NodeCount < MinDuplicateFunctionNodes || helper.FreeNames.Overlaps(handlerNames)) {
				continue;
			}
			if (!helpersByShape.TryGetValue(helper.Shape, out List<NamedFunction> sameShape)) {
				sameShape = new List<NamedFunction>();
				helpersByShape[helper.Shape] = sameShape;
			}
			sameShape.Add(helper);
		}
		Node handlerNode = (Node)handler;
		if (helpers.Count > MaxNestedFunctionsPerHandler) {
			budget.TryAdd(RuleHandlerTooManyNestedFunctions, LintSeverity.Warning,
				handlerNode.Location.Start.Line, handlerNode.Location.Start.Column + 1,
				() => $"This handler declares {helpers.Count} functions inside its body (limit "
					+ $"{MaxNestedFunctionsPerHandler}): {string.Join(", ", helpers.Select(h => h.Name))}. Keep the "
					+ $"handler thin and move each behavior to its own named request or module function. "
					+ ModuleHelperAdvice);
		}
		CheckSequentialAwaits(handler.Body, depth: 0, budget);
	}

	// Page handlers run as a chain: a handler that does not call `next.handle(request)` stops every later handler of
	// the request, and the platform's own handling of a `crt.*` request, without an error; one that calls it and drops
	// the result loses what the rest of the chain returned. The toolkit's migration gate holds the same rule.
	private static void CheckHandlerCallsNext(IFunction handler, string request, LintFindingBudget budget) {
		string problem = NextProblem(handler);
		if (problem is null) {
			return;
		}
		Node handlerNode = (Node)handler;
		string requestName = request is null ? "its request" : $"`{request}`";
		budget.TryAdd(RuleHandlerWithoutNext, LintSeverity.Warning,
			handlerNode.Location.Start.Line, handlerNode.Location.Start.Column + 1,
			() => $"This handler {problem}, so the rest of the chain for {requestName} (and the platform's own handling "
				+ "of a `crt.*` request) is skipped or its result lost, without an error. Take `next` as the second "
				+ "parameter and return `next?.handle(request)` (or a variable assigned from it), unless stopping the "
				+ "chain is the point.");
	}

	// Null when the handler hands the request on: it takes `next` (a defaulted parameter counts), its own body (not a
	// callback inside it) calls `next.handle(...)`, and the result reaches the caller - an arrow's expression body
	// gives it, or every `return` of a block body does (directly or through a variable assigned from it) and the body
	// does not run off its end.
	private static string NextProblem(IFunction handler) {
		string next = handler.Params.Count > 1 ? handler.Params[1] switch {
			Identifier identifier => identifier.Name,
			AssignmentPattern {Left: Identifier defaulted} => defaulted.Name,
			_ => null
		} : null;
		if (next is null) {
			return "takes no `next`";
		}
		List<Node> own = new();
		CollectOwnNodes((Node)handler.Body, own, depth: 0);
		if (!own.Any(node => IsNextHandleCall(node, next))) {
			return "never calls `next.handle(request)`";
		}
		HashSet<string> fromNext = new(StringComparer.Ordinal);
		foreach (Node node in own) {
			switch (node) {
				case VariableDeclarator {Id: Identifier name, Init: not null} declarator when GivesNextResult(declarator.Init, next, fromNext, depth: 0):
					fromNext.Add(name.Name);
					break;
				case AssignmentExpression {Left: Identifier target} assignment when GivesNextResult(assignment.Right, next, fromNext, depth: 0):
					fromNext.Add(target.Name);
					break;
			}
		}
		bool returned = handler.Body switch {
			BlockStatement block => EndsInReturn(block, depth: 0)
				&& own.OfType<ReturnStatement>().All(r => r.Argument is not null && GivesNextResult(r.Argument, next, fromNext, depth: 0)),
			Node expressionBody => GivesNextResult(expressionBody, next, fromNext, depth: 0)
		};
		return returned ? null : "calls `next.handle(request)` but does not return its result";
	}

	// Every path through the statement ends in a `return` or a `throw`.
	private static bool EndsInReturn(Statement statement, int depth) {
		if (statement is null || depth > MaxAstDepth) {
			return false;
		}
		return statement switch {
			ReturnStatement or ThrowStatement => true,
			BlockStatement block => block.Body.Count > 0 && EndsInReturn(block.Body[^1], depth + 1),
			IfStatement branch => EndsInReturn(branch.Consequent, depth + 1) && EndsInReturn(branch.Alternate, depth + 1),
			TryStatement attempt => EndsInReturn(attempt.Finalizer, depth + 1)
				|| (EndsInReturn(attempt.Block, depth + 1) && (attempt.Handler is null || EndsInReturn(attempt.Handler.Body, depth + 1))),
			SwitchStatement choice => SwitchEndsInReturn(choice, depth + 1),
			_ => false
		};
	}

	// A switch ends in a return when it has a `default` and every case either falls through to the next one (no
	// statements) or ends in a return or throw itself, the last one included.
	private static bool SwitchEndsInReturn(SwitchStatement choice, int depth) {
		if (!choice.Cases.Any(c => c.Test is null) || choice.Cases.Count == 0 || choice.Cases[^1].Consequent.Count == 0) {
			return false;
		}
		return choice.Cases.All(c => c.Consequent.Count == 0 || EndsInReturn(c.Consequent[^1], depth));
	}

	// The nodes of a function body that run as part of it: a nested function is a callback with its own flow.
	private static void CollectOwnNodes(Node node, List<Node> into, int depth) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		into.Add(node);
		foreach (Node child in node.ChildNodes) {
			if (child is not IFunction) {
				CollectOwnNodes(child, into, depth + 1);
			}
		}
	}

	// `next.handle(...)` or `next?.handle(...)`.
	private static bool IsNextHandleCall(Node node, string next) =>
		Unwrap(node, depth: 0) is CallExpression {
			Callee: MemberExpression {Computed: false, Object: Identifier {Name: var target}, Property: Identifier {Name: "handle"}}
		} && target == next;

	// The expression gives the chain's result: the call itself (awaited or not), a call chained on it
	// (`next.handle(request).then(...)`), or a variable assigned from either. A chain deeper than MaxAstDepth counts
	// as not giving it (the handler is then reported), never as a stack overflow.
	private static bool GivesNextResult(Node expression, string next, HashSet<string> fromNext, int depth) {
		if (depth > MaxAstDepth) {
			return false;
		}
		Node value = Unwrap(expression, depth);
		return IsNextHandleCall(value, next)
			|| (value is Identifier {Name: var name} && fromNext.Contains(name))
			|| (value is CallExpression {Callee: MemberExpression {Computed: false} chained}
				&& GivesNextResult(chained.Object, next, fromNext, depth + 1));
	}

	// The expression under `await` and `?.` wrappers; past MaxAstDepth the node is returned as it is.
	private static Node Unwrap(Node node, int depth) =>
		depth > MaxAstDepth ? node : node switch {
			AwaitExpression await => Unwrap(await.Argument, depth + 1),
			ChainExpression chain => Unwrap(chain.Expression, depth + 1),
			_ => node
		};

	/// <summary>A function a handler declares by name, and its shape for the duplicate check.</summary>
	private sealed record NamedFunction(string Name, int Line, int Column, string Shape, int NodeCount,
		HashSet<string> FreeNames, bool Nested);

	// Named functions only: `function name() {}` and `const name = () => {}`. An inline callback such as
	// `.map(x => x.id)` is not a helper and is not counted.
	private static void CollectNamedFunctions(Node node, List<NamedFunction> found, bool insideHelper, int depth) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		switch (node) {
			case FunctionDeclaration {Id: not null} declaration:
				found.Add(DescribeNamedFunction(declaration.Id.Name, node, declaration, insideHelper));
				break;
			case VariableDeclarator {Id: Identifier name, Init: IFunction init}:
				found.Add(DescribeNamedFunction(name.Name, node, init, insideHelper));
				break;
		}
		// A function's own children are inside it; the function itself is at the level it was found on.
		bool inner = insideHelper || node is IFunction;
		foreach (Node child in node.ChildNodes) {
			CollectNamedFunctions(child, found, inner, depth + 1);
		}
	}

	private static NamedFunction DescribeNamedFunction(string name, Node anchor, IFunction function, bool nested) {
		StringBuilder shape = new();
		int nodeCount = 0;
		foreach (Node param in function.Params) {
			AppendShape(param, shape, ref nodeCount, depth: 0);
		}
		AppendShape(function.Body, shape, ref nodeCount, depth: 0);
		HashSet<string> own = new(StringComparer.Ordinal) {name};
		foreach (Node param in function.Params) {
			CollectBindingNames(param, own);
		}
		CollectDeclaredNames(function.Body, own, intoFunctions: true, depth: 0);
		HashSet<string> free = new(StringComparer.Ordinal);
		CollectReferencedNames(function.Body, free, depth: 0);
		free.ExceptWith(own);
		return new NamedFunction(name, anchor.Location.Start.Line, anchor.Location.Start.Column + 1,
			shape.ToString(), nodeCount, free, nested);
	}

	private static void CollectBindingNames(Node pattern, HashSet<string> names) =>
		VisitBindingNames(pattern, name => names.Add(name), depth: 0);

	// The names a function body declares: variables, functions and catch parameters, and - with `intoFunctions` -
	// the declarations and parameters of the functions inside it too.
	private static void CollectDeclaredNames(Node node, HashSet<string> names, bool intoFunctions, int depth) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		switch (node) {
			case VariableDeclarator declarator:
				CollectBindingNames(declarator.Id, names);
				break;
			case FunctionDeclaration {Id: not null} declaration:
				names.Add(declaration.Id.Name);
				break;
			case CatchClause {Param: not null} catchClause:
				CollectBindingNames(catchClause.Param, names);
				break;
		}
		foreach (Node child in node.ChildNodes) {
			if (child is IFunction function) {
				if (!intoFunctions) {
					continue;
				}
				foreach (Node param in function.Params) {
					CollectBindingNames(param, names);
				}
			}
			CollectDeclaredNames(child, names, intoFunctions, depth + 1);
		}
	}

	// The variable names an expression reads: identifiers, except a property name (`a.name`, `{ name: v }`).
	private static void CollectReferencedNames(Node node, HashSet<string> names, int depth) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		switch (node) {
			case Identifier identifier:
				names.Add(identifier.Name);
				return;
			case MemberExpression member:
				CollectReferencedNames(member.Object, names, depth + 1);
				if (member.Computed) {
					CollectReferencedNames(member.Property, names, depth + 1);
				}
				return;
			case Property property:
				if (property.Computed) {
					CollectReferencedNames(property.Key, names, depth + 1);
				}
				CollectReferencedNames(property.Value, names, depth + 1);
				return;
			case AssignmentExpression {Left: Identifier} assignment:
				// The target is written, not read: `data = await other.load()` does not depend on the old `data`.
				CollectReferencedNames(assignment.Right, names, depth + 1);
				return;
		}
		foreach (Node child in node.ChildNodes) {
			CollectReferencedNames(child, names, depth + 1);
		}
	}

	// The structure of a function without its own name: node types, identifier names, literal values, operators,
	// declaration kinds and template text. Two copies of one helper give the same shape wherever they sit and
	// whatever they are called; `===` and `!==` are properties, not child nodes, so they are appended here.
	private static void AppendShape(Node node, StringBuilder shape, ref int nodeCount, int depth) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		nodeCount++;
		shape.Append('(').Append(node.Type);
		string token = ShapeToken(node);
		if (token is not null) {
			shape.Append(' ').Append(token);
		}
		foreach (Node child in node.ChildNodes) {
			AppendShape(child, shape, ref nodeCount, depth + 1);
		}
		shape.Append(')');
	}

	// The part of a node its shape keeps besides its type; null when the type alone describes it.
	private static string ShapeToken(Node node) =>
		node switch {
			Identifier identifier => identifier.Name,
			Literal literal => literal.Raw,
			UpdateExpression update => (update.Prefix ? "prefix " : "postfix ") + update.Operator,
			UnaryExpression unary => unary.Operator.ToString(),
			LogicalExpression logical => logical.Operator.ToString(),
			BinaryExpression binary => binary.Operator.ToString(),
			AssignmentExpression assignment => assignment.Operator.ToString(),
			VariableDeclaration declaration => declaration.Kind.ToString(),
			TemplateElement element => element.Value.Raw,
			_ => null
		};

	private static void ReportDuplicateHelpers(Dictionary<string, List<NamedFunction>> helpersByShape,
		LintFindingBudget budget) {
		foreach (List<NamedFunction> copies in helpersByShape.Values.Where(list => list.Count > 1)) {
			NamedFunction first = copies[0];
			foreach (NamedFunction copy in copies.Skip(1)) {
				budget.TryAdd(RuleDuplicateFunctionInPage, LintSeverity.Warning, copy.Line, copy.Column,
					() => $"`{copy.Name}` repeats the body of `{first.Name}` (line {first.Line}); the page declares "
						+ $"this function {copies.Count} times. Declare it once. " + ModuleHelperAdvice);
			}
		}
	}

	#endregion

	#region Sequential awaits

	// Within each block, counts the awaited model loads (`await x.load(...)`, `await sdk.Model.create(...)`) that
	// do not use the result of an earlier one. A result is followed through the variables assigned from it, so
	// `const id = rows[0].id; await other.load(id)` still depends on the load that produced `rows`.
	private static void CheckSequentialAwaits(Node node, int depth, LintFindingBudget budget) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		if (node is BlockStatement block) {
			CheckBlockAwaits(block, budget);
		}
		foreach (Node child in node.ChildNodes) {
			// A nested function is its own sequence: its awaits do not run in this block's order, so its body is
			// checked on its own (each block keeps its own count).
			CheckSequentialAwaits(child is IFunction function ? function.Body : child, depth + 1, budget);
		}
	}

	private static void CheckBlockAwaits(BlockStatement block, LintFindingBudget budget) {
		HashSet<string> fromLoads = new(StringComparer.Ordinal);
		int independent = 0;
		Statement firstLoad = null;
		HashSet<string> referenced = new(StringComparer.Ordinal);
		foreach (Statement statement in block.Body) {
			// Nothing can depend on a load before the first one, so the statement is only read once there is one.
			referenced.Clear();
			if (fromLoads.Count > 0) {
				CollectReferencedNames(statement, referenced, depth: 0);
			}
			bool usesLoadResult = referenced.Overlaps(fromLoads);
			bool isLoad = IsAwaitedLoad(statement);
			if (isLoad && !usesLoadResult) {
				independent++;
				firstLoad ??= statement;
			}
			if (isLoad || usesLoadResult) {
				CollectStatementBindings(statement, fromLoads);
			}
		}
		// The first independent load is where the warning points; it is set whenever the count is above zero.
		if (independent <= MaxSequentialIndependentAwaits || firstLoad is not { } first) {
			return;
		}
		budget.TryAdd(RuleSequentialIndependentAwaits, LintSeverity.Warning,
			first.Location.Start.Line, first.Location.Start.Column + 1,
			() => $"{independent} awaited model loads run one after another although none uses another's result "
				+ $"(limit {MaxSequentialIndependentAwaits}); start them together with `Promise.all`, or read the "
				+ "data through the page's data sources.");
	}

	private static bool IsAwaitedLoad(Statement statement) {
		Node expression = statement switch {
			ExpressionStatement {Expression: AssignmentExpression assignment} => assignment.Right,
			ExpressionStatement expressionStatement => expressionStatement.Expression,
			VariableDeclaration {Declarations.Count: 1} declaration => declaration.Declarations[0].Init,
			_ => null
		};
		return ((expression as AwaitExpression)?.Argument as CallExpression)?.Callee is MemberExpression {Computed: false, Property: Identifier property} member
			&& (property.Name == "load" || (property.Name == "create" && IsModelReference(member.Object)));
	}

	private static bool IsModelReference(Node node) =>
		node switch {
			Identifier {Name: "Model"} => true,
			MemberExpression {Computed: false, Property: Identifier {Name: "Model"}} => true,
			_ => false
		};

	// The names a statement binds: the declared variables of a declaration, or the assigned identifier of an
	// assignment.
	private static void CollectStatementBindings(Statement statement, HashSet<string> names) {
		switch (statement) {
			case VariableDeclaration declaration:
				foreach (VariableDeclarator declarator in declaration.Declarations) {
					CollectBindingNames(declarator.Id, names);
				}
				break;
			case ExpressionStatement {Expression: AssignmentExpression {Left: Identifier target}}:
				names.Add(target.Name);
				break;
		}
	}

	#endregion

}
