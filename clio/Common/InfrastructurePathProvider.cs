using System.IO;

namespace Clio.Common
{
	/// <summary>
	/// Provides infrastructure path resolution for Kubernetes infrastructure files.
	/// </summary>
	public interface IInfrastructurePathProvider
	{
		/// <summary>
		/// Gets the infrastructure path, using the provided custom path if specified,
		/// otherwise returns the default path from application settings.
		/// </summary>
		/// <param name="customPath">Optional custom path to infrastructure files</param>
		/// <returns>The resolved infrastructure path</returns>
		string GetInfrastructurePath(string customPath = null);
	}

	/// <summary>
	/// Default implementation of infrastructure path provider.
	/// </summary>
	public class InfrastructurePathProvider : IInfrastructurePathProvider
	{
		/// <summary>
		/// Gets the infrastructure path, using the provided custom path if specified,
		/// otherwise returns the default path from application settings.
		/// </summary>
		/// <param name="customPath">Optional custom path to infrastructure files</param>
		/// <returns>The resolved infrastructure path</returns>
		public string GetInfrastructurePath(string customPath = null)
		{
			if (!string.IsNullOrWhiteSpace(customPath))
			{
				return customPath;
			}
			
			return Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");
		}
	}
}
