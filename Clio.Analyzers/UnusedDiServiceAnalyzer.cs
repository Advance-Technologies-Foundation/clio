using System;
using System.Collections.Concurrent;
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
///     Reports diagnostics when a service is registered through Microsoft DI but is never injected
///     or resolved anywhere in the compilation, indicating possible dead code.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnusedDiServiceAnalyzer : DiagnosticAnalyzer{
	private static readonly DiagnosticDescriptor Rule = new(
		"CLIO005",
		"DI service is registered but never resolved",
		"Service '{0}' is registered in DI but never injected or resolved; it may be dead code. Inject/resolve it, remove the registration, or mark it [ResolvedDynamically].",
		"DependencyInjection",
		DiagnosticSeverity.Warning,
		true,
		"Services registered in dependency injection that are never injected or resolved are likely dead code.",
		customTags: [WellKnownDiagnosticTags.CompilationEnd]);

	private static readonly ImmutableHashSet<string> RegistrationMethodNames = ImmutableHashSet.Create(
		"AddSingleton",
		"AddScoped",
		"AddTransient");

	private static readonly ImmutableHashSet<string> ResolutionMethodNames = ImmutableHashSet.Create(
		"GetService",
		"GetRequiredService",
		"GetServices");

	private const string ResolvedDynamicallyAttributeName = "ResolvedDynamicallyAttribute";

	// Attribute simple names that signal a registered type is consumed by reflection rather than
	// by an explicit inject/resolve the analyzer can observe. ResolvedDynamicallyAttribute is a
	// type-level opt-out; McpServerToolAttribute is placed on the tool method (declared on the
	// registered tool type) and is discovered by McpToolSchemaCatalog's assembly scan at runtime.
	private static readonly ImmutableHashSet<string> ReflectionConsumedAttributeNames = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		ResolvedDynamicallyAttributeName,
		"McpServerToolAttribute");

	private static readonly ImmutableHashSet<string> EnumerableWrapperMetadataNames = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"IEnumerable`1",
		"IReadOnlyList`1",
		"IReadOnlyCollection`1",
		"ICollection`1",
		"IList`1",
		"List`1");

	#region Properties: Public

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

	#endregion

	#region Methods: Private

	private static bool IsTestAssembly(Compilation compilation) {
		string assemblyName = compilation.AssemblyName ?? string.Empty;
		return assemblyName.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static void AnalyzeInvocation(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		ConcurrentDictionary<INamedTypeSymbol, Registration> registrations,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed,
		CancellationToken cancellationToken) {
		SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
		IMethodSymbol? methodSymbol = symbolInfo.Symbol as IMethodSymbol
			?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

		string invokedName = GetInvokedMethodName(invocation);

		if (RegistrationMethodNames.Contains(invokedName) && IsServiceCollectionRegistration(methodSymbol)) {
			CollectRegistration(semanticModel, invocation, methodSymbol, registrations, cancellationToken);
			return;
		}

		if (ResolutionMethodNames.Contains(invokedName)) {
			CollectResolution(semanticModel, invocation, methodSymbol, consumed, cancellationToken);
			return;
		}

		// Any other generic method invocation (e.g. a custom dispatch helper such as
		// Resolve<ExtractPackageCommand>() or LINQ's OfType<Foo>()) consumes its concrete
		// type arguments. Service-collection Add* registrations are handled above and
		// returned early, so they never reach here and cannot count as self-consumption.
		CollectGenericInvocationTypeArguments(semanticModel, invocation, methodSymbol, consumed, cancellationToken);
	}

	private static void CollectGenericInvocationTypeArguments(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		IMethodSymbol? methodSymbol,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed,
		CancellationToken cancellationToken) {
		// Bound symbol: use the substituted type arguments. Unbound type parameters
		// (e.g. a method/class type parameter T) are not INamedTypeSymbol and are skipped
		// by AddConsumed, which is the desired behavior.
		if (methodSymbol is not null && methodSymbol.TypeArguments.Length > 0) {
			foreach (ITypeSymbol typeArgument in methodSymbol.TypeArguments) {
				AddConsumed(consumed, typeArgument);
			}
			return;
		}

		// Syntax-level fallback when the symbol could not be bound.
		if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax memberGeneric }) {
			CollectTypeArgumentSyntaxes(semanticModel, memberGeneric, consumed, cancellationToken);
			return;
		}

		if (invocation.Expression is GenericNameSyntax directGeneric) {
			CollectTypeArgumentSyntaxes(semanticModel, directGeneric, consumed, cancellationToken);
		}
	}

	private static void CollectTypeArgumentSyntaxes(
		SemanticModel semanticModel,
		GenericNameSyntax genericName,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed,
		CancellationToken cancellationToken) {
		foreach (TypeSyntax arg in genericName.TypeArgumentList.Arguments) {
			if (semanticModel.GetTypeInfo(arg, cancellationToken).Type is { } t) {
				AddConsumed(consumed, t);
			}
		}
	}

	private static void AnalyzeObjectCreation(
		SemanticModel semanticModel,
		ObjectCreationExpressionSyntax objectCreation,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed,
		CancellationToken cancellationToken) {
		// Generic object creation new Bar<Foo>() consumes the type argument Foo, NOT Bar.
		if (objectCreation.Type is not GenericNameSyntax genericName) {
			return;
		}

		CollectTypeArgumentSyntaxes(semanticModel, genericName, consumed, cancellationToken);
	}

	private static void AnalyzeTypeOf(
		SemanticModel semanticModel,
		TypeOfExpressionSyntax typeOf,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed,
		CancellationToken cancellationToken) {
		// A typeof(Foo) expression is a reflection-resolution signal and consumes Foo,
		// EXCEPT when it is an argument to a service-collection Add* registration
		// (that is the registration under test, not a consumption of it).
		if (IsTypeOfInsideRegistration(semanticModel, typeOf)) {
			return;
		}

		if (semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type is { } typeSymbol) {
			AddConsumed(consumed, typeSymbol);
		}
	}

	private static bool IsTypeOfInsideRegistration(SemanticModel semanticModel, TypeOfExpressionSyntax typeOf) {
		if (typeOf.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } }) {
			return false;
		}

		string invokedName = GetInvokedMethodName(invocation);
		if (!RegistrationMethodNames.Contains(invokedName)) {
			return false;
		}

		IMethodSymbol? methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol
			?? semanticModel.GetSymbolInfo(invocation).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
		return IsServiceCollectionRegistration(methodSymbol);
	}

	private static string GetInvokedMethodName(InvocationExpressionSyntax invocation) {
		return invocation.Expression switch {
			MemberAccessExpressionSyntax memberAccess => memberAccess.Name switch {
				IdentifierNameSyntax id => id.Identifier.ValueText,
				GenericNameSyntax generic => generic.Identifier.ValueText,
				_ => string.Empty
			},
			IdentifierNameSyntax id => id.Identifier.ValueText,
			GenericNameSyntax generic => generic.Identifier.ValueText,
			_ => string.Empty
		};
	}

	private static bool IsServiceCollectionRegistration(IMethodSymbol? methodSymbol) {
		if (methodSymbol is null) {
			return false;
		}

		IMethodSymbol sourceMethod = methodSymbol.ReducedFrom ?? methodSymbol;
		string ns = sourceMethod.ContainingNamespace?.ToDisplayString() ?? string.Empty;
		return ns.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal);
	}

	private static void CollectRegistration(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		IMethodSymbol? methodSymbol,
		ConcurrentDictionary<INamedTypeSymbol, Registration> registrations,
		CancellationToken cancellationToken) {
		ImmutableArray<ITypeSymbol> typeArguments = ResolveRegistrationTypeArguments(
			semanticModel, invocation, methodSymbol, cancellationToken);
		if (typeArguments.Length == 0) {
			return;
		}

		INamedTypeSymbol? service = typeArguments[0] as INamedTypeSymbol;
		INamedTypeSymbol? impl = typeArguments.Length >= 2
			? typeArguments[1] as INamedTypeSymbol
			: service;

		if (service is null || impl is null) {
			return;
		}

		// Skip open generics for v1 (e.g. AddTransient(typeof(IFoo<>), typeof(Foo<>))).
		if (service.IsUnboundGenericType || impl.IsUnboundGenericType) {
			return;
		}

		INamedTypeSymbol serviceKey = (INamedTypeSymbol)service.OriginalDefinition;
		Registration registration = new(serviceKey, (INamedTypeSymbol)impl.OriginalDefinition, invocation.GetLocation());
		registrations.TryAdd(serviceKey, registration);
	}

	private static ImmutableArray<ITypeSymbol> ResolveRegistrationTypeArguments(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		IMethodSymbol? methodSymbol,
		CancellationToken cancellationToken) {
		// Generic registration: Add*<TService>() or Add*<TService, TImpl>().
		if (methodSymbol is not null && methodSymbol.TypeArguments.Length > 0) {
			return methodSymbol.TypeArguments;
		}

		// Syntax-level fallback for generic args when the symbol could not be bound.
		if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
			&& genericName.TypeArgumentList.Arguments.Count > 0) {
			List<ITypeSymbol> syntaxTypes = [];
			foreach (TypeSyntax arg in genericName.TypeArgumentList.Arguments) {
				if (semanticModel.GetTypeInfo(arg, cancellationToken).Type is { } t) {
					syntaxTypes.Add(t);
				}
			}
			if (syntaxTypes.Count > 0) {
				return [.. syntaxTypes];
			}
		}

		// Non-generic registration using typeof(...) arguments.
		List<ITypeSymbol> typeOfTypes = [];
		foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments) {
			if (argument.Expression is TypeOfExpressionSyntax typeOf
				&& semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type is { } typeOfSymbol) {
				typeOfTypes.Add(typeOfSymbol);
			}
		}
		return typeOfTypes.Count > 0 ? [.. typeOfTypes] : ImmutableArray<ITypeSymbol>.Empty;
	}

	private static void CollectResolution(
		SemanticModel semanticModel,
		InvocationExpressionSyntax invocation,
		IMethodSymbol? methodSymbol,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed,
		CancellationToken cancellationToken) {
		// Generic resolution: GetService<T>() / GetRequiredService<T>() / GetServices<T>().
		if (methodSymbol is not null && methodSymbol.TypeArguments.Length > 0) {
			foreach (ITypeSymbol typeArgument in methodSymbol.TypeArguments) {
				AddConsumed(consumed, typeArgument);
			}
			return;
		}

		if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }) {
			foreach (TypeSyntax arg in genericName.TypeArgumentList.Arguments) {
				if (semanticModel.GetTypeInfo(arg, cancellationToken).Type is { } t) {
					AddConsumed(consumed, t);
				}
			}
			return;
		}

		// Non-generic resolution: GetService(typeof(T)) / GetRequiredService(typeof(T)).
		foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments) {
			if (argument.Expression is TypeOfExpressionSyntax typeOf
				&& semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type is { } typeOfSymbol) {
				AddConsumed(consumed, typeOfSymbol);
			}
		}
	}

	private static void CollectConstructorParameters(
		IMethodSymbol constructor,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed) {
		foreach (IParameterSymbol parameter in constructor.Parameters) {
			AddConsumed(consumed, parameter.Type);
		}
	}

	private static void AddConsumed(ConcurrentDictionary<INamedTypeSymbol, byte> consumed, ITypeSymbol type) {
		if (type is INamedTypeSymbol named) {
			consumed.TryAdd((INamedTypeSymbol)named.OriginalDefinition, 0);

			// Also record the constructed form (e.g. IValidator<ExternalLinkOptions>) so the
			// CompilationEnd interface-liveness check can match it constructed-to-constructed against
			// a registered type's AllInterfaces. The OriginalDefinition entry above keeps direct
			// service/impl consumption lookups (which key on OriginalDefinition) working unchanged.
			if (!SymbolEqualityComparer.Default.Equals(named, named.OriginalDefinition)) {
				consumed.TryAdd(named, 0);
			}

			// IEnumerable<T> / IReadOnlyList<T> / List<T> etc. -> also consume the element type T.
			if (named.IsGenericType
				&& named.TypeArguments.Length == 1
				&& EnumerableWrapperMetadataNames.Contains(named.OriginalDefinition.MetadataName)
				&& named.TypeArguments[0] is INamedTypeSymbol elementType) {
				consumed.TryAdd((INamedTypeSymbol)elementType.OriginalDefinition, 0);
			}
			return;
		}

		// T[] -> consume the element type T.
		if (type is IArrayTypeSymbol arrayType && arrayType.ElementType is INamedTypeSymbol arrayElement) {
			consumed.TryAdd((INamedTypeSymbol)arrayElement.OriginalDefinition, 0);
		}
	}

	private static bool ImplementsConsumedInterface(
		INamedTypeSymbol type,
		ConcurrentDictionary<INamedTypeSymbol, byte> consumed) {
		// AllInterfaces covers the type's own interfaces plus all transitively inherited ones,
		// and yields constructed forms (e.g. IValidator<ExternalLinkOptions>). The consumed set
		// is keyed with SymbolEqualityComparer.Default, so a constructed interface present in
		// AllInterfaces matches the constructed interface recorded from a constructor parameter.
		foreach (INamedTypeSymbol implementedInterface in type.AllInterfaces) {
			if (consumed.ContainsKey(implementedInterface)) {
				return true;
			}
		}

		return false;
	}

	private static bool IsReflectionConsumed(INamedTypeSymbol type) {
		// Type-level signal (e.g. [ResolvedDynamically]).
		if (HasReflectionConsumedAttribute(type)) {
			return true;
		}

		// Member-level signal: the MCP tool attribute sits on a declared instance method of the
		// registered tool type, mirroring McpToolSchemaCatalog's DeclaredOnly assembly scan.
		// Declared members only (no base walk) keeps the analyzer's notion of "is a tool" identical
		// to the runtime catalog.
		foreach (ISymbol member in type.GetMembers()) {
			if (member is IMethodSymbol && HasReflectionConsumedAttribute(member)) {
				return true;
			}
		}

		return false;
	}

	private static bool HasReflectionConsumedAttribute(ISymbol symbol) {
		return symbol.GetAttributes().Any(attr =>
			attr.AttributeClass?.Name is { } name && ReflectionConsumedAttributeNames.Contains(name));
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

			ConcurrentDictionary<INamedTypeSymbol, Registration> registrations
				= new(SymbolEqualityComparer.Default);
			ConcurrentDictionary<INamedTypeSymbol, byte> consumed
				= new(SymbolEqualityComparer.Default);

			startContext.RegisterSyntaxNodeAction(
				syntaxContext => AnalyzeInvocation(
					syntaxContext.SemanticModel,
					(InvocationExpressionSyntax)syntaxContext.Node,
					registrations,
					consumed,
					syntaxContext.CancellationToken),
				SyntaxKind.InvocationExpression);

			startContext.RegisterSyntaxNodeAction(
				syntaxContext => AnalyzeObjectCreation(
					syntaxContext.SemanticModel,
					(ObjectCreationExpressionSyntax)syntaxContext.Node,
					consumed,
					syntaxContext.CancellationToken),
				SyntaxKind.ObjectCreationExpression);

			startContext.RegisterSyntaxNodeAction(
				syntaxContext => AnalyzeTypeOf(
					syntaxContext.SemanticModel,
					(TypeOfExpressionSyntax)syntaxContext.Node,
					consumed,
					syntaxContext.CancellationToken),
				SyntaxKind.TypeOfExpression);

			startContext.RegisterSymbolAction(
				symbolContext => {
					if (symbolContext.Symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor) {
						CollectConstructorParameters(constructor, consumed);
					}
				},
				SymbolKind.Method);

			startContext.RegisterCompilationEndAction(endContext => {
				foreach (Registration registration in registrations.Values) {
					bool serviceConsumed = consumed.ContainsKey(registration.Service);
					bool implConsumed = consumed.ContainsKey(registration.Implementation);
					if (serviceConsumed || implConsumed) {
						continue;
					}

					if (IsReflectionConsumed(registration.Service)
						|| IsReflectionConsumed(registration.Implementation)) {
						continue;
					}

					// Interface-liveness: a concrete type registered explicitly (e.g.
					// AddTransient<ExternalLinkOptionsValidator>()) is alive when consumers inject one
					// of the interfaces it implements (e.g. IValidator<ExternalLinkOptions>) and the
					// reflection registration loops (RegisterFluentValidators /
					// RegisterAssemblyInterfaceTypes) wire the concrete to that interface at runtime.
					// Match constructed-to-constructed (consumed holds the constructed interface; see
					// AddConsumed) so IValidator<ExternalLinkOptions> matches exactly and the open
					// IValidator<> alone does not over-suppress.
					// Known, accepted limitation: a genuinely dead concrete type that merely implements
					// a consumed interface (but is not the registered impl actually wired to it) is
					// missed. This is an accepted trade-off to avoid false-flagging the legitimate
					// concrete+interface pattern that clio's reflection registration loops create.
					if (ImplementsConsumedInterface(registration.Implementation, consumed)
						|| ImplementsConsumedInterface(registration.Service, consumed)) {
						continue;
					}

					endContext.ReportDiagnostic(Diagnostic.Create(
						Rule,
						registration.Location,
						registration.Service.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
				}
			});
		});
	}

	#endregion

	private sealed class Registration{
		public Registration(INamedTypeSymbol service, INamedTypeSymbol implementation, Location location) {
			Service = service;
			Implementation = implementation;
			Location = location;
		}

		public INamedTypeSymbol Service { get; }

		public INamedTypeSymbol Implementation { get; }

		public Location Location { get; }
	}
}
