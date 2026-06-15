using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Clio.Common.ProcessDesigner;

/// <summary>
/// Reads versioned JavaScript "recipes" (the diagram-js driving scripts) shipped as embedded resources,
/// caching each by name after the first read.
/// </summary>
public static class ProcessDesignerRecipes {
	private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

	/// <summary>
	/// Returns the embedded recipe source for the given name (e.g. <c>read-data-element</c>), reading the
	/// assembly manifest stream once and caching the result.
	/// </summary>
	/// <param name="recipeName">The recipe name without the <c>.recipe.js</c> suffix.</param>
	/// <returns>The recipe JavaScript source.</returns>
	/// <exception cref="InvalidOperationException">The embedded recipe could not be found.</exception>
	public static string Get(string recipeName) => Cache.GetOrAdd(recipeName, Load);

	private static string Load(string recipeName) {
		string fileName = recipeName + ".recipe.js";
		Assembly assembly = typeof(ProcessDesignerRecipes).Assembly;
		string resourceName = assembly.GetManifestResourceNames()
			.FirstOrDefault(name => name.EndsWith(fileName, StringComparison.Ordinal))
			?? throw new InvalidOperationException(
				$"Embedded process-designer recipe '{fileName}' was not found in the assembly manifest.");
		using Stream stream = assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException($"Embedded process-designer recipe stream '{resourceName}' could not be opened.");
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}
}
