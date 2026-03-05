using System;
using Clio.Common;

namespace Clio.Command.CreatioInstallCommand;

/// <summary>
/// Determines whether current machine is eligible for corporate-only deployment behaviors.
/// </summary>
public interface ICorporateEnvironmentDetector{
	#region Methods: Public

	/// <summary>
	/// Checks whether current machine is in corporate network context.
	/// </summary>
	/// <returns>
	/// <c>true</c> when current user belongs to <c>tscrm</c> domain or corporate host <c>tscrm.com</c> is reachable.
	/// </returns>
	bool IsCorporateEnvironment();

	#endregion
}

/// <summary>
/// Default implementation of <see cref="ICorporateEnvironmentDetector"/>.
/// </summary>
/// <param name="processExecutor">Process executor used for identity and connectivity checks.</param>
/// <param name="operationSystem">Operating system information provider.</param>
public class CorporateEnvironmentDetector(IProcessExecutor processExecutor, IOperationSystem operationSystem)
	: ICorporateEnvironmentDetector{
	#region Fields: Private

	private readonly IOperationSystem _operationSystem = operationSystem;
	private readonly IProcessExecutor _processExecutor = processExecutor;

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public bool IsCorporateEnvironment() {
		return IsTscrmDomainMember() || CanReachTscrmHost();
	}

	#endregion

	#region Methods: Private

	private bool CanReachTscrmHost() {
		string arguments = _operationSystem.IsWindows ? "-n 1 tscrm.com" : "-c 1 tscrm.com";
		ProcessExecutionOptions options = new("ping", arguments) {
			Timeout = TimeSpan.FromSeconds(5)
		};
		ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(options).GetAwaiter().GetResult();
		return result is {
			Started: true,
			TimedOut: false,
			Canceled: false,
			ExitCode: 0
		};
	}

	private bool IsTscrmDomainMember() {
		if (!_operationSystem.IsWindows) {
			return false;
		}

		ProcessExecutionOptions options = new("cmd", "/c whoami") {
			Timeout = TimeSpan.FromSeconds(5)
		};
		ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(options).GetAwaiter().GetResult();
		if (result is not {
			Started: true,
			TimedOut: false,
			Canceled: false,
			ExitCode: 0
		}) {
			return false;
		}

		string identity = (result.StandardOutput ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(identity)) {
			return false;
		}

		string[] tokens = identity.Split('\\', StringSplitOptions.RemoveEmptyEntries);
		return tokens.Length >= 2 && tokens[0].Equals("tscrm", StringComparison.OrdinalIgnoreCase);
	}

	#endregion
}
