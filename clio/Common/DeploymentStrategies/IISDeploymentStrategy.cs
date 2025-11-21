using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.ScenarioHandlers;
using MediatR;

namespace Clio.Common.DeploymentStrategies;

/// <summary>
/// Deployment strategy for Windows using Internet Information Services (IIS).
/// This strategy creates IIS application pools, websites, and bindings.
/// </summary>
public class IISDeploymentStrategy : IDeploymentStrategy
{
	private readonly IMediator _mediator;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the IISDeploymentStrategy class.
	/// </summary>
	public IISDeploymentStrategy(IMediator mediator, ILogger logger)
	{
		_mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>
	/// Gets the target platform for this strategy.
	/// </summary>
	public DeploymentPlatform TargetPlatform => DeploymentPlatform.Windows;

	/// <summary>
	/// Determines if IIS deployment is possible on current system.
	/// Checks if running on Windows.
	/// </summary>
	public bool CanDeploy()
	{
		// IIS is only available on Windows
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	}

	/// <summary>
	/// Deploys Creatio application via IIS.
	/// Creates application pool, website, and HTTP/HTTPS bindings.
	/// </summary>
	public async Task<int> Deploy(DirectoryInfo appDirectory, PfInstallerOptions options)
	{
		if (appDirectory == null)
			throw new ArgumentNullException(nameof(appDirectory));

		if (options == null)
			throw new ArgumentNullException(nameof(options));

		_logger.WriteInfo("[Create IIS Site] - Started");

		try
		{
			var frameworkType = InstallerHelper.DetectFramework(appDirectory);
			var request = new CreateIISSiteRequest
			{
				Arguments = new Dictionary<string, string>
				{
					{ "siteName", options.SiteName },
					{ "port", options.SitePort.ToString() },
					{ "sourceDirectory", appDirectory.FullName },
					{ "destinationDirectory", string.Empty }, // Will be filled from settings
					{ "isNetFramework", (frameworkType == InstallerHelper.FrameworkType.NetFramework).ToString() }
				}
			};

			var result = await _mediator.Send(request);

			if (result.Value is HandlerError error)
			{
				_logger.WriteError(error.ErrorDescription);
				return 1;
			}

			if (result.Value is CreateIISSiteResponse response)
			{
				if (response.Status == BaseHandlerResponse.CompletionStatus.Success)
				{
					_logger.WriteInfo(response.Description);
					return 0;
				}
				else
				{
					_logger.WriteError(response.Description);
					return 1;
				}
			}

			_logger.WriteError("Unknown error occurred during IIS deployment");
			return 1;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"IIS deployment failed: {ex.Message}");
			return 1;
		}
	}

	/// <summary>
	/// Gets the URL where the application will be accessible.
	/// </summary>
	public string GetApplicationUrl(PfInstallerOptions options)
	{
		if (options == null)
			throw new ArgumentNullException(nameof(options));

		string protocol = options.UseHttps ? "https" : "http";
		string fqdn = InstallerHelper.FetFQDN();
		int port = options.SitePort;

		// Don't include default ports in URL
		if ((protocol == "http" && port == 80) || (protocol == "https" && port == 443))
		{
			return $"{protocol}://{fqdn}";
		}

		return $"{protocol}://{fqdn}:{port}";
	}

	/// <summary>
	/// Gets a human-readable description of this deployment strategy.
	/// </summary>
	public string GetDescription() => "Windows IIS (Internet Information Services)";
}
