using System.Text.Json;
using Clio.Query;

namespace Clio.Common;

public class CreatioLogStreamer(CallServiceCommand callServiceCommand, IServiceUrlBuilder serviceUrlBuilder)
    : ILogStreamer
{
    private readonly CallServiceCommand _callServiceCommand = callServiceCommand;

    public void WriteLine(string message) =>
        _callServiceCommand.Execute(new CallServiceCommandOptions
        {
            HttpMethodName = "POST",
            RequestBody = CreateBody(message),
            ServicePath = ServiceUrlBuilder.KnownRoutes[ServiceUrlBuilder.KnownRoute.SendEventToUI],
            IsSilent = true
        });

    private string CreateBody(string message)
    {
        var body = new { Sender = "Clio", Content = message, CommandName = "Show logs" };
        return JsonSerializer.Serialize(body);
    }
}
