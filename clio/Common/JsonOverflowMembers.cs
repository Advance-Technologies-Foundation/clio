using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Clio.Common;

/// <summary>
/// Keeps a <c>[JsonExtensionData]</c> bag free of members the type already declares.
/// </summary>
/// <remarks>
/// A READ-ONLY property is serialized but cannot be deserialized, so Json.NET routes the value it read
/// into the overflow bag - and the next save then writes that member TWICE, once from the property and
/// once from the bag. A file with a duplicated key is still parsed by most readers, which is what makes
/// this silent: <c>appsettings.json</c> grew a second <c>"$schema"</c> on every round trip before this
/// existed. The bag is meant for members this build has never heard of; anything the contract already
/// names is not one.
/// </remarks>
internal static class JsonOverflowMembers {
	private static readonly DefaultContractResolver ContractResolver = new();

	/// <summary>Drops every bag entry whose name the owner's own JSON contract already declares.</summary>
	/// <param name="owner">The object the bag belongs to.</param>
	/// <param name="overflow">The overflow bag, which may be <see langword="null"/> or empty.</param>
	internal static void RemoveDeclaredMembers(object owner, IDictionary<string, JToken> overflow) {
		if (owner is null || overflow is null || overflow.Count == 0) {
			return;
		}
		if (ContractResolver.ResolveContract(owner.GetType()) is not JsonObjectContract contract) {
			return;
		}
		foreach (JsonProperty property in contract.Properties) {
			overflow.Remove(property.PropertyName);
		}
	}
}
