using System;
using Clio.Common;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Reads the environment's <c>SchemaNamePrefix</c> for the web service tools and fails when it cannot. The shared
/// <see cref="ISchemaNamePrefixResolver"/> degrades to no prefix on a timeout or a failed read, which suits local
/// package generation but would switch every prefix check off here and save codes Studio then refuses.
/// </summary>
public interface IWebServicePrefixReader {
	/// <summary>Reads the prefix; an environment with an empty setting yields <see cref="string.Empty"/>.</summary>
	/// <returns>The prefix, trimmed.</returns>
	/// <exception cref="InvalidOperationException">The setting could not be read.</exception>
	string Read();
}

/// <inheritdoc cref="IWebServicePrefixReader"/>
public sealed class WebServicePrefixReader(
	EnvironmentSettings environmentSettings,
	Func<EnvironmentSettings, ISysSettingsManager> sysSettingsManagerFactory) : IWebServicePrefixReader {

	/// <inheritdoc />
	public string Read() {
		try {
			return SysSettingCodes.ReadSchemaNamePrefix(sysSettingsManagerFactory(environmentSettings));
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			// The server's own text can carry a login page or a token, so it is not repeated.
			throw new InvalidOperationException("The SchemaNamePrefix system setting could not be read, so the "
				+ "prefix rule Studio applies cannot be checked; nothing was saved. Retry the call.");
		}
	}
}
