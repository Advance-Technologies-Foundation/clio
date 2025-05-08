using System;
using System.Diagnostics;
using System.Text;

namespace Clio.Common;

public class ProcessExecutor : IProcessExecutor
{
    public string Execute(string program, string command, bool waitForExit, string workingDirectory = null,
        bool showOutput = false)
    {
        program.CheckArgumentNullOrWhiteSpace(nameof(program));
        command.CheckArgumentNullOrWhiteSpace(nameof(command));
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = program,
            Arguments = command,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        StringBuilder sb = new();
        process.EnableRaisingEvents = waitForExit;

        if (showOutput)
        {
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    Console.WriteLine(e.Data);
                    sb.Append(e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    ConsoleColor color = Console.ForegroundColor;
                    Console.ForegroundColor = e.Data.ToLower().Contains("error")
                        ? ConsoleColor.DarkRed
                        : ConsoleColor.DarkYellow;
                    Console.WriteLine(e.Data);
                    Console.ForegroundColor = color;
                    sb.Append(e.Data);
                }
            };
        }

        process.Start();
        if (showOutput)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        if (waitForExit)
        {
            process.WaitForExit();
        }

        if (!showOutput)
        {
            return process.StandardOutput.ReadToEnd();
        }

        return sb.ToString();
    }
}
