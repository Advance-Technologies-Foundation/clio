using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.UserEnvironment;
using MediatR;

namespace Clio.Requests;

public class GetAppSettingsFilePath : IExternalLink
{
    public string Content { get; set; }
}

/// <summary>
///     Finds path to appSetting.json.
/// </summary>
/// <remarks>
///     Handles extenral link requests.
///     <example>
///         <code>clio --externalLink clio://GetAppSettingsFilePath</code>
///     </example>
/// </remarks>
internal class GetAppSettingsFilePathHandler(ISettingsRepository settingsRepository, ILogger logger)
    : BaseExternalLinkHandler, IRequestHandler<GetAppSettingsFilePath>
{
    private readonly ILogger _logger = logger;
    private readonly ISettingsRepository _settingsRepository = settingsRepository;

    public Task Handle(GetAppSettingsFilePath request, CancellationToken cancellationToken)
    {
        _logger.WriteInfo(_settingsRepository.AppSettingsFilePath);
        return Unit.Task;
    }
}
