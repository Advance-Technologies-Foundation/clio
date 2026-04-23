using System;
using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Clio.Analyzers;

/// <summary>
/// Reports diagnostics when code writes output through <see cref="System.Console"/>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConsoleOutputAnalyzer : DiagnosticAnalyzer {
	private static readonly DiagnosticDescriptor Rule = new(
		"CLIO002",
		"Avoid direct Console output",
		"Use ConsoleLogger/ILogger instead of Console.{0}",
		"Logging",
		DiagnosticSeverity.Warning,
		true,
		"Prefer centralized logging abstraction instead of direct Console output.");

	private static readonly ImmutableHashSet<string> ConsoleOutputMethods = ImmutableHashSet.Create(
		"Write",
		"WriteLine");
	private static readonly ImmutableHashSet<string> ConsoleOutputProperties = ImmutableHashSet.Create(
		"Out",
		"OutputEncoding");

	private static readonly ImmutableHashSet<string> ExemptNamespaces = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"Clio.Command.Quiz");

	private static readonly ImmutableHashSet<string> ExemptTypeFullNames = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"Clio.Program",
		"Clio.EnvironmentSettings");

	#region Properties: Public

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

	#endregion

	#region Methods: Private

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context) {
		MemberAccessExpressionSyntax memberAccess = (MemberAccessExpressionSyntax)context.Node;
		if (memberAccess.Parent is MemberAccessExpressionSyntax parentMemberAccess
			&& parentMemberAccess.Expression == memberAccess
			&& parentMemberAccess.Parent is InvocationExpressionSyntax invocation
			&& invocation.Expression == parentMemberAccess) {
			return;
		}

		if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IPropertySymbol propertySymbol) {
			return;
		}

		if (!ConsoleOutputProperties.Contains(propertySymbol.Name)) {
			return;
		}

		if (!IsConsoleType(propertySymbol.ContainingType)) {
			return;
		}

		if (IsClioConsoleLogger(context) || IsExemptContext(context)) {
			return;
		}

		Diagnostic diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), propertySymbol.Name);
		context.ReportDiagnostic(diagnostic);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context) {
		InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
		if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken)
				   .Symbol is not IMethodSymbol methodSymbol) {
			return;
		}

		if (IsClioConsoleLogger(context) || IsExemptContext(context)) {
			return;
		}

		INamedTypeSymbol containingType = methodSymbol.ContainingType;
		if (IsConsoleType(containingType) && ConsoleOutputMethods.Contains(methodSymbol.Name)) {
			Diagnostic directConsoleDiagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), methodSymbol.Name);
			context.ReportDiagnostic(directConsoleDiagnostic);
			return;
		}

		if (invocation.Expression is not MemberAccessExpressionSyntax invocationMemberAccess) {
			return;
		}

		if (context.SemanticModel.GetSymbolInfo(invocationMemberAccess.Expression, context.CancellationToken).Symbol is not IPropertySymbol propertySymbol) {
			return;
		}

		if (!ConsoleOutputProperties.Contains(propertySymbol.Name) || !IsConsoleType(propertySymbol.ContainingType)) {
			return;
		}

		Diagnostic consoleOutDiagnostic = Diagnostic.Create(
			Rule,
			invocation.GetLocation(),
			$"{propertySymbol.Name}.{methodSymbol.Name}");
		context.ReportDiagnostic(consoleOutDiagnostic);
	}

	private static bool IsClioConsoleLogger(SyntaxNodeAnalysisContext context) {
		INamedTypeSymbol? containingClass = context.ContainingSymbol?.ContainingType;
		return containingClass?.Name == "ConsoleLogger"
			&& containingClass.ContainingNamespace.ToDisplayString().StartsWith("Clio", StringComparison.Ordinal);
	}

	private static bool IsExemptContext(SyntaxNodeAnalysisContext context) {
		INamedTypeSymbol? containingClass = context.ContainingSymbol?.ContainingType;
		if (containingClass is null) {
			return false;
		}

		string fullTypeName = containingClass.ToDisplayString();
		if (ExemptTypeFullNames.Contains(fullTypeName)) {
			return true;
		}

		string namespaceName = containingClass.ContainingNamespace.ToDisplayString();
		foreach (string exempt in ExemptNamespaces) {
			if (namespaceName == exempt || namespaceName.StartsWith(exempt + ".", StringComparison.Ordinal)) {
				return true;
			}
		}

		return false;
	}

	private static bool IsConsoleType(INamedTypeSymbol typeSymbol) {
		return typeSymbol is not null && typeSymbol.ToDisplayString() == "System.Console";
	}

	private static bool IsTestAssembly(Compilation compilation) {
		string assemblyName = compilation.AssemblyName ?? string.Empty;
		return assemblyName.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext => {
			if (IsTestAssembly(startContext.Compilation)) {
				return;
			}

			startContext.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
			startContext.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
		});
	}

	#endregion
}
