namespace Clio.Common
{
	public interface IApplicationClientFactory
	{
		IApplicationClient CreateClient(EnvironmentSettings environment);

		IApplicationClient CreateEnvironmentClient(EnvironmentSettings environment);

		/// <summary>Creates an owned environment-relative forms-auth client with strict TLS validation.</summary>
		/// <param name="environment">Target environment.</param>
		/// <returns>An owned client that the caller must dispose.</returns>
		IOwnedApplicationClient CreateFormsEnvironmentClient(EnvironmentSettings environment) =>
			throw new System.NotSupportedException("The configured factory does not support dedicated forms clients.");

		/// <summary>Creates an owned environment-relative bearer client with strict TLS validation.</summary>
		/// <param name="environment">Target environment.</param>
		/// <param name="accessToken">Bearer token.</param>
		/// <returns>An owned client that the caller must dispose.</returns>
		IOwnedApplicationClient CreateBearerEnvironmentClient(EnvironmentSettings environment,
			string accessToken) => throw new System.NotSupportedException(
				"The configured factory does not support dedicated bearer clients.");
	}

	/// <summary>Ownership-aware accessors for clients produced by the application client factory.</summary>
	public static class ApplicationClientFactoryExtensions {
		/// <summary>Creates a caller-owned client using the configured authentication mode.</summary>
		public static IOwnedApplicationClient CreateOwnedClient(this IApplicationClientFactory factory,
			EnvironmentSettings environment) => RequireOwned(factory.CreateClient(environment));

		/// <summary>Creates a caller-owned environment-relative client.</summary>
		public static IOwnedApplicationClient CreateOwnedEnvironmentClient(this IApplicationClientFactory factory,
			EnvironmentSettings environment) => RequireOwned(factory.CreateEnvironmentClient(environment));

		private static IOwnedApplicationClient RequireOwned(IApplicationClient client) =>
			client as IOwnedApplicationClient ?? new ApplicationClientLease(client);
	}
}
