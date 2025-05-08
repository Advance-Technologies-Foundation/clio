using System;
using System.Diagnostics;
using System.IO;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

[Verb("register", HelpText = "Register clio commands in context menu ")]
public class RegisterOptions
{
    [Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
                                                     " machine location. Use 'u' for set user location and 'm' to set machine location.")]
    public string Target { get; set; }

    [Option('p', "Path", HelpText = "Path where clio is stored.")]
    public string Path { get; set; }
}

[Verb("unregister", HelpText = "Unregister clio commands in context menu")]
internal class UnregisterOptions
{
    [Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
                                                     " machine location. Use 'u' for set user location and 'm' to set machine location.")]
    public string Target { get; set; }

    [Option('p', "Path", HelpText = "Path where clio is stored.")]
    public string Path { get; set; }
}

public class RegisterCommand : Command<RegisterOptions>
{
    public override int Execute(RegisterOptions options)
    {
        try
        {
            if (OperationSystem.Current.IsWindows)
            {
                if (!OperationSystem.Current.HasAdminRights())
                {
                    Console.WriteLine("Clio register command need admin rights.");
                    return 1;
                }

                string folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appDataClioFolderPath = Path.Combine(folder, "clio");
                Directory.CreateDirectory(appDataClioFolderPath);
                CreatioEnvironment environment = new();
                string clioIconPath = Path.Combine(environment.GetAssemblyFolderPath(), "img");
                DirectoryInfo imgFolder = new(clioIconPath);
                FileInfo[] allImgFiles = imgFolder.GetFiles();
                foreach (FileInfo imgFile in allImgFiles)
                {
                    string destImgFilePath = Path.Combine(appDataClioFolderPath, imgFile.Name);
                    imgFile.CopyTo(destImgFilePath, true);
                }

                string unreg_file_name = Path.Combine(environment.GetAssemblyFolderPath(), "reg",
                    "unreg_clio_context_menu_win.reg");
                string reg_file_name =
                    Path.Combine(environment.GetAssemblyFolderPath(), "reg", "clio_context_menu_win.reg");
                Process? unregProcess =
                    Process.Start(
                        new ProcessStartInfo("cmd", $"/c reg import  {unreg_file_name}") { CreateNoWindow = true });
                unregProcess.WaitForExit();
                Process.Start(new ProcessStartInfo("cmd", $"/c reg import  {reg_file_name}") { CreateNoWindow = true });
                Console.WriteLine("Clio context menu successfully registered.");
                return 0;
            }

            Console.WriteLine("Clio register command is only supported on: 'windows'.");
            return 1;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
    }
}

internal class UnregisterCommand : Command<UnregisterOptions>
{
    public override int Execute(UnregisterOptions options)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd", "/c reg delete HKEY_CLASSES_ROOT\\Folder\\shell\\clio /f"));
            Process.Start(new ProcessStartInfo("cmd", "/c reg delete HKEY_CLASSES_ROOT\\*\\shell\\clio /f"));
            Console.WriteLine("Clio context menu successfully unregistered");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
    }
}
