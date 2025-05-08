using System.Text.Json;
using Clio.Query;

namespace Clio.Common;

public class CreatioLogStreamer : ILogStreamer
{

    #region Fields: Private

    private readonly CallServiceCommand _callServiceCommand;

    #endregion

    #region Constructors: Public

    public CreatioLogStreamer(CallServiceCommand callServiceCommand, IServiceUrlBuilder serviceUrlBuilder)
    {
        _callServiceCommand = callServiceCommand;
    }

    #endregion

    #region Methods: Private

    private string CreateBody(string message)
    {
        var body = new
        {
            Sender = "Clio", Content = message, CommandName = "Show logs"
        };
        return JsonSerializer.Serialize(body);
    }

    #endregion

    #region Methods: Public

    public void WriteLine(string message)
    {
        _callServiceCommand.Execute(new CallServiceCommandOptions
        {
            HttpMethodName = "POST",
            RequestBody = CreateBody(message),
            ServicePath = ServiceUrlBuilder.KnownRoutes[ServiceUrlBuilder.KnownRoute.SendEventToUI],
            IsSilent = true
        });
    }

    #endregion

}
