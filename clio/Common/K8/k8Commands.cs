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

public class k8Commands
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
		
		private const string PostgresContainerName = "clio-postgres";
		private const string PostgresPodLabel = "clio-postgres";
		private const string PostgresVolumeMountName = "postgres-backup-images";
		private const string PostgresAppName = "clio-postgres";

		#endregion

		#region Fields: Internal

		internal readonly string ContainerName;
		internal readonly string PodLabel;
		internal readonly string VolumeMountName;
		internal readonly string FolderInVolumeMountName;
		internal readonly string AppName;

		#endregion

		#region Constructors: Public

		public ActivePod(PodType podType) {
			(ContainerName, PodLabel, VolumeMountName, FolderInVolumeMountName, AppName) = podType switch {
				PodType.Postgres => (PostgresContainerName, PostgresPodLabel, PostgresVolumeMountName, string.Empty, PostgresAppName),
				PodType.Mssql => (MssqlContainerName, MssqlPodLabel, MssqlVolumeMountName,
					MssqlFolderInVolumeMountName, MssqlAppName),
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

	
	private static readonly Func<string, bool> CreatePostgresDbWithClient = (dbName)=> {
		
		try {
			// also valid
			NpgsqlConnectionStringBuilder csb = new () {
				Host = "localhost",
				Port = 5432,
				Username = "postgres",
				Password = "root",
				Database = "postgres"
			};
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(csb);
			
			//using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PgConnectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
			return false;
		}
	};
	
	private readonly IKubernetes _client;

	#endregion

	#region Constructors: Public

	public k8Commands(IKubernetes client) {
		_client = client;

		//TODO - Can we do better ?
		if (GetNamespaces() == null) {
			throw new Exception($"{K8NNameSpace} namespace not found");
		}
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

	private V1Namespace GetNamespaces() =>
		_client.CoreV1.ListNamespace().Items.FirstOrDefault(ns => ns.Metadata.Name == K8NNameSpace);

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
		
		var r = CreatePostgresDbWithClient(dbName);
		var result =  ExecInPod(currentPod, pod, command).GetAwaiter().GetResult();
		return result;
	}
	
	public ConnectionStringParams GetPostgresConnectionString() {
		
		ActivePod currentPod = new ActivePod(PodType.Postgres);
		V1Pod pod = GetPodByLabel(currentPod.PodLabel);
		
		V1StatefulSet ss = _client.AppsV1.ListNamespacedStatefulSet(K8NNameSpace)
			.Items.FirstOrDefault(s=> s.Metadata.Name == currentPod.AppName);
		string serviceName = ss.Spec.ServiceName;
		
		
		var pgPort = GetLoadBalancePort(currentPod.AppName, serviceName);
		var redisPort = GetLoadBalancePort("clio-redis", "redis-service-lb");
		
		
		string secretName = pod.Spec.Containers.FirstOrDefault(c=> c.Name == currentPod.ContainerName)
			?.Env.
			FirstOrDefault(e=> e.Name == "POSTGRES_USER")
			?.ValueFrom.SecretKeyRef.Name;
		
		V1Secret secrets = _client.CoreV1.ListNamespacedSecret(K8NNameSpace)
			.Items.FirstOrDefault(s=>s.Metadata.Name ==secretName);
		
		byte[] password = secrets?.Data["POSTGRES_PASSWORD"];
		byte[] username = secrets?.Data["POSTGRES_USER"];
		string passwordStr = Encoding.UTF8.GetString(password ?? Array.Empty<byte>());
		string usernameStr = Encoding.UTF8.GetString(username ?? Array.Empty<byte>());
		
		
		return new ConnectionStringParams(pgPort.Port, redisPort.Port, usernameStr, passwordStr);
		
	}
	
	public ConnectionStringParams GetMssqlConnectionString() {
		
		ActivePod currentPod = new ActivePod(PodType.Mssql);
		V1Pod pod = GetPodByLabel(currentPod.PodLabel);
		
		V1StatefulSet ss = _client.AppsV1.ListNamespacedStatefulSet(K8NNameSpace)
			.Items.FirstOrDefault(s=> s.Metadata.Name == currentPod.AppName);
		string serviceName = ss.Spec.ServiceName;
		
		var dbPort = GetLoadBalancePort(currentPod.AppName, serviceName);
		var redisPort = GetLoadBalancePort("clio-redis", "redis-service-lb");
		
		string secretName = pod.Spec.Containers.FirstOrDefault(c=> c.Name == currentPod.ContainerName)
			?.Env.
			FirstOrDefault(e=> e.Name == "MSSQL_SA_PASSWORD")
			?.ValueFrom.SecretKeyRef.Name;
		
		V1Secret secrets = _client.CoreV1.ListNamespacedSecret(K8NNameSpace)
			.Items.FirstOrDefault(s=>s.Metadata.Name ==secretName);
		
		byte[] password = secrets?.Data["MSSQL_SA_PASSWORD"];
		string passwordStr = Encoding.UTF8.GetString(password ?? Array.Empty<byte>());
		
		return new ConnectionStringParams(dbPort.Port, redisPort.Port, "sa", passwordStr);
		
	}
	private V1ServicePort GetLoadBalancePort(string appName, string serviceName ) {
		V1Service service = _client.CoreV1.ListNamespacedService(K8NNameSpace, labelSelector:$"app={appName}")
			.Items.FirstOrDefault(s=> s.Metadata.Name == serviceName);
		V1ServicePort port = service.Spec.Ports.FirstOrDefault();
		return port;
	}
	
	
	#endregion
	public record ConnectionStringParams(int DbPort, int RedisPort, string DbUsername, string DbPassword);
	
}


