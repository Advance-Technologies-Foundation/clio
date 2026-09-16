using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.Administration;

/// <summary>Native response contracts used by Creatio administration services.</summary>
public enum AdministrationResponseKind {

	/// <summary>An empty result string succeeds; a nonempty string reports failure.</summary>
	ErrorString,
	/// <summary>The result contains encoded JSON with an explicit success boolean.</summary>
	SuccessObject,
	/// <summary>The result must be the boolean true.</summary>
	True,
	/// <summary>The result is a boolean value, including false.</summary>
	Boolean,
	/// <summary>The result contains a JSON-encoded array.</summary>
	Array
}

/// <summary>Calls native administration services and performs bounded explicit-column inspection.</summary>
public interface IAdministrationClient {

	/// <summary>Posts one request and validates its method-specific result contract.</summary>
	/// <param name="route">Registered native service endpoint.</param>
	/// <param name="resultProperty">Expected wrapped method result property.</param>
	/// <param name="body">Typed or dictionary payload created by the administration service.</param>
	/// <param name="kind">The endpoint's expected response contract.</param>
	/// <returns>The validated result, independent of the response document's lifetime.</returns>
	JsonElement Post(ServiceUrlBuilder.KnownRoute route, string resultProperty, object body,
		AdministrationResponseKind kind);

	/// <summary>Reads explicitly selected columns using exact equality filters and stable ID ordering.</summary>
	/// <param name="schema">Administration schema selected by the calling service.</param>
	/// <param name="columns">Explicit non-secret column allowlist.</param>
	/// <param name="filters">Exact column-value comparisons.</param>
	/// <param name="offset">Zero-based row offset.</param>
	/// <param name="limit">Maximum rows, from one to two hundred.</param>
	/// <returns>The rows; missing columns or failed queries throw.</returns>
	JsonElement Select(string schema, IReadOnlyList<string> columns,
		IReadOnlyDictionary<string, object> filters, int offset = 0, int limit = 100);

	/// <summary>Writes one exact administration entity through native DataService entity events.</summary>
	/// <remarks>Only the service's fixed schema and column allowlists may call this method.</remarks>
	void WriteEntity(string schema, Guid id, IReadOnlyDictionary<string, object> values, bool insert);
	/// <summary>Deletes one exact entity through native DataService entity events.</summary>
	void DeleteEntity(string schema, Guid id);
}
