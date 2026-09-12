namespace Clio;

/// <summary>Describes the optional local IdentityService owned by a registered environment.</summary>
public sealed record IdentityServiceAttachment {

	/// <summary>Whether the component contains no deployment or cleanup information.</summary>
	[Newtonsoft.Json.JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
	public bool IsEmpty => string.IsNullOrWhiteSpace(EnvironmentPath) && string.IsNullOrWhiteSpace(IisTarget)
		&& string.IsNullOrWhiteSpace(ApplicationPool) && string.IsNullOrWhiteSpace(Uri) && !CrmReferencesCleared;

	/// <summary>Absolute deployment directory; empty when no identity is attached.</summary>
	public string EnvironmentPath { get; init; } = string.Empty;
	/// <summary>Exact IIS site or application name, independent of the environment name.</summary>
	public string IisTarget { get; init; } = string.Empty;
	/// <summary>Recorded application pool, removed only while unused.</summary>
	public string ApplicationPool { get; init; } = string.Empty;
	/// <summary>IdentityService base address used to identify matching authentication references.</summary>
	public string Uri { get; init; } = string.Empty;
	/// <summary>Whether standalone cleanup already cleared or preserved the CRM references.</summary>
	/// <remarks>Allows an interrupted removal to resume without authenticating through a removed service.</remarks>
	public bool CrmReferencesCleared { get; init; }
}
