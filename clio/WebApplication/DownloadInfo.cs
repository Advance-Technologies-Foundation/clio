namespace Clio.WebApplication;

public class DownloadInfo(string url, string archiveName, string destinationPath, string requestData = null)
{
    public string Url { get; } = url;

    public string ArchiveName { get; } = archiveName;

    public string DestinationPath { get; } = destinationPath;

    public string RequestData { get; } = requestData;
}
