using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;

namespace Clio.Utilities;

public interface IPowerShellFactory : IDisposable
{
    string ComputerName { get; }

    PowerShell GetInstance();

    void Initialize(string userName, string password, string computerName);
}

public class PowerShellFactory : IPowerShellFactory
{
    private bool _disposed;
    private string _password;
    private string _userName;

    private Runspace Runspace { get; set; }

    private SecureString SecureString { get; set; }

    private WSManConnectionInfo ConnectionInfo { get; set; }

    public string ComputerName { get; private set; }

    public void Initialize(string userName, string password, string computerName)
    {
        _userName = userName ?? string.Empty;
        _password = password ?? string.Empty;
        ComputerName = computerName ?? "localhost";
        CreateConnectionInfo();
        CreateRunspace();
    }

    public PowerShell GetInstance()
    {
        if (Runspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
            CreateRunspace();
        }

        PowerShell? ps = PowerShell.Create();
        ps.Runspace = Runspace;
        return ps;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void CreateConnectionInfo()
    {
        AuthenticationMechanism _authenticationMechanism = ComputerName == "localhost"
            ? AuthenticationMechanism.Default
            : AuthenticationMechanism.Kerberos;
        if (string.IsNullOrEmpty(_userName) && string.IsNullOrEmpty(_password))
        {
            ConnectionInfo = new WSManConnectionInfo
            {
                ComputerName = ComputerName, AuthenticationMechanism = AuthenticationMechanism.Default
            };
        }
        else
        {
            SecureString = new SecureString();
            foreach (char c in _password)
            {
                SecureString.AppendChar(c);
            }

            PSCredential creds = new(_userName, SecureString);
            ConnectionInfo = new WSManConnectionInfo
            {
                Credential = creds, ComputerName = ComputerName, AuthenticationMechanism = _authenticationMechanism
            };
        }
    }

    private void CreateRunspace()
    {
        Runspace = RunspaceFactory.CreateRunspace(ConnectionInfo);
        Runspace.Open();
        while (Runspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            SecureString.Dispose();
            SecureString = null;
            Runspace.Close();
            Runspace.Dispose();
            Runspace = null;
        }

        _disposed = true;
    }
}
