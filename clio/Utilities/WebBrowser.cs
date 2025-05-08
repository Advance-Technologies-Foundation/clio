using System;
using System.Diagnostics;
using System.Net;

namespace Clio.Utilities;

internal class WebBrowser
{
    public static bool Enabled => OSPlatformChecker.GetIsWindowsEnvironment();

    public static bool CheckUrl(string url)
    {
        UriBuilder uriBuilder = new(url);
        WebRequest request = HttpWebRequest.Create(uriBuilder.Uri);
        HttpWebResponse response = (HttpWebResponse)request.GetResponse();
        return response.StatusCode == HttpStatusCode.OK && response.ResponseUri == request.RequestUri;
    }

    public static void OpenUrl(string url)
    {
        if (OSPlatformChecker.GetIsWindowsEnvironment())
        {
            Console.WriteLine($"Open {url}...");
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
        else
        {
            throw new NotFiniteNumberException("Command not supported for current platform...");
        }
    }
}
