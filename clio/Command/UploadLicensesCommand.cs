namespace Clio.Command.PackageCommand;

using System;
using System.IO;
using System.Text.Json;
using Common;

public class UploadLicensesCommand : RemoteCommand<UploadLicensesOptions>
{
    public UploadLicensesCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    {
    }

    protected override string ServicePath => @"/ServiceModel/LicenseService.svc/UploadLicenses";

    protected override string GetRequestData(UploadLicensesOptions options)
    {
        string fileBody = File.ReadAllText(options.FilePath);
        fileBody = fileBody.Replace("\"", "\\\"");
        return "{\"licData\":\"" + fileBody + "\"}";
    }

    protected override void ProceedResponse(string response, UploadLicensesOptions options)
    {
        JsonDocument json = JsonDocument.Parse(response);
        if (json.RootElement.TryGetProperty("success", out JsonElement successProperty) &&
            successProperty.GetBoolean() == false)
        {
            if (json.RootElement.TryGetProperty("errorInfo", out JsonElement errorInfo))
            {
                string? errorMessage = errorInfo.TryGetProperty("message", out JsonElement messageProperty)
                    ? messageProperty.GetString()
                    : "Unknown error message";
                string? errorCode = errorInfo.TryGetProperty("errorCode", out JsonElement codeProperty)
                    ? codeProperty.GetString()
                    : "UNKNOWN_CODE";
                throw new LicenseInstallationException(
                    $"License not installed. ErrorCode: {errorCode}, Message: {errorMessage}");
            }

            throw new LicenseInstallationException("License not installed: Unknown error details");
        }

        if (response.ToLower().Contains("authentication failed"))
        {
            throw new LicenseInstallationException("License not installed: Authentication failed.");
        }

        base.ProceedResponse(response, options);
    }
}

public class LicenseInstallationException : Exception
{
    public LicenseInstallationException(string message) : base(message)
    {
    }
}
