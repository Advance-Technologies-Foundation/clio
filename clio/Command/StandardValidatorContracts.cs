using System;
using System.Collections.Generic;

namespace Clio.Command;

/// <summary>
/// Defines parameter contracts used by Creatio's built-in Freedom UI validators.
/// </summary>
internal static class StandardValidatorContracts {
	private static readonly IReadOnlyDictionary<string, string[]> Contracts =
		new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
			["crt.Required"] = [],
			["crt.EmptyOrWhiteSpace"] = [],
			["crt.MinLength"] = ["minLength"],
			["crt.MaxLength"] = ["maxLength"],
			["crt.Min"] = ["min"],
			["crt.Max"] = ["max"]
		};

	/// <summary>
	/// Returns the immutable built-in validator parameter contracts used by schema validation.
	/// </summary>
	internal static IReadOnlyDictionary<string, string[]> GetContracts() => Contracts;
}
