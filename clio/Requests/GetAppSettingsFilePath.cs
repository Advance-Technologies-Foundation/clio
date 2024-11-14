using Clio.Common;
using Clio.UserEnvironment;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class GetAppSettingsFilePath : IExternalLink
	{
		public string Content {
			get; set;
		}
	}

	/// <summary>
	/// Finds path to appSetting.json
	/// </summary>
	/// <remarks>
	/// Handles extenral link requests
	/// <example><code>clio --externalLink clio://GetAppSettingsFilePath</code></example>
	/// </remarks>
	internal class GetAppSettingsFilePathHandler : BaseExternalLinkHandler, IRequestHandler<GetAppSettingsFilePath>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly ILogger _logger;

		public GetAppSettingsFilePathHandler(ISettingsRepository settingsRepository, ILogger logger)
		{
			_settingsRepository = settingsRepository;
			_logger = logger;
		}

		public Task Handle(GetAppSettingsFilePath request, CancellationToken cancellationToken)
		{
			_logger.WriteInfo(_settingsRepository.AppSettingsFilePath);
			return Unit.Task;
		}
	}
}
