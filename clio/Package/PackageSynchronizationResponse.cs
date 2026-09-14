using Clio.Common.Responses;
using Newtonsoft.Json;

namespace Clio.Package;

// The platform can report success=true and still reject individual descriptors.
internal sealed class PackageSynchronizationResponse : BaseResponse {
	/// <summary>Items rejected during synchronization despite an overall successful request.</summary>
	[JsonProperty("errors")]
	public PackageSynchronizationError[] Errors { get; set; }
}

internal sealed record PackageSynchronizationError {
	/// <summary>Identity of the rejected descriptor.</summary>
	[JsonProperty("workspaceItem")]
	public PackageSynchronizationItem WorkspaceItem { get; init; }

	/// <summary>Platform diagnostic for the rejected item.</summary>
	[JsonProperty("errorInfo")]
	public ErrorInfo ErrorInfo { get; init; }
}

internal sealed record PackageSynchronizationItem {
	/// <summary>Name of the rejected descriptor.</summary>
	[JsonProperty("name")]
	public string Name { get; init; }
}
