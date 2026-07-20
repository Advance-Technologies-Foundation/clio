namespace Clio.Common;

/// <summary>
/// Common-layer carrier for the request timeout and retry settings used when calling native Creatio
/// services. A Common analog of the request-related part of <c>RemoteCommandOptions</c> so Common-layer
/// service clients do not depend on the Command layer. Defaults mirror <c>RemoteCommandOptions</c>.
/// </summary>
public sealed record CreatioRequestOptions
{
	/// <summary>Request timeout in milliseconds.</summary>
	public int TimeOut { get; init; } = 100_000;

	/// <summary>Maximum number of attempts.</summary>
	public int MaxAttempts { get; init; } = 3;

	/// <summary>Delay between attempts in seconds.</summary>
	public int RetryDelay { get; init; } = 1;
}
