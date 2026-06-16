using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Clio.Analyzers.Tests;

/// <summary>
/// Provides helper methods for executing analyzers against in-memory C# source code.
/// </summary>
internal static class AnalyzerTestRunner {
	/// <summary>
	/// Runs an analyzer against the provided source and returns diagnostics from that analyzer only.
	/// </summary>
	/// <param name="source">C# source code to analyze.</param>
	/// <param name="analyzer">Analyzer under test.</param>
	/// <param name="assemblyName">Optional assembly name used for compilation.</param>
	/// <returns>Diagnostics produced by the analyzer.</returns>
	public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(
		string source,
		DiagnosticAnalyzer analyzer,
		string assemblyName = "AnalyzerTargetAssembly") {
		SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
		CSharpCompilation compilation = CSharpCompilation.Create(
			assemblyName,
			[syntaxTree],
			GetMetadataReferences(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
		AnalysisResult result = await compilationWithAnalyzers.GetAnalysisResultAsync(System.Threading.CancellationToken.None);
		return result.GetAllDiagnostics();
	}

	private static MetadataReference[] GetMetadataReferences() {
		List<MetadataReference> references = [];
		string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
		foreach (string assemblyPath in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
			references.Add(MetadataReference.CreateFromFile(assemblyPath));
		}

		AddIfMissing(references, typeof(System.IO.Abstractions.IFileSystem).Assembly.Location);
		AddIfMissing(references, typeof(object).Assembly.Location);
		AddIfMissing(references, typeof(Enumerable).Assembly.Location);

		return references.ToArray();
	}

	private static void AddIfMissing(List<MetadataReference> references, string assemblyPath) {
		if (string.IsNullOrWhiteSpace(assemblyPath)) {
			return;
		}

		bool alreadyAdded = references
			.OfType<PortableExecutableReference>()
			.Any(reference => string.Equals(reference.FilePath, assemblyPath, StringComparison.OrdinalIgnoreCase));
		if (!alreadyAdded) {
			references.Add(MetadataReference.CreateFromFile(assemblyPath));
		}
	}
}
