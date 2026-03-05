using System.Text.Json.Serialization;

namespace Clio.Common.Assertions;

/// <summary>
/// Represents resolved Redis endpoint data returned by assertion commands.
/// </summary>
public sealed record RedisAssertionResolvedDto{
	/// <summary>
	/// Gets Redis workload or endpoint display name.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>
	/// Gets Redis host used for connectivity and ping checks.
	/// </summary>
	[JsonPropertyName("host")]
	public string Host { get; init; }

	/// <summary>
	/// Gets Redis port used for connectivity and ping checks.
	/// </summary>
	[JsonPropertyName("port")]
	public int Port { get; init; }

	/// <summary>
	/// Gets first available empty Redis database index discovered by clio.
	/// </summary>
	[JsonPropertyName("firstAvailableDb")]
	public int FirstAvailableDb { get; init; }
}
