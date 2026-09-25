using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Clio.Tests;

/// <summary>
/// Guard over the <c>clio.mcp.e2e</c> SOURCE: every catch block that swallows a parse failure from a
/// dumping parser must delete that failure's payload dump (GitHub issue #1592).
/// </summary>
/// <remarks>
/// <c>McpResultDiagnostics</c> writes a payload dump into the published <c>TestResults</c> artifact on
/// EVERY parse failure, including the ones a lenient call site catches as ordinary control flow on a
/// passing test. Such a site must call <c>PayloadDumpReader.DeleteIfPresent</c>, and nothing but this
/// guard makes a new site remember to: forgetting compiles, passes, and quietly publishes a payload from
/// a green build.
/// <para>
/// The scan is syntax-only. A method counts as dumping when it calls a <c>McpResultDiagnostics.Describe*</c>
/// member, or — transitively — a dumping method, outside a try block that already deletes. A call is
/// resolved to <c>Type.Method</c> only where the receiver is known without a semantic model: an
/// unqualified call (searched through the enclosing type and its declared base types),
/// <c>this.</c>/<c>base.</c>, or a static call through a type declared in the e2e sources — which is how
/// every result parser is called. A call through a variable resolves to nothing. That is a deliberate
/// trade: matching by bare member name instead made <c>session.CallToolAsync</c> and every other SDK call
/// that shares a helper's name "dumping", and flagged about thirty catch blocks that cannot receive a
/// parse failure at all.
/// A catch clause is a lenient site when its try block calls a dumping method, it can catch the parser's
/// <see cref="InvalidOperationException"/>, and it contains no <c>throw</c>: a catch that rethrows or wraps
/// surfaces the failure, and then the dump is the evidence it exists for.
/// </para>
/// <para>
/// Lives in <c>clio.tests</c>, like <c>McpFixturePolicyTests</c>, so it runs in the pre-merge Unit lane
/// that the e2e project itself never reaches. Roslyn arrives transitively through the PowerShell SDK
/// (<c>Microsoft.PowerShell.Commands.Utility</c>); it is not referenced directly, so this test does not
/// pin a compiler version the SDK has to agree with.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpPayloadDumpCleanupPolicyTests {
	private const string DiagnosticsTypeName = "McpResultDiagnostics";
	private const string ReaderTypeName = "PayloadDumpReader";

	/// <summary>Reader members that remove the dump a message names.</summary>
	private static readonly HashSet<string> DeletingMembers = new(StringComparer.Ordinal) {
		"DeleteIfPresent",
		"ReadAndDelete"
	};

	/// <summary>
	/// Catch types that can receive the parser's <see cref="InvalidOperationException"/>. A catch of any
	/// other type cannot swallow the parse failure, so its dump is not its responsibility.
	/// </summary>
	private static readonly HashSet<string> ParseFailureCatchTypes = new(StringComparer.Ordinal) {
		"InvalidOperationException",
		"SystemException",
		"Exception"
	};

	/// <summary>
	/// Catch sites that swallow a dumping call but are NOT a passing test's path, keyed by
	/// <c>&lt;file&gt;::&lt;enclosing member&gt;</c>, each with the reason its dump is evidence rather than noise.
	/// </summary>
	/// <remarks>
	/// A site belongs here only when reaching its catch block already fails the test through some other
	/// statement, so deleting the dump would throw away the one record of what the tool returned. Every
	/// entry must still match a site: a stale one fails the guard instead of silently exempting the next
	/// catch that happens to reuse the name.
	/// </remarks>
	private static readonly IReadOnlyDictionary<string, string> EvidenceKeepingSites =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["clio.mcp.e2e/PageSyncToolE2ETests.cs::PageSyncTool_Should_Block_Real_Save_And_Leave_Page_Unchanged_When_HelperIsConditionallyDeclared"] =
				"the restore runs only when restoreNeeded, i.e. when the probe body reached the stand, and the bodyAfter assertion has then already failed the test",
			["clio.mcp.e2e/PageUpdateToolE2ETests.cs::TryRestorePageBodyAsync"] =
				"called only from a finally block under restoreNeeded, which is set exactly when the test's own assertion on the unchanged body fails"
		};

	/// <summary>
	/// The repository root, four levels above the test output directory
	/// (<c>clio.tests/bin/&lt;configuration&gt;/&lt;framework&gt;</c>).
	/// </summary>
	private static readonly string RepositoryRoot =
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	[Test]
	[Description("Every catch block in clio.mcp.e2e that swallows a parse failure from a dumping parser deletes that failure's payload dump, so a passing test never publishes one (GitHub issue #1592).")]
	public void LenientParseFailureCatchSites_ShouldDeleteTheirPayloadDump() {
		// Arrange
		IReadOnlyList<(string Path, SyntaxNode Root)> sources = ParseE2ESources();

		// Act
		IReadOnlyList<LenientCatchSite> sites = FindLenientCatchSites(sources);
		string[] missingDelete = sites
			.Where(site => !site.Deletes && !EvidenceKeepingSites.ContainsKey(site.Key))
			.Select(site => site.Location)
			.ToArray();
		string[] staleExemptions = EvidenceKeepingSites.Keys
			.Where(key => !sites.Any(site => !site.Deletes && site.Key == key))
			.ToArray();

		// Assert
		sites.Should().HaveCountGreaterThanOrEqualTo(4,
			because: "clio.mcp.e2e has at least four known lenient sites (ApplicationToolE2ETests, PageSyncToolE2ETests, PageHierarchyGetToolE2ETests, PageGetToolE2ETests); finding fewer means the scan stopped recognizing them and this guard pins nothing");
		missingDelete.Should().BeEmpty(
			because: "a catch that swallows a parse failure is a PASSING test's path, and without PayloadDumpReader.DeleteIfPresent its payload dump lands in the published TestResults artifact of a green build");
		staleExemptions.Should().BeEmpty(
			because: "an evidence-keeping exemption that no longer matches a site would silently exempt the next catch that reuses its file and member name");
	}

	[Test]
	[Description("The guard flags a lenient catch that swallows a dumping parser's failure without always deleting the dump, and exempts one that always deletes or always rethrows, so a scan that silently recognizes nothing cannot pass.")]
	public void FindLenientCatchSites_ShouldFlagOnlyTheSwallowingSiteWithoutADelete() {
		// Arrange
		const string source = """
			internal static class Parser {
				public static T Extract<T>(object result) =>
					throw new System.InvalidOperationException(McpResultDiagnostics.Describe(result));
			}

			internal static class Helper {
				public static int Wrapped(object result) => Parser.Extract<int>(result);
			}

			internal sealed class Fixture {
				public bool Forgets(object result) {
					try { Helper.Wrapped(result); return true; }
					catch (System.InvalidOperationException) { return false; }
				}

				public bool Deletes(object result) {
					try { Parser.Extract<int>(result); return true; }
					catch (System.InvalidOperationException exception) {
						PayloadDumpReader.DeleteIfPresent(exception.Message);
						return false;
					}
				}

				public void Rethrows(object result) {
					try { Parser.Extract<int>(result); }
					catch (System.InvalidOperationException exception) {
						throw new System.InvalidOperationException("wrapped " + exception.Message);
					}
				}

				public bool RethrowsOnlySometimes(object result, bool strict) {
					try { Parser.Extract<int>(result); return true; }
					catch (System.InvalidOperationException) {
						if (strict) { throw; }
						return false;
					}
				}

				public bool DeletesOnlySometimes(object result, bool keep) {
					try { Parser.Extract<int>(result); return true; }
					catch (System.InvalidOperationException exception) {
						if (!keep) { PayloadDumpReader.DeleteIfPresent(exception.Message); }
						return false;
					}
				}

				public bool CatchesSomethingElse(object result) {
					try { Parser.Extract<int>(result); return true; }
					catch (System.IO.IOException) { return false; }
				}
			}
			""";
		(string Path, SyntaxNode Root)[] sources = [("Fixture.cs", CSharpSyntaxTree.ParseText(source).GetRoot())];

		// Act
		IReadOnlyList<LenientCatchSite> sites = FindLenientCatchSites(sources);

		// Assert
		sites.Should().HaveCount(4,
			because: "Forgets, Deletes and the two conditional catches all have a path that swallows the parse failure; Rethrows always surfaces it and CatchesSomethingElse cannot receive it");
		sites.Where(site => !site.Deletes).Select(site => site.Key).Should().BeEquivalentTo(
			["Fixture.cs::Forgets", "Fixture.cs::RethrowsOnlySometimes", "Fixture.cs::DeletesOnlySometimes"],
			because: "a swallow through a transitive helper, a rethrow on one branch only and a delete on one branch only each leave a passing test's dump behind");
		sites.Single(site => site.Key == "Fixture.cs::Forgets").Location.Should().Be("Fixture.cs:13",
			because: "the reported location must point at the offending catch clause");
	}

	private static IReadOnlyList<(string Path, SyntaxNode Root)> ParseE2ESources() {
		string e2eDirectory = Path.Combine(RepositoryRoot, "clio.mcp.e2e");
		string binSegment = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
		string objSegment = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
		return Directory.EnumerateFiles(e2eDirectory, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains(binSegment, StringComparison.Ordinal)
				&& !path.Contains(objSegment, StringComparison.Ordinal))
			.OrderBy(path => path, StringComparer.Ordinal)
			.Select(path => (
				Path.GetRelativePath(RepositoryRoot, path),
				CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot()))
			.ToArray();
	}

	private static IReadOnlyList<LenientCatchSite> FindLenientCatchSites(
		IReadOnlyCollection<(string Path, SyntaxNode Root)> sources) {
		IReadOnlyDictionary<string, string[]> baseTypes = IndexBaseTypes(sources);
		HashSet<string> dumpingMethods = FindDumpingMethods(sources, baseTypes);
		List<LenientCatchSite> sites = [];
		foreach ((string path, SyntaxNode root) in sources) {
			foreach (CatchClauseSyntax catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>()) {
				if (catchClause.Parent is not TryStatementSyntax tryStatement
					|| !CanCatchParseFailure(catchClause)
					|| !tryStatement.Block.DescendantNodes().OfType<InvocationExpressionSyntax>()
						.Any(invocation => IsDumpingCall(invocation, dumpingMethods, baseTypes))
					|| Throws(catchClause.Block)) {
					continue;
				}

				int line = catchClause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
				string normalizedPath = path.Replace('\\', '/');
				sites.Add(new LenientCatchSite(
					$"{normalizedPath}:{line}",
					$"{normalizedPath}::{EnclosingMemberName(catchClause)}",
					Deletes(catchClause.Block)));
			}
		}

		return sites;
	}

	/// <summary>
	/// Base-list names of every type declared in the sources, partial declarations merged, so an
	/// unqualified call in a derived fixture resolves to a helper its base class declares.
	/// </summary>
	private static IReadOnlyDictionary<string, string[]> IndexBaseTypes(
		IReadOnlyCollection<(string Path, SyntaxNode Root)> sources) =>
		sources
			.SelectMany(source => source.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
			.GroupBy(type => type.Identifier.Text, StringComparer.Ordinal)
			.ToDictionary(
				group => group.Key,
				group => group
					.SelectMany(type => type.BaseList?.Types ?? default)
					.Select(baseType => RightmostName(baseType.Type))
					.Where(name => name.Length > 0)
					.Distinct(StringComparer.Ordinal)
					.ToArray(),
				StringComparer.Ordinal);

	/// <summary>
	/// <c>Type.Method</c> keys of methods and local functions whose call can write a payload dump that
	/// reaches the caller undeleted, closed transitively over callers.
	/// </summary>
	private static HashSet<string> FindDumpingMethods(
		IReadOnlyCollection<(string Path, SyntaxNode Root)> sources,
		IReadOnlyDictionary<string, string[]> baseTypes) {
		(string Key, SyntaxNode Body)[] methods = sources
			.SelectMany(source => source.Root.DescendantNodes())
			.Select(node => (Node: node, Named: ToNamedBody(node)))
			.Where(method => method.Named.Body is not null)
			.Select(method => ($"{EnclosingTypeName(method.Node)}.{method.Named.Name}", method.Named.Body!))
			.ToArray();

		HashSet<string> dumping = new(StringComparer.Ordinal);
		bool grew = true;
		while (grew) {
			grew = false;
			foreach ((string key, SyntaxNode body) in methods) {
				if (!dumping.Contains(key) && EscapesUndeleted(body, dumping, baseTypes)) {
					dumping.Add(key);
					grew = true;
				}
			}
		}

		return dumping;
	}

	private static (string Name, SyntaxNode? Body) ToNamedBody(SyntaxNode node) =>
		node switch {
			MethodDeclarationSyntax method => (method.Identifier.Text, (SyntaxNode?)method.Body ?? method.ExpressionBody),
			LocalFunctionStatementSyntax local => (local.Identifier.Text, (SyntaxNode?)local.Body ?? local.ExpressionBody),
			_ => (string.Empty, null)
		};

	private static bool EscapesUndeleted(
		SyntaxNode body,
		HashSet<string> dumpingMethods,
		IReadOnlyDictionary<string, string[]> baseTypes) =>
		body.DescendantNodesAndSelf()
			.OfType<InvocationExpressionSyntax>()
			.Any(invocation => IsDumpingCall(invocation, dumpingMethods, baseTypes)
				&& !IsInsideDeletingTry(invocation));

	private static bool IsDumpingCall(
		InvocationExpressionSyntax invocation,
		HashSet<string> dumpingMethods,
		IReadOnlyDictionary<string, string[]> baseTypes) =>
		IsDiagnosticsDescribe(invocation)
		|| ResolveCallees(invocation, baseTypes).Any(dumpingMethods.Contains);

	/// <summary>
	/// The <c>Type.Method</c> keys an invocation can bind to, resolved by name only where the receiver is
	/// known without a semantic model: an unqualified call, <c>this.</c>/<c>base.</c>, or a static call
	/// through a type declared in the sources. A call through a variable or an SDK object resolves to
	/// nothing, which is what keeps <c>session.CallToolAsync</c> from matching a same-named helper.
	/// </summary>
	private static IEnumerable<string> ResolveCallees(
		InvocationExpressionSyntax invocation,
		IReadOnlyDictionary<string, string[]> baseTypes) {
		(string? receiverType, string method) = invocation.Expression switch {
			SimpleNameSyntax simple => (EnclosingTypeName(invocation), simple.Identifier.Text),
			MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } access =>
				(EnclosingTypeName(invocation), access.Name.Identifier.Text),
			MemberAccessExpressionSyntax access when baseTypes.ContainsKey(RightmostName(access.Expression)) =>
				(RightmostName(access.Expression), access.Name.Identifier.Text),
			_ => ((string?)null, string.Empty)
		};

		return receiverType is null
			? []
			: TypeAndBases(receiverType, baseTypes).Select(type => $"{type}.{method}");
	}

	private static IEnumerable<string> TypeAndBases(string type, IReadOnlyDictionary<string, string[]> baseTypes) {
		HashSet<string> seen = new(StringComparer.Ordinal);
		Queue<string> pending = new([type]);
		while (pending.Count > 0) {
			string current = pending.Dequeue();
			if (!seen.Add(current)) {
				continue;
			}

			yield return current;
			foreach (string baseType in baseTypes.GetValueOrDefault(current) ?? []) {
				pending.Enqueue(baseType);
			}
		}
	}

	private static string EnclosingTypeName(SyntaxNode node) =>
		node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? string.Empty;

	private static string RightmostName(ExpressionSyntax expression) =>
		expression switch {
			SimpleNameSyntax simple => simple.Identifier.Text,
			QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
			MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
			_ => string.Empty
		};

	private static bool IsInsideDeletingTry(SyntaxNode node) =>
		node.Ancestors()
			.OfType<TryStatementSyntax>()
			.Any(tryStatement => tryStatement.Block.Span.Contains(node.Span)
				&& tryStatement.Catches.Any(catchClause => Deletes(catchClause.Block)));

	private static bool IsDiagnosticsDescribe(InvocationExpressionSyntax invocation) =>
		invocation.Expression is MemberAccessExpressionSyntax {
			Expression: IdentifierNameSyntax { Identifier.Text: DiagnosticsTypeName }
		} access
		&& access.Name.Identifier.Text.StartsWith("Describe", StringComparison.Ordinal);

	private static bool CanCatchParseFailure(CatchClauseSyntax catchClause) {
		if (catchClause.Declaration is null) {
			return true;
		}

		string typeName = catchClause.Declaration.Type switch {
			QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
			SimpleNameSyntax simple => simple.Identifier.Text,
			_ => catchClause.Declaration.Type.ToString()
		};
		return ParseFailureCatchTypes.Contains(typeName);
	}

	/// <summary>
	/// Whether the catch block ALWAYS throws: a top-level <c>throw</c> statement, after which nothing in the
	/// block runs. A throw nested under an <c>if</c> does not count, because the other branch still
	/// swallows the failure and leaves the dump behind.
	/// </summary>
	private static bool Throws(BlockSyntax block) =>
		block.Statements.Any(statement => statement is ThrowStatementSyntax
			|| statement is ExpressionStatementSyntax { Expression: ThrowExpressionSyntax });

	/// <summary>
	/// Whether the catch block ALWAYS deletes: a top-level <c>PayloadDumpReader</c> delete call. A delete
	/// nested under an <c>if</c> does not count, because the other branch returns with the dump in place.
	/// </summary>
	private static bool Deletes(BlockSyntax block) =>
		block.Statements
			.OfType<ExpressionStatementSyntax>()
			.Select(statement => statement.Expression)
			.OfType<InvocationExpressionSyntax>()
			.Any(invocation => invocation.Expression is MemberAccessExpressionSyntax {
					Expression: IdentifierNameSyntax { Identifier.Text: ReaderTypeName }
				} access
				&& DeletingMembers.Contains(access.Name.Identifier.Text));

	private static string EnclosingMemberName(SyntaxNode node) =>
		node.Ancestors()
			.Select(ancestor => ToNamedBody(ancestor).Name)
			.FirstOrDefault(name => name.Length > 0) ?? string.Empty;

	/// <param name="Location">File and line of the catch clause, for the failure message.</param>
	/// <param name="Key">File and enclosing member, stable across edits that move lines.</param>
	/// <param name="Deletes">Whether the catch block removes the dump.</param>
	private sealed record LenientCatchSite(string Location, string Key, bool Deletes);
}
