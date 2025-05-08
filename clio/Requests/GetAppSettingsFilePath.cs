using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.UserEnvironment;
using MediatR;

namespace Clio.Requests;

public class GetAppSettingsFilePath : IExternalLink
{

    #region Properties: Public

    public string Content { get; set; }

    #endregion

}

/// <summary>
///     Finds path to appSetting.json
/// </summary>
/// <remarks>
///     Handles extenral link requests
///     <example>
///         <code>clio --externalLink clio://GetAppSettingsFilePath</code>
///     </example>
/// </remarks>
internal class GetAppSettingsFilePathHandler : BaseExternalLinkHandler, IRequestHandler<GetAppSettingsFilePath>
{

    #region Fields: Private

    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger _logger;

    #endregion

    #region Constructors: Public

    public GetAppSettingsFilePathHandler(ISettingsRepository settingsRepository, ILogger logger)
    {
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    #endregion

    #region Methods: Public

    public Task Handle(GetAppSettingsFilePath request, CancellationToken cancellationToken)
    {
        _logger.WriteInfo(_settingsRepository.AppSettingsFilePath);
        return Unit.Task;
    }

    #endregion

}
