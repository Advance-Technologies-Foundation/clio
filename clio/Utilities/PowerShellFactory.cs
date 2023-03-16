using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;

namespace Clio.Utilities
{
	public interface IPowerShellFactory
	{
		string ComputerName { get; }
		void Dispose();
		PowerShell GetInstance();
		void Initialize(string userName, string password, string computerName);
	}

	public class PowerShellFactory : IDisposable, IPowerShellFactory
	{

		private bool _disposed = false;
		private string _userName;
		private string _password;
		private string _computerName;

		public string ComputerName => _computerName;

		private Runspace Runspace { get; set; }

		private SecureString SecureString { get; set; }

		private WSManConnectionInfo ConnectionInfo { get; set; }

		public PowerShellFactory()
		{
		}

		public void Initialize(string userName, string password, string computerName)
		{
			_userName = userName ?? string.Empty;
			_password = password ?? string.Empty;
			_computerName = computerName ?? "localhost";
			CreateConnectionInfo();
			CreateRunspace();
		}

		private void CreateConnectionInfo()
		{
			AuthenticationMechanism _authenticationMechanism = (_computerName == "localhost") ? AuthenticationMechanism.Default : AuthenticationMechanism.Kerberos;
			if (string.IsNullOrEmpty(_userName) && string.IsNullOrEmpty(_password))
			{
				ConnectionInfo = new WSManConnectionInfo
				{
					ComputerName = _computerName,
					AuthenticationMechanism = AuthenticationMechanism.Default
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
					Credential = creds,
					ComputerName = _computerName,
					AuthenticationMechanism = _authenticationMechanism
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

		public PowerShell GetInstance()
		{

			if (Runspace.RunspaceStateInfo.State != RunspaceState.Opened)
			{
				CreateRunspace();
			}

			var ps = PowerShell.Create();
			ps.Runspace = Runspace;
			return ps;
		}

		#region IDIsposable
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
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

		#endregion

	}
}
