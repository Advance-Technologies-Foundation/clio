namespace Clio.Common.DeploymentStrategies;

/// <summary>
/// Enumeration of supported deployment platforms
/// </summary>
public enum DeploymentPlatform
{
	/// <summary>
	/// Windows with IIS (Internet Information Services)
	/// </summary>
	Windows,

	/// <summary>
	/// macOS with dotnet runner
	/// </summary>
	MacOS,

	/// <summary>
	/// Linux with dotnet runner
	/// </summary>
	Linux
}
