using System;
using Newtonsoft.Json;
using Terrasoft.Common.Json;

namespace Cliogate.Dto;

internal class WebSocket(string commandName, string message)
{

    #region Constructors: Public

    #endregion

    #region Properties: Public

    [JsonProperty("commandName")] public string CommandName { get; private set; } = commandName;

    [JsonProperty("message")] public string Message { get; private set; } = message;

    #endregion

    #region Methods: Public

    public override string ToString() => Json.FormatJsonString(Json.Serialize(this), Formatting.Indented);

    #endregion
}
