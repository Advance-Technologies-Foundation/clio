using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Npgsql;

namespace Clio.Common.K8;

public interface Ik8Commands
{

	void CopyBackupFileToPod(k8Commands.PodType podType, string src, string destFileName);

	string DeleteBackupImage(k8Commands.PodType podType, string fileName);

	string RestorePgDatabase(string backupFileName, string dbName);

	k8Commands.ConnectionStringParams GetPostgresConnectionString();

	k8Commands.ConnectionStringParams GetMssqlConnectionString();

	bool NamespaceExists(string namespaceName);

	bool DeleteNamespace(string namespaceName);

	IList<string> GetReleasedPersistentVolumes(string namespacePrefix);

	bool DeletePersistentVolume(string pvName);

	bool CleanupReleasedVolumes(string namespacePrefix);

	DeleteNamespaceResult DeleteNamespaceWithCleanup(string namespaceName, string namespacePrefix, int maxWaitAttempts = 15, int delaySeconds = 2);

	CleanupNamespaceResult CleanupAndDeleteNamespace(string namespaceName, string namespacePrefix);

}

public class k8Commands : Ik8Commands
{

	#region Enum: Public

	public enum PodType
	{

		Mssql,
		Postgres

	}

	#endregion

	#region Struct: Private

	private readonly struct ActivePod
	{

		#region Constants: Private

		private const string MssqlContainerName = "clio-mssql";
		private const string MssqlFolderInVolumeMountName = "data";
		private const string MssqlPodLabel = "clio-mssql";
		private const string MssqlVolumeMountName = "mssql-data";
		private const string MssqlAppName = "clio-mssql";
		private const string MssqlUserNameKey = "";
		private const string MssqlPasswordKey = "MSSQL_SA_PASSWORD";
		private const string MssqlSecretName = "clio-mssql-secret";
		private const string MssqlInternalServiceName = "mssql-service-internal";
		
		private const string PostgresContainerName = "clio-postgres";
		private const string PostgresPodLabel = "clio-postgres";
		private const string PostgresVolumeMountName = "postgres-backup-images";
		private const string PostgresAppName = "clio-postgres";
		private const string PostgresUserNameKey = "POSTGRES_USER";
		private const string PostgresPasswordKey = "POSTGRES_PASSWORD";
		private const string PostgresSecretName = "clio-postgres-secret";
		private const string PostgresInternalServiceName = "postgres-service-internal";
		
		#endregion

		#region Fields: Internal

		internal readonly string ContainerName;
		internal readonly string PodLabel;
		internal readonly string VolumeMountName;
		internal readonly string FolderInVolumeMountName;
		internal readonly string AppName;
		internal readonly string UsernameKey;
		internal readonly string PasswordKey;
		internal const string RedisAppName = "clio-redis";
		internal const string RedisLoadBalancerServiceName = "redis-service-lb";
		internal const string RedisInternalServiceName = "redis-service-internal";
		internal readonly string SecretName;
		internal readonly string InternalServiceName;
		
		#endregion

		#region Constructors: Public

		public ActivePod(PodType podType) {
			(ContainerName, PodLabel, VolumeMountName, FolderInVolumeMountName, AppName, UsernameKey, PasswordKey, SecretName, InternalServiceName) = podType switch {
				PodType.Postgres => (PostgresContainerName, PostgresPodLabel, PostgresVolumeMountName, string.Empty, 
					PostgresAppName, PostgresUserNameKey, PostgresPasswordKey, PostgresSecretName, PostgresInternalServiceName),
				PodType.Mssql => (MssqlContainerName, MssqlPodLabel, MssqlVolumeMountName, MssqlFolderInVolumeMountName,
					MssqlAppName, "",MssqlPasswordKey, MssqlSecretName, MssqlInternalServiceName),
				_ => throw new InvalidOperationException($"Unsupported PodType: {podType}")
			};
		}

		#endregion

	}

	#endregion

	#region Constants: Private

	private const string K8NNameSpace = "clio-infrastructure";

	#endregion

	#region Fields: Private

	private static readonly Func<ActivePod, V1Pod, string, string> GetBackupFullDestPath =
		(currentPodType, pod, destFileName) => {
			string mountpath = pod.Spec.Containers
				.FirstOrDefault(c => c.Name == currentPodType.ContainerName)?
				.VolumeMounts.FirstOrDefault(vm => vm.Name == currentPodType.VolumeMountName)?.MountPath;
			return string.IsNullOrWhiteSpace(currentPodType.FolderInVolumeMountName) switch {
				true => $"{mountpath}/{destFileName}",
				_ => $"{mountpath}/{currentPodType.FolderInVolumeMountName}/{destFileName}"
			};
		};

	
	
	private readonly IKubernetes _client;

	#endregion

	#region Constructors: Public

	public k8Commands(IKubernetes client) {
		_client = client;
	}

	#endregion

	#region Methods: Private

	private async Task<string> ExecInPod(ActivePod currentPod, V1Pod pod, IEnumerable<string> commandToExecute) {
		try {
			WebSocket webSocket =
				await _client.WebSocketNamespacedPodExecAsync(pod.Metadata.Name, K8NNameSpace, commandToExecute,
					currentPod.ContainerName).ConfigureAwait(false);

			using StreamDemuxer demux = new(webSocket);
			demux.Start();
			await using Stream stream = demux.GetStream(1, 1);
			using MemoryStream ms = new MemoryStream();
			await stream.CopyToAsync(ms);
			ms.Seek(0,SeekOrigin.Begin);
			
			StreamReader sr = new StreamReader(ms);
			return await sr.ReadToEndAsync();
		}
		catch (Exception e){
			Console.WriteLine(e);
		}
		return "";
	}

	// private V1Namespace GetNamespaces(){
	// 	
	// 	//TODO - Can we do better ?
	// 	var namespaces = _client.CoreV1.ListNamespace().Items.FirstOrDefault(ns => ns.Metadata.Name == K8NNameSpace);
	 //        if (namespaces == null) {
	 //        	throw new Exception($"{K8NNameSpace} namespace not found");
	 //        }
	// 	return namespaces;
	// }

	private V1Pod GetPodByLabel(string appName) {
		V1Pod pod = _client.CoreV1
			.ListNamespacedPod(K8NNameSpace, null, null, null, $"app={appName}")
			.Items.FirstOrDefault();
		return pod;
	}

	#endregion

	#region Methods: Public

	public void CopyBackupFileToPod(PodType podType, string src, string destFileName) {
		ActivePod currentPod = new(podType);
		V1Pod pod = GetPodByLabel(currentPod.PodLabel);
		string fullDestFilePath = GetBackupFullDestPath(currentPod, pod, destFileName);
		Cp cp = new(_client);
		cp.Copy(pod, K8NNameSpace, currentPod.ContainerName, src, $"{fullDestFilePath}").GetAwaiter().GetResult();
	}

	public string DeleteBackupImage(PodType podType, string fileName) {
		ActivePod currentPod = new(podType);
		V1Pod pod = GetPodByLabel(currentPod.PodLabel);
		string fullBackupFileName = GetBackupFullDestPath(currentPod, pod, fileName);
		string[] command = new[] {"rm", fullBackupFileName};
		return ExecInPod(currentPod, pod, command).GetAwaiter().GetResult();
	}

	public string RestorePgDatabase(string backupFileName, string dbName) {
		ActivePod currentPod = new(PodType.Postgres);
		V1Pod pod = GetPodByLabel(currentPod.PodLabel);
		string[] command = new[] {
			"pg_restore", 
			$"/usr/local/backup-images/{backupFileName}", 
			$"--dbname={dbName}", 
			"--verbose",
			"--no-owner", 
			"--no-privileges", 
			"--jobs=4", 
			"--username=postgres"
		};
		string result =  ExecInPod(currentPod, pod, command).GetAwaiter().GetResult();
		return result;
	}
	
	public ConnectionStringParams GetPostgresConnectionString() {
		ActivePod currentPod = new (PodType.Postgres);
		V1StatefulSet statefulSet = _client.AppsV1.ListNamespacedStatefulSet(K8NNameSpace)
			.Items.FirstOrDefault(s=> s.Metadata.Name == currentPod.AppName);
		string serviceName = statefulSet.Spec.ServiceName;
		
		V1ServicePort pgPort = GetServicePort(currentPod.AppName, serviceName);
		V1ServicePort dbPortInternal = GetServicePort(currentPod.AppName, currentPod.InternalServiceName);
		V1ServicePort redisPort = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisLoadBalancerServiceName);
		V1ServicePort redisPortInternal = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisInternalServiceName);
		
		
		V1Secret secrets = _client.CoreV1.ListNamespacedSecret(K8NNameSpace)
			.Items.FirstOrDefault(s=>s.Metadata.Name ==currentPod.SecretName);
		
		byte[] password = secrets?.Data[currentPod.PasswordKey];
		byte[] username = secrets?.Data[currentPod.UsernameKey];
		string passwordStr = Encoding.UTF8.GetString(password ?? Array.Empty<byte>());
		string usernameStr = Encoding.UTF8.GetString(username ?? Array.Empty<byte>());
		
		int dbPortValue = pgPort.Port >0? pgPort.Port: pgPort.NodePort ?? 5432;
		int dbPortInternalValue = dbPortInternal.Port>0? dbPortInternal.Port : dbPortInternal.NodePort ?? 0;
		int redisPortValue = redisPort.Port>0? redisPort.Port : redisPort.NodePort ?? 6379;
		int redisPortInternalValue = redisPortInternal.Port>0? redisPortInternal.Port : redisPortInternal.NodePort ?? 0;

		return new ConnectionStringParams(dbPortValue, dbPortInternalValue, redisPortValue, redisPortInternalValue, usernameStr, passwordStr);
	}
	
	public ConnectionStringParams GetMssqlConnectionString() {
		ActivePod currentPod = new ActivePod(PodType.Mssql);
		V1StatefulSet statefulSet = _client.AppsV1.ListNamespacedStatefulSet(K8NNameSpace)
			.Items.FirstOrDefault(s=> s.Metadata.Name == currentPod.AppName);
		string serviceName = statefulSet.Spec.ServiceName;
		
		V1ServicePort dbPort = GetServicePort(currentPod.AppName, serviceName);
		V1ServicePort dbPortInternal = GetServicePort(currentPod.AppName, currentPod.InternalServiceName);
		V1ServicePort redisPort = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisLoadBalancerServiceName);
		V1ServicePort redisPortInternal = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisInternalServiceName);
		
		V1Secret secrets = _client.CoreV1.ListNamespacedSecret(K8NNameSpace)
			.Items.FirstOrDefault(s=>s.Metadata.Name ==currentPod.SecretName);
		byte[] password = secrets?.Data[currentPod.PasswordKey];
		string passwordStr = Encoding.UTF8.GetString(password ?? Array.Empty<byte>());

		// var dbPortValue = dbPort.NodePort ?? dbPort.Port;
		// var dbPortInternalValue = dbPortInternal.NodePort ?? dbPortInternal.Port;
		// var redisPortValue = redisPort.NodePort ?? redisPort.Port;
		// var redisPortInternalValue = redisPortInternal.NodePort ?? redisPortInternal.Port;
		
		int dbPortValue = dbPort.Port >0? dbPort.Port: dbPort.NodePort ?? 1433;
		int dbPortInternalValue = dbPortInternal.Port>0? dbPortInternal.Port : dbPortInternal.NodePort ?? 0;
		int redisPortValue = redisPort.Port>0? redisPort.Port : redisPort.NodePort ?? 6379;
		int redisPortInternalValue = redisPortInternal.Port>0? redisPortInternal.Port : redisPortInternal.NodePort ?? 0;


		return new ConnectionStringParams(dbPortValue, dbPortInternalValue, redisPortValue, redisPortInternalValue, "sa", passwordStr);
	}
	private V1ServicePort GetServicePort(string appName, string serviceName ) {
		V1Service service = _client.CoreV1.ListNamespacedService(K8NNameSpace, labelSelector:$"app={appName}")
			.Items.FirstOrDefault(s=> s.Metadata.Name == serviceName);
		return service?.Spec.Ports.FirstOrDefault();
	}

	public bool NamespaceExists(string namespaceName)
	{
		try
		{
			V1Namespace ns = _client.CoreV1.ReadNamespace(namespaceName);
			return ns != null;
		}
		catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return false;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool DeleteNamespace(string namespaceName)
	{
		try
		{
			V1DeleteOptions deleteOptions = new V1DeleteOptions
			{
				PropagationPolicy = "Foreground", // Wait for dependent resources to be deleted
				GracePeriodSeconds = 30
			};
			_client.CoreV1.DeleteNamespace(namespaceName, deleteOptions);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public IList<string> GetReleasedPersistentVolumes(string namespacePrefix)
	{
		try
		{
			var releasedPvs = _client.CoreV1.ListPersistentVolume()
				.Items
				.Where(pv => pv.Status.Phase == "Released" && 
							 pv.Spec.ClaimRef != null &&
							 pv.Spec.ClaimRef.NamespaceProperty != null &&
							 pv.Spec.ClaimRef.NamespaceProperty.StartsWith(namespacePrefix))
				.Select(pv => pv.Metadata.Name)
				.ToList();

			return releasedPvs;
		}
		catch (Exception)
		{
			return new List<string>();
		}
	}

	public bool DeletePersistentVolume(string pvName)
	{
		try
		{
			_client.CoreV1.DeletePersistentVolume(pvName);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool CleanupReleasedVolumes(string namespacePrefix)
	{
		try
		{
			var releasedPvs = GetReleasedPersistentVolumes(namespacePrefix);
			
			foreach (var pvName in releasedPvs)
			{
				DeletePersistentVolume(pvName);
			}

			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public DeleteNamespaceResult DeleteNamespaceWithCleanup(string namespaceName, string namespacePrefix, int maxWaitAttempts = 15, int delaySeconds = 2)
	{
		try
		{
			// Step 1: Clean up released PersistentVolumes
			var releasedPvs = GetReleasedPersistentVolumes(namespacePrefix);
			var deletedPvs = new List<string>();
			
			foreach (var pvName in releasedPvs)
			{
				if (DeletePersistentVolume(pvName))
				{
					deletedPvs.Add(pvName);
				}
			}

			// Step 2: Delete namespace
			if (!DeleteNamespace(namespaceName))
			{
				return new DeleteNamespaceResult
				{
					Success = false,
					Message = $"Failed to delete namespace '{namespaceName}'",
					DeletedPersistentVolumes = deletedPvs
				};
			}

			// Step 3: Wait for namespace deletion
			int attemptCount = 0;
			while (attemptCount < maxWaitAttempts && NamespaceExists(namespaceName))
			{
				System.Threading.Thread.Sleep(delaySeconds * 1000);
				attemptCount++;
			}

			bool namespaceFullyDeleted = !NamespaceExists(namespaceName);
			
			return new DeleteNamespaceResult
			{
				Success = namespaceFullyDeleted,
				Message = namespaceFullyDeleted 
					? $"Namespace '{namespaceName}' deleted successfully"
					: $"Namespace deletion in progress but may not be fully complete yet",
				DeletedPersistentVolumes = deletedPvs,
				WaitAttempts = attemptCount,
				NamespaceFullyDeleted = namespaceFullyDeleted
			};
		}
		catch (Exception ex)
		{
			return new DeleteNamespaceResult
			{
				Success = false,
				Message = $"Error deleting namespace: {ex.Message}",
				DeletedPersistentVolumes = new List<string>()
			};
		}
	}

	public CleanupNamespaceResult CleanupAndDeleteNamespace(string namespaceName, string namespacePrefix)
	{
		try
		{
			// Step 1: Clean up released PersistentVolumes
			var releasedPvs = GetReleasedPersistentVolumes(namespacePrefix);
			var deletedPvs = new List<string>();
			
			foreach (var pvName in releasedPvs)
			{
				if (DeletePersistentVolume(pvName))
				{
					deletedPvs.Add(pvName);
				}
			}

			// Step 2: Delete namespace
			if (!DeleteNamespace(namespaceName))
			{
				return new CleanupNamespaceResult
				{
					Success = false,
					Message = $"Failed to delete namespace '{namespaceName}'",
					DeletedPersistentVolumes = deletedPvs
				};
			}

			// Step 3: Wait for namespace deletion (up to 30 seconds)
			int maxWaitAttempts = 15;
			int delaySeconds = 2;
			int attemptCount = 0;
			
			while (attemptCount < maxWaitAttempts && NamespaceExists(namespaceName))
			{
				System.Threading.Thread.Sleep(delaySeconds * 1000);
				attemptCount++;
			}

			bool namespaceFullyDeleted = !NamespaceExists(namespaceName);
			
			return new CleanupNamespaceResult
			{
				Success = namespaceFullyDeleted,
				Message = namespaceFullyDeleted 
					? $"Namespace '{namespaceName}' deleted successfully"
					: $"Namespace deletion in progress but may not be fully complete yet",
				DeletedPersistentVolumes = deletedPvs,
				NamespaceFullyDeleted = namespaceFullyDeleted
			};
		}
		catch (Exception ex)
		{
			return new CleanupNamespaceResult
			{
				Success = false,
				Message = $"Error deleting namespace: {ex.Message}",
				DeletedPersistentVolumes = new List<string>()
			};
		}
	}

	#endregion
	public record ConnectionStringParams(int DbPort, int DbInternalPort,int RedisPort, int RedisInternalPort,string DbUsername, string DbPassword);
	
}

public class DeleteNamespaceResult
{
	public bool Success { get; set; }
	public string Message { get; set; }
	public IList<string> DeletedPersistentVolumes { get; set; }
	public int WaitAttempts { get; set; }
	public bool NamespaceFullyDeleted { get; set; }
}

public class CleanupNamespaceResult
{
	public bool Success { get; set; }
	public string Message { get; set; }
	public IList<string> DeletedPersistentVolumes { get; set; }
	public bool NamespaceFullyDeleted { get; set; }
}

