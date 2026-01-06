using System;
using System.IO;
using Clio.UserEnvironment;

namespace Clio.Common.Assertions
{
	/// <summary>
	/// Handles filesystem path validation assertions.
	/// </summary>
	public class FsPathAssertion
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly ILogger _logger;

		public FsPathAssertion(ISettingsRepository settingsRepository, ILogger logger)
		{
			_settingsRepository = settingsRepository;
			_logger = logger;
		}

		/// <summary>
		/// Validates that a filesystem path exists.
		/// Supports special setting keys like "iis-clio-root-path" or absolute paths.
		/// </summary>
		/// <param name="pathOrSettingKey">Path to validate or setting key (e.g., "iis-clio-root-path")</param>
		/// <returns>AssertionResult with resolved path</returns>
		public AssertionResult Execute(string pathOrSettingKey)
		{
			if (string.IsNullOrWhiteSpace(pathOrSettingKey))
			{
				return AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPath,
					"Path parameter is required but was not provided"
				);
			}

			// Resolve path: check if it's a setting key or a direct path
			string resolvedPath = ResolvePath(pathOrSettingKey);

			_logger.WriteInfo($"Validating path: {resolvedPath}");

			// Validate path exists
			if (!Directory.Exists(resolvedPath))
			{
				var result = AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPath,
					$"Path does not exist: {resolvedPath}"
				);
				result.Details["requestedPath"] = pathOrSettingKey;
				result.Details["resolvedPath"] = resolvedPath;
				return result;
			}

			// Success
			var successResult = AssertionResult.Success();
			successResult.Scope = AssertionScope.Fs;
			successResult.Resolved["path"] = resolvedPath;
			successResult.Details["requestedPath"] = pathOrSettingKey;
			
			return successResult;
		}

		/// <summary>
		/// Resolves a path string which may be either a setting key or an absolute path.
		/// </summary>
		private string ResolvePath(string pathOrSettingKey)
		{
			// Check if this is a known setting key
			if (pathOrSettingKey.Equals("iis-clio-root-path", StringComparison.OrdinalIgnoreCase))
			{
				return _settingsRepository.GetIISClioRootPath();
			}

			// Otherwise, treat it as a direct path
			return pathOrSettingKey;
		}
	}
}
