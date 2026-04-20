using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Clio.Analyzers;

/// <summary>
///     Reports diagnostics when a type registered through Microsoft DI is instantiated manually with <c>new</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyInjectionManualConstructionAnalyzer : DiagnosticAnalyzer{
	private static readonly DiagnosticDescriptor Rule = new(
		"CLIO001",
		"Avoid manual construction of DI-registered services",
		"Type '{0}' is registered in DI and should be resolved from the container instead of using 'new'",
		"DependencyInjection",
		DiagnosticSeverity.Warning,
		true,
		"Behavior classes registered in dependency injection should not be manually constructed.");

	private static readonly ImmutableHashSet<string> RegistrationMethodNames = ImmutableHashSet.Create(
		"AddSingleton",
		"AddScoped",
		"AddTransient",
		"TryAddSingleton",
		"TryAddScoped",
		"TryAddTransient",
		"Replace");

	#region Properties: Public

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

	#endregion

	#region Methods: Private

private static void AnalyzeObjectCreation(
		SyntaxNodeAnalysisContext context,
		ImmutableHashSet<INamedTypeSymbol> registeredTypes,
		ImmutableHashSet<string> registeredFullyQualifiedTypeNames,
		ImmutableHashSet<string> registeredSimpleTypeNames) {
		if (IsInsideRegistrationInvocation(context.SemanticModel, context.Node, context.CancellationToken)) {
			return;
		}

		ITypeSymbol? creationType = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type
			?? context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).ConvertedType;
		INamedTypeSymbol? namedType = creationType as INamedTypeSymbol;

		if (namedType?.IsRecord == true) {
			return;
		}

		INamedTypeSymbol? containingType = context.ContainingSymbol?.ContainingType;
		if (namedType is not null && SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, containingType?.OriginalDefinition)) {
			return;
		}

		if (IsInsideFactoryClass(context)) {
			return;
		}

		string typeName = namedType?.Name ?? GetTypeNameFromCreationSyntax(context.Node);
		string displayName = namedType?.ToDisplayString() ?? typeName;

		bool isRegisteredBySymbol = namedType is not null && registeredTypes.Contains(namedType.OriginalDefinition);
		bool isRegisteredByName = false;

		if (namedType is not null) {
			string fullyQualifiedName = NormalizeTypeName(
				namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
			isRegisteredByName = registeredFullyQualifiedTypeNames.Contains(fullyQualifiedName);
		}
		else {
			// Fall back to simple-name matching only when semantic binding is unavailable.
			string syntaxTypeName = NormalizeTypeName(GetTypeTextFromCreationSyntax(context.Node));
			isRegisteredByName = registeredFullyQualifiedTypeNames.Contains(syntaxTypeName);
			if (!isRegisteredByName && IsSimpleCreationSyntax(context.Node)) {
				isRegisteredByName = registeredSimpleTypeNames.Contains(typeName);
			}
		}

		bool isLikelyDiService = namedType is not null && IsLikelyDiServiceType(namedType);

		if (!isRegisteredBySymbol && !isRegisteredByName && !isLikelyDiService) {
			return;
		}

		Diagnostic diagnostic = Diagnostic.Create(Rule, context.Node.GetLocation(), displayName);
		context.ReportDiagnostic(diagnostic);
	}

	private static bool IsInsideFactoryClass(SyntaxNodeAnalysisContext context) {
		INamedTypeSymbol? containingClass = context.ContainingSymbol?.ContainingType;
		if (containingClass is null) {
			return false;
		}

		if (containingClass.Name.EndsWith("Factory", StringComparison.Ordinal)) {
			return true;
		}

		return containingClass.AllInterfaces.Any(i =>
			i.Name.EndsWith("Factory", StringComparison.Ordinal));
	}

	private static bool IsLikelyDiServiceType(INamedTypeSymbol typeSymbol) {
		string ns = typeSymbol.ContainingNamespace.ToDisplayString();
		if (!ns.StartsWith("Clio", StringComparison.Ordinal)) {
			return false;
		}

		string expectedInterfaceName = $"I{typeSymbol.Name}";
		return typeSymbol.AllInterfaces.Any(i =>
			i.Name.Equals(expectedInterfaceName, StringComparison.Ordinal)
			&& i.ContainingNamespace.ToDisplayString().StartsWith("Clio", StringComparison.Ordinal));
	}

	private static ImmutableHashSet<INamedTypeSymbol> CollectRegisteredTypes(
		Compilation compilation,
		CancellationToken cancellationToken,
		out ImmutableHashSet<string> registeredFullyQualifiedTypeNames,
		out ImmutableHashSet<string> registeredSimpleTypeNames) {
		HashSet<INamedTypeSymbol> result = new(SymbolEqualityComparer.Default);
		HashSet<string> fullyQualifiedNames = new(StringComparer.Ordinal);
		HashSet<string> simpleNames = new(StringComparer.Ordinal);

		foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
			if (cancellationToken.IsCancellationRequested) {
				break;
			}
			SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
			SyntaxNode root = syntaxTree.GetRoot(cancellationToken);
			IEnumerable<InvocationExpressionSyntax> invocations
				= root.DescendantNodes().OfType<InvocationExpressionSyntax>();

			foreach (InvocationExpressionSyntax invocation in invocations) {
				if (cancellationToken.IsCancellationRequested) {
					break;
				}

				if (!TryGetRegistrationMethod(semanticModel, invocation, cancellationToken,
						out IMethodSymbol? methodSymbol)) {
					continue;
				}

				foreach (ITypeSymbol type in GetTypesFromInvocation(semanticModel, invocation, methodSymbol,
							 cancellationToken)) {
					if (type is not INamedTypeSymbol namedType) {
						continue;
					}

					if (namedType.TypeKind is TypeKind.Interface or TypeKind.Delegate) {
						continue;
					}

					if (namedType.IsAbstract) {
						continue;
					}

					if (namedType.ContainingNamespace.ToDisplayString()
							.StartsWith("System", StringComparison.Ordinal)) {
						continue;
					}

					result.Add(namedType.OriginalDefinition);
					simpleNames.Add(namedType.Name);
					fullyQualifiedNames.Add(
						NormalizeTypeName(namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
				}
			}
		}

		registeredFullyQualifiedTypeNames = fullyQualifiedNames.ToImmutableHashSet(StringComparer.Ordinal);
		registeredSimpleTypeNames = simpleNames.ToImmutableHashSet(StringComparer.Ordinal);
		return result.ToImmutableHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
	}

	private static string NormalizeTypeName(string typeName) {
		return typeName.Replace("global::", string.Empty);
	}

	private static bool IsSimpleTypeSyntax(TypeSyntax typeSyntax) {
		return typeSyntax is IdentifierNameSyntax or GenericNameSyntax or NullableTypeSyntax;
	}

	private static string GetTypeNameFromCreationSyntax(SyntaxNode creationNode) {
		if (creationNode is ObjectCreationExpressionSyntax explicitCreation) {
			return GetTypeNameFromSyntax(explicitCreation.Type);
		}

		return creationNode.ToString();
	}

	private static string GetTypeTextFromCreationSyntax(SyntaxNode creationNode) {
		if (creationNode is ObjectCreationExpressionSyntax explicitCreation) {
			return explicitCreation.Type.ToString();
		}

		return creationNode.ToString();
	}

	private static bool IsSimpleCreationSyntax(SyntaxNode creationNode) {
		if (creationNode is ObjectCreationExpressionSyntax explicitCreation) {
			return IsSimpleTypeSyntax(explicitCreation.Type);
		}

		return false;
	}

	private static string GetTypeNameFromSyntax(TypeSyntax typeSyntax) {
		return typeSyntax switch {
			IdentifierNameSyntax id => id.Identifier.ValueText,
			GenericNameSyntax generic => generic.Identifier.ValueText,
			QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
			AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
			NullableTypeSyntax nullable => GetTypeNameFromSyntax(nullable.ElementType),
			_ => typeSyntax.ToString()
		};
	}

	private static IEnumerable<ITypeSymbol> ExtractObjectCreationTypes(
		SemanticModel semanticModel,
		CSharpSyntaxNode body,
		CancellationToken cancellationToken) {
		IEnumerable<ObjectCreationExpressionSyntax> objectCreations = body is ExpressionSyntax expression
			? expression.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>()
			: body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();

		foreach (ObjectCreationExpressionSyntax creation in objectCreations) {
			ITypeSymbol? type = semanticModel.GetTypeInfo(creation.Type, cancellationToken).Type;
			if (type is not null) {
				yield return type;
			}
		}
	}

	private static IEnumerable<ITypeSymbol> GetTypesFromInvocation(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		IMethodSymbol? methodSymbol,
		CancellationToken cancellationToken) {
		if (methodSymbol is not null) {
			foreach (ITypeSymbol type in GetTypesFromTypeArguments(methodSymbol)) {
				yield return type;
			}
		}

		foreach (ITypeSymbol type in GetTypesFromSyntaxTypeArguments(semanticModel, invocation, cancellationToken)) {
			yield return type;
		}

		foreach (ITypeSymbol type in GetTypesFromTypeOfArguments(semanticModel, invocation, cancellationToken)) {
			yield return type;
		}

		foreach (ITypeSymbol type in GetTypesFromRegistrationFactories(semanticModel, invocation, cancellationToken)) {
			yield return type;
		}
	}

	private static IEnumerable<ITypeSymbol> GetTypesFromSyntaxTypeArguments(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		CancellationToken cancellationToken) {
		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
			|| memberAccess.Name is not GenericNameSyntax genericName) {
			yield break;
		}

		string methodName = genericName.Identifier.ValueText;
		if (!RegistrationMethodNames.Contains(methodName)) {
			yield break;
		}

		SeparatedSyntaxList<TypeSyntax> args = genericName.TypeArgumentList.Arguments;
		if (args.Count == 0) {
			yield break;
		}

		int targetIndex = args.Count >= 2 ? 1 : 0;
		ITypeSymbol? type = semanticModel.GetTypeInfo(args[targetIndex], cancellationToken).Type;
		if (type is not null) {
			yield return type;
		}
	}

	private static IEnumerable<ITypeSymbol> GetTypesFromRegistrationFactories(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		CancellationToken cancellationToken) {
		foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments) {
			if (argument.Expression is ParenthesizedLambdaExpressionSyntax parenthesizedLambda) {
				foreach (ITypeSymbol type in ExtractObjectCreationTypes(semanticModel, parenthesizedLambda.Body,
							 cancellationToken)) {
					yield return type;
				}
			}
			else if (argument.Expression is SimpleLambdaExpressionSyntax simpleLambda) {
				foreach (ITypeSymbol type in ExtractObjectCreationTypes(semanticModel, simpleLambda.Body,
							 cancellationToken)) {
					yield return type;
				}
			}
			else if (argument.Expression is AnonymousMethodExpressionSyntax anonymousMethod) {
				foreach (ITypeSymbol type in ExtractObjectCreationTypes(semanticModel, anonymousMethod.Body,
							 cancellationToken)) {
					yield return type;
				}
			}
		}
	}

	private static IEnumerable<ITypeSymbol> GetTypesFromTypeArguments(IMethodSymbol methodSymbol) {
		ImmutableArray<ITypeSymbol> typeArguments = methodSymbol.TypeArguments;
		if (typeArguments.Length == 0) {
			yield break;
		}

		if (methodSymbol.Name is "AddSingleton" or "AddScoped" or "AddTransient"
			or "TryAddSingleton" or "TryAddScoped" or "TryAddTransient") {
			if (typeArguments.Length >= 2) {
				yield return typeArguments[1];
				yield break;
			}

			yield return typeArguments[0];
		}
	}

	private static IEnumerable<ITypeSymbol> GetTypesFromTypeOfArguments(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		CancellationToken cancellationToken) {
		foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments) {
			if (argument.Expression is not TypeOfExpressionSyntax typeOfExpression) {
				continue;
			}

			ITypeSymbol? type = semanticModel.GetTypeInfo(typeOfExpression.Type, cancellationToken).Type;
			if (type is not null) {
				yield return type;
			}
		}
	}

	private static bool IsInsideRegistrationInvocation(
		SemanticModel semanticModel,
		SyntaxNode creationNode,
		CancellationToken cancellationToken) {
		InvocationExpressionSyntax? invocation
			= creationNode.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
		if (invocation is null) {
			return false;
		}

		return TryGetRegistrationMethod(semanticModel, invocation, cancellationToken, out var _);
	}

	private static bool IsTestAssembly(Compilation compilation) {
		string assemblyName = compilation.AssemblyName ?? string.Empty;
		return assemblyName.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool TryGetRegistrationMethod(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		CancellationToken cancellationToken,
		out IMethodSymbol? methodSymbol) {
		SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
		methodSymbol = symbolInfo.Symbol as IMethodSymbol;
		if (methodSymbol is null) {
			methodSymbol = symbolInfo.CandidateSymbols
				.OfType<IMethodSymbol>()
				.FirstOrDefault(candidate => RegistrationMethodNames.Contains(candidate.Name));
		}
		if (methodSymbol is not null && RegistrationMethodNames.Contains(methodSymbol.Name)) {
			IMethodSymbol sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
			string ns = sourceMethod.ContainingNamespace.ToDisplayString();
			return ns.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal);
		}

		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) {
			return false;
		}

		string methodName = memberAccess.Name switch {
			IdentifierNameSyntax id => id.Identifier.ValueText,
			GenericNameSyntax generic => generic.Identifier.ValueText,
			_ => string.Empty
		};
		if (!RegistrationMethodNames.Contains(methodName)) {
			return false;
		}

		ITypeSymbol? receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
		if (receiverType is null) {
			return false;
		}

		if (receiverType.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.IServiceCollection") {
			return true;
		}

		return receiverType.AllInterfaces.Any(i =>
			i.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.IServiceCollection");
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

			ImmutableHashSet<INamedTypeSymbol> registeredTypes;
			ImmutableHashSet<string> registeredFullyQualifiedTypeNames;
			ImmutableHashSet<string> registeredSimpleTypeNames;
			try {
				registeredTypes = CollectRegisteredTypes(
					startContext.Compilation,
					CancellationToken.None,
					out registeredFullyQualifiedTypeNames,
					out registeredSimpleTypeNames);
			}
			catch (OperationCanceledException) {
				registeredTypes = ImmutableHashSet<INamedTypeSymbol>.Empty;
				registeredFullyQualifiedTypeNames = ImmutableHashSet<string>.Empty;
				registeredSimpleTypeNames = ImmutableHashSet<string>.Empty;
			}
			startContext.RegisterSyntaxNodeAction(
				syntaxContext => AnalyzeObjectCreation(
					syntaxContext,
					registeredTypes,
					registeredFullyQualifiedTypeNames,
					registeredSimpleTypeNames),
				SyntaxKind.ObjectCreationExpression,
				SyntaxKind.ImplicitObjectCreationExpression);
		});
	}

	#endregion
}
