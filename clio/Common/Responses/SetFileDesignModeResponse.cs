using Newtonsoft.Json;

namespace Clio.Common.Responses;

public class SetFileDesignModeResponse : BaseResponse
{
	[JsonProperty("WebConfigPath")]
	public string WebConfigPath { get; set; }

	[JsonProperty("PreviousFileDesignMode")]
	public string PreviousFileDesignMode { get; set; }

	[JsonProperty("NewFileDesignMode")]
	public string NewFileDesignMode { get; set; }

	[JsonProperty("PreviousUseStaticFileContent")]
	public string PreviousUseStaticFileContent { get; set; }

	[JsonProperty("NewUseStaticFileContent")]
	public string NewUseStaticFileContent { get; set; }
}
