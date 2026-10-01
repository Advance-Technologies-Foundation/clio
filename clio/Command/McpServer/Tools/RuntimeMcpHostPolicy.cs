using System;
using System.Linq;
using Clio.Common.McpWorker;

namespace Clio.Command.McpServer.Tools;

/// <summary>Explicit process-level permission for tools that manage the developer machine.</summary>
public interface IRuntimeMcpHostPolicy {
	/// <summary>Whether this server was explicitly started as a developer runtime host.</summary>
	bool Enabled { get; }
}

/// <inheritdoc/>
public sealed class RuntimeMcpHostPolicy : IRuntimeMcpHostPolicy {
	/// <inheritdoc/>
	public bool Enabled => Environment.GetCommandLineArgs().Contains("--runtime-host", StringComparer.Ordinal)
		&& !McpWorkerEnvironment.IsWorkerProcess
		&& !string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
		&& string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
}
