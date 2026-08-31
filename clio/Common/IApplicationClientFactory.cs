namespace Clio.Common
{
	public interface IApplicationClientFactory
	{
		/// <summary>Creates a client using the environment's configured authentication mode.</summary>
		IApplicationClient CreateClient(EnvironmentSettings environment);

		/// <summary>Creates an environment-relative client using the configured authentication mode.</summary>
		IApplicationClient CreateEnvironmentClient(EnvironmentSettings environment);

		/// <summary>Creates an owned environment-relative forms-auth client with explicit TLS policy.</summary>
		/// <param name="environment">Target environment.</param>
		/// <param name="useUntrustedSsl">Whether invalid server certificates are accepted.</param>
		/// <returns>An owned client that the caller must dispose.</returns>
		IOwnedApplicationClient CreateFormsEnvironmentClient(EnvironmentSettings environment,
			bool useUntrustedSsl);

		/// <summary>Creates an owned environment-relative bearer client with explicit TLS policy.</summary>
		/// <param name="environment">Target environment.</param>
		/// <param name="accessToken">Bearer token.</param>
		/// <param name="useUntrustedSsl">Whether invalid server certificates are accepted.</param>
		/// <returns>An owned client that the caller must dispose.</returns>
		IOwnedApplicationClient CreateBearerEnvironmentClient(EnvironmentSettings environment,
			string accessToken, bool useUntrustedSsl);
	}
}
