using Clio.UserEnvironment;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class GetAppSettingsFilePath : IExtenalLink
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

		public GetAppSettingsFilePathHandler(ISettingsRepository settingsRepository)
		{
			_settingsRepository = settingsRepository;
		}

		public Task Handle(GetAppSettingsFilePath request, CancellationToken cancellationToken)
		{
			Console.WriteLine(_settingsRepository.AppSettingsFilePath);
			return Unit.Task;
		}
	}
}
