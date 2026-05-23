using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Clio.Analyzers;

/// <summary>
/// Reports diagnostics when core <c>System.IO</c> APIs are used directly instead of abstractions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectSystemIoUsageAnalyzer : DiagnosticAnalyzer {
	private static readonly DiagnosticDescriptor Rule = new(
		"CLIO003",
		"Avoid direct System.IO usage",
		"Use System.IO.Abstractions instead of System.IO.{0}",
		"FileSystem",
		DiagnosticSeverity.Warning,
		true,
		"Direct System.IO usage reduces testability. Use System.IO.Abstractions.");

	private static readonly ImmutableHashSet<string> ForbiddenTypeMetadataNames = ImmutableHashSet.Create(
		"System.IO.File",
		"System.IO.Directory",
		"System.IO.Path",
		"System.IO.FileInfo",
		"System.IO.DirectoryInfo",
		"System.IO.DriveInfo",
		"System.IO.FileStream");

	#region Properties: Public

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

	#endregion

	#region Methods: Private

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context) {
		InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
		if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol) {
			return;
		}

		if (!IsForbiddenType(methodSymbol.ContainingType)) {
			return;
		}

		if (IsFileSystemImplementation(context)) {
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), methodSymbol.ContainingType.Name));
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context) {
		MemberAccessExpressionSyntax memberAccess = (MemberAccessExpressionSyntax)context.Node;
		if (memberAccess.Parent is InvocationExpressionSyntax invocation && invocation.Expression == memberAccess) {
			return;
		}

		if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not ISymbol symbol) {
			return;
		}

		if (!IsForbiddenType(symbol.ContainingType)) {
			return;
		}

		if (IsFileSystemImplementation(context)) {
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation(), symbol.ContainingType.Name));
	}

	private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context) {
		ITypeSymbol? creationType = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type
			?? context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).ConvertedType;
		if (creationType is not INamedTypeSymbol namedType) {
			return;
		}

		if (!IsForbiddenType(namedType)) {
			return;
		}

		if (IsFileSystemImplementation(context)) {
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), namedType.Name));
	}

	private static bool IsFileSystemImplementation(SyntaxNodeAnalysisContext context) {
		INamedTypeSymbol? containingClass = context.ContainingSymbol?.ContainingType;
		return containingClass is not null
			&& containingClass.AllInterfaces.Any(i =>
				i.ToDisplayString() == "System.IO.Abstractions.IFileSystem");
	}

	private static bool IsForbiddenType(ITypeSymbol? typeSymbol) {
		if (typeSymbol is not INamedTypeSymbol namedType) {
			return false;
		}

		string fullName = namedType.OriginalDefinition.ToDisplayString();
		return ForbiddenTypeMetadataNames.Contains(fullName);
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

			startContext.RegisterSyntaxNodeAction(
				AnalyzeInvocation,
				SyntaxKind.InvocationExpression);
			startContext.RegisterSyntaxNodeAction(
				AnalyzeMemberAccess,
				SyntaxKind.SimpleMemberAccessExpression);
			startContext.RegisterSyntaxNodeAction(
				AnalyzeObjectCreation,
				SyntaxKind.ObjectCreationExpression,
				SyntaxKind.ImplicitObjectCreationExpression);
		});
	}

	#endregion
}
