using System.Collections.Generic;
using System.IO;

namespace Clio.UserEnvironment;

internal class EnvironmentResult : IResult
{
    private readonly List<string> _messages = [];

    public void ShowMessagesTo(TextWriter writer) => _messages.ForEach(writer.WriteLine);

    public void AppendMessage(string message) => _messages.Add(message);
}
