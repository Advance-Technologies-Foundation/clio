using System.Collections.Generic;
using System.IO;

namespace Clio.UserEnvironment;

internal class EnvironmentResult : IResult
{

    #region Fields: Private

    private readonly List<string> _messages = new();

    #endregion

    #region Methods: Public

    public void AppendMessage(string message)
    {
        _messages.Add(message);
    }

    public void ShowMessagesTo(TextWriter writer)
    {
        _messages.ForEach(writer.WriteLine);
    }

    #endregion

}
