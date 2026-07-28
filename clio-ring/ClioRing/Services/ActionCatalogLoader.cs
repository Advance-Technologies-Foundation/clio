using System;
using System.IO;
using System.Text.Json;
using ClioRing.Models;

namespace ClioRing.Services;

/// <summary>
/// Default <see cref="IActionCatalogLoader"/>. Deserializes via the source-generated
/// <see cref="RingJsonContext"/> (AOT-safe) then enforces the closed-envelope contract:
/// every action's <see cref="ActionKind"/> must have exactly its matching typed block.
/// </summary>
public sealed class ActionCatalogLoader : IActionCatalogLoader {
	/// <summary>Default file name looked up next to the executable.</summary>
	public const string DefaultFileName = "actions.json";

	/// <inheritdoc />
	public ActionCatalog Load(string? path = null) {
		string resolved = path ?? Path.Combine(AppContext.BaseDirectory, DefaultFileName);

		if (!File.Exists(resolved)) {
			throw new ActionCatalogException($"Action catalog not found: {resolved}");
		}

		ActionCatalog? catalog;
		try {
			string json = File.ReadAllText(resolved);
			catalog = JsonSerializer.Deserialize(json, RingJsonContext.Default.ActionCatalog);
		}
		catch (JsonException ex) {
			// Covers malformed JSON and unknown enum values (string enum converter throws here).
			throw new ActionCatalogException($"Invalid action catalog '{resolved}': {ex.Message}", ex);
		}

		if (catalog is null) {
			throw new ActionCatalogException($"Action catalog '{resolved}' deserialized to null.");
		}

		Validate(catalog, resolved);
		return catalog;
	}

	private static void Validate(ActionCatalog catalog, string source) {
		var seenIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (RingAction action in catalog.Actions) {
			if (string.IsNullOrWhiteSpace(action.Id)) {
				throw new ActionCatalogException($"Action in '{source}' has an empty Id.");
			}

			if (!seenIds.Add(action.Id)) {
				throw new ActionCatalogException($"Duplicate action Id '{action.Id}' in '{source}'.");
			}

			if (string.IsNullOrWhiteSpace(action.Title)) {
				throw new ActionCatalogException($"Action '{action.Id}' in '{source}' has an empty Title.");
			}

			if (!Enum.IsDefined(action.Kind)) {
				throw new ActionCatalogException($"Action '{action.Id}' has an unknown Kind.");
			}

			ValidateKindBlocks(action, source);
			ValidateParameters(action, source);
		}
	}

	private static void ValidateKindBlocks(RingAction action, string source) {
		// Exactly the block matching Kind must be present; the other two must be absent.
		bool clio = action.ClioCommand is not null;
		bool url = action.OpenUrl is not null;
		bool path = action.OpenPath is not null;

		switch (action.Kind) {
			case ActionKind.ClioCommand:
				Require(clio, action, "ClioCommand", source);
				Forbid(url || path, action, "OpenUrl/OpenPath", source);
				if (string.IsNullOrWhiteSpace(action.ClioCommand!.Verb)) {
					throw new ActionCatalogException($"Action '{action.Id}' ClioCommand.Verb is empty.");
				}
				break;
			case ActionKind.OpenUrl:
				Require(url, action, "OpenUrl", source);
				Forbid(clio || path, action, "ClioCommand/OpenPath", source);
				if (string.IsNullOrWhiteSpace(action.OpenUrl!.Url)) {
					throw new ActionCatalogException($"Action '{action.Id}' OpenUrl.Url is empty.");
				}
				break;
			case ActionKind.OpenPath:
				Require(path, action, "OpenPath", source);
				Forbid(clio || url, action, "ClioCommand/OpenUrl", source);
				if (string.IsNullOrWhiteSpace(action.OpenPath!.Path)) {
					throw new ActionCatalogException($"Action '{action.Id}' OpenPath.Path is empty.");
				}
				break;
			case ActionKind.GuidedInstall:
				// A guided-install action carries no typed command block; the ring host opens the form.
				Forbid(clio || url || path, action, "ClioCommand/OpenUrl/OpenPath", source);
				break;
			case ActionKind.GuidedUninstall:
				// A guided-uninstall action carries no typed command block; the ring host opens the flow.
				Forbid(clio || url || path, action, "ClioCommand/OpenUrl/OpenPath", source);
				break;
			default:
				throw new ActionCatalogException($"Action '{action.Id}' has an unhandled Kind '{action.Kind}'.");
		}
	}

	private static void ValidateParameters(RingAction action, string source) {
		foreach (ParameterDescriptor p in action.Parameters) {
			if (string.IsNullOrWhiteSpace(p.Name)) {
				throw new ActionCatalogException($"Action '{action.Id}' has a parameter with an empty Name in '{source}'.");
			}
			if (!Enum.IsDefined(p.ParameterType)) {
				throw new ActionCatalogException($"Action '{action.Id}' parameter '{p.Name}' has an unknown ParameterType.");
			}
		}
	}

	private static void Require(bool present, RingAction action, string block, string source) {
		if (!present) {
			throw new ActionCatalogException(
				$"Action '{action.Id}' in '{source}' declares Kind={action.Kind} but its '{block}' block is missing.");
		}
	}

	private static void Forbid(bool present, RingAction action, string blocks, string source) {
		if (present) {
			throw new ActionCatalogException(
				$"Action '{action.Id}' in '{source}' declares Kind={action.Kind} but also populates '{blocks}'.");
		}
	}
}
