using System;
using System.Runtime.InteropServices;
using Clio.Command.CreatioInstallCommand;

namespace Clio.Common.DeploymentStrategies;

/// <summary>
/// Factory for selecting and creating appropriate deployment strategy
/// based on current OS platform and user options.
/// </summary>
public class DeploymentStrategyFactory
{
	private readonly IISDeploymentStrategy _iisStrategy;
	private readonly DotNetDeploymentStrategy _dotNetStrategy;

	/// <summary>
	/// Initializes a new instance of the DeploymentStrategyFactory class.
	/// </summary>
	/// <param name="iisStrategy">IIS deployment strategy implementation</param>
	/// <param name="dotNetStrategy">DotNet deployment strategy implementation</param>
	public DeploymentStrategyFactory(IISDeploymentStrategy iisStrategy, DotNetDeploymentStrategy dotNetStrategy)
	{
		_iisStrategy = iisStrategy ?? throw new ArgumentNullException(nameof(iisStrategy));
		_dotNetStrategy = dotNetStrategy ?? throw new ArgumentNullException(nameof(dotNetStrategy));
	}

	/// <summary>
	/// Detects the current operating system platform.
	/// </summary>
	/// <returns>Detected deployment platform</returns>
	/// <exception cref="PlatformNotSupportedException">Thrown when platform is not recognized</exception>
	public DeploymentPlatform DetectPlatform()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return DeploymentPlatform.Windows;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return DeploymentPlatform.MacOS;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return DeploymentPlatform.Linux;

		throw new PlatformNotSupportedException(
			$"Platform is not supported: {RuntimeInformation.OSDescription}"
		);
	}

	/// <summary>
	/// Selects appropriate deployment strategy based on OS and user options.
	/// </summary>
	/// <param name="deploymentMethod">User-specified deployment method: "auto", "iis", or "dotnet"</param>
	/// <param name="noIIS">Force disable IIS even on Windows</param>
	/// <returns>Selected deployment strategy</returns>
	/// <exception cref="ArgumentException">Thrown when invalid deployment method specified</exception>
	/// <exception cref="PlatformNotSupportedException">Thrown when platform/method combination not supported</exception>
	public IDeploymentStrategy SelectStrategy(string deploymentMethod = "auto", bool noIIS = false)
	{
		// If explicitly requested, use the specified strategy
		if (!string.IsNullOrEmpty(deploymentMethod) && deploymentMethod != "auto")
		{
			return SelectStrategyByName(deploymentMethod);
		}

		// If IIS explicitly disabled, use dotnet
		if (noIIS && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return _dotNetStrategy;
		}

		// Auto-detect based on current OS
		var currentPlatform = DetectPlatform();
		return currentPlatform switch
		{
			DeploymentPlatform.Windows => _iisStrategy,
			DeploymentPlatform.MacOS => _dotNetStrategy,
			DeploymentPlatform.Linux => _dotNetStrategy,
			_ => throw new PlatformNotSupportedException($"Unsupported platform: {currentPlatform}")
		};
	}

	/// <summary>
	/// Selects strategy by explicit name.
	/// </summary>
	/// <param name="methodName">Method name: "iis" or "dotnet"</param>
	/// <returns>Selected deployment strategy</returns>
	/// <exception cref="ArgumentException">Thrown when invalid method name specified</exception>
	private IDeploymentStrategy SelectStrategyByName(string methodName)
	{
		return methodName.ToLowerInvariant() switch
		{
			"iis" => _iisStrategy,
			"dotnet" => _dotNetStrategy,
			_ => throw new ArgumentException(
				$"Unknown deployment method: '{methodName}'. Supported values: auto, iis, dotnet",
				nameof(methodName)
			)
		};
	}
}
