namespace Clio.WebApplication
{

	#region Class: DownloadInfo

	public class DownloadInfo
	{

		#region Constructors: Public

		public DownloadInfo(string url, string archiveName, string destinationPath, string requestData = null) {
			Url = url;
			ArchiveName = archiveName;
			DestinationPath = destinationPath;
			RequestData = requestData;
		}

		#endregion

		#region Properties: Public

		public string Url { get; }
		public string ArchiveName { get; }
		public string DestinationPath { get; }
		public string RequestData { get; }

		#endregion

	}

	#endregion

}