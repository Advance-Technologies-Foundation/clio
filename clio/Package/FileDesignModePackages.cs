using System.Threading;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Package;

public interface IFileDesignModePackages
{
    void LoadPackagesToFileSystem();

    void LoadPackagesToDb();
}

public class FileDesignModeFileDesignModePackages : IFileDesignModePackages
{
    private const int RetryRequestCount = 3;

    private const int DelayBetweenRetryAttemptsSec = 1;
    private readonly IApplicationClient _applicationClient;
    private readonly string _getIsFileDesignModeUrl;
    private readonly IJsonConverter _jsonConverter;
    private readonly string _loadPackagesToDbUrl;
    private readonly string _loadPackagesToFileSystemUrl;
    private readonly ILogger _logger;

    public FileDesignModeFileDesignModePackages(IApplicationClient applicationClient, IJsonConverter jsonConverter,
        ILogger logger, IServiceUrlBuilder serviceUrlBuilder)
    {
        applicationClient.CheckArgumentNull(nameof(applicationClient));
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        logger.CheckArgumentNull(nameof(logger));
        serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
        _applicationClient = applicationClient;
        _jsonConverter = jsonConverter;
        _logger = logger;
        _loadPackagesToFileSystemUrl = serviceUrlBuilder
            .Build("/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem");
        _loadPackagesToDbUrl = serviceUrlBuilder
            .Build("/ServiceModel/AppInstallerService.svc/LoadPackagesToDB");
        _getIsFileDesignModeUrl = serviceUrlBuilder
            .Build("/ServiceModel/WorkspaceExplorerService.svc/GetIsFileDesignMode");
    }

    private bool IsFileDesignModeUrl
    {
        get
        {
            string responseFormServer
                = _applicationClient.ExecutePostRequest(_getIsFileDesignModeUrl, string.Empty, Timeout.Infinite,
                    RetryRequestCount, DelayBetweenRetryAttemptsSec);
            BoolResponse response = _jsonConverter.DeserializeObject<BoolResponse>(responseFormServer);
            if (response.Success)
            {
                return response.Value;
            }

            _ = response.ErrorInfo;
            _logger.WriteLine($"Get file design mode ended with error: {GetErrorDetails(response.ErrorInfo)}");
            return false;
        }
    }

    public void LoadPackagesToFileSystem() => LoadPackagesToStorage(_loadPackagesToFileSystemUrl, "file system");

    public void LoadPackagesToDb() => LoadPackagesToStorage(_loadPackagesToDbUrl, "database");

    private static string GetErrorDetails(ErrorInfo errorInfo) =>
        $"{errorInfo.Message} (error code: {errorInfo.ErrorCode})";

    private void PrintErrorOperationMessage(string storageName, string errorMessage) =>
        _logger.WriteLine($"Load packages to {storageName} on a web application ended with error: {errorMessage}");

    private void LoadPackagesToStorage(string endpoint, string storageName)
    {
        if (!IsFileDesignModeUrl)
        {
            PrintErrorOperationMessage(storageName, "disabled file design mode");
            return;
        }

        _logger.WriteLine($"Start load packages to {storageName} on a web application");
        string responseFormServer = _applicationClient.ExecutePostRequest(endpoint, string.Empty, Timeout.Infinite,
            RetryRequestCount, DelayBetweenRetryAttemptsSec);
        BaseResponse response = _jsonConverter.DeserializeObject<BaseResponse>(responseFormServer);
        if (response.Success)
        {
            _logger.WriteLine($"Load packages to {storageName} on a web application completed");
            return;
        }

        _ = response.ErrorInfo;
        PrintErrorOperationMessage(storageName, GetErrorDetails(response.ErrorInfo));
    }
}
