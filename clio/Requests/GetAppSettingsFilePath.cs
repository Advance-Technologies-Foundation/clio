using Clio.UserEnvironment;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class GetAppSettingsFilePath : IExtenalLink
	{
		public string Content
		{
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
	/// <example>
	/// </example>
	public class GetAppSettingsFilePathHandler : BaseExternalLinkHandler, IRequestHandler<GetAppSettingsFilePath>
	{
		private readonly ISettingsRepository _settingsRepository;

		public GetAppSettingsFilePathHandler(ISettingsRepository settingsRepository)
		{
			_settingsRepository = settingsRepository;
		}

		public Task<Unit> Handle(GetAppSettingsFilePath request, CancellationToken cancellationToken)
		{

			if (!IsLinkValid(request.Content))
				return Unit.Task;

#if (DEBUG)
			PrintArguments();
#endif

			System.Console.WriteLine(_settingsRepository.AppSettingsFilePath);
			return Unit.Task;
		}
	}
}
