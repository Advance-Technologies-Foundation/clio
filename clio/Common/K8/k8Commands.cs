﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using k8s;
using k8s.Models;

namespace Clio.Common.K8;

public interface Ik8Commands
{

    #region Methods: Public

    void CopyBackupFileToPod(k8Commands.PodType podType, string src, string destFileName);

    string DeleteBackupImage(k8Commands.PodType podType, string fileName);

    k8Commands.ConnectionStringParams GetMssqlConnectionString();

    k8Commands.ConnectionStringParams GetPostgresConnectionString();

    string RestorePgDatabase(string backupFileName, string dbName);

    #endregion

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

        private const string MssqlAppName = "clio-mssql";
        private const string MssqlContainerName = "clio-mssql";
        private const string MssqlFolderInVolumeMountName = "data";
        private const string MssqlInternalServiceName = "mssql-service-internal";
        private const string MssqlPasswordKey = "MSSQL_SA_PASSWORD";
        private const string MssqlPodLabel = "clio-mssql";
        private const string MssqlSecretName = "clio-mssql-secret";
        private const string MssqlUserNameKey = "";
        private const string MssqlVolumeMountName = "mssql-data";
        private const string PostgresAppName = "clio-postgres";
        private const string PostgresContainerName = "clio-postgres";
        private const string PostgresInternalServiceName = "postgres-service-internal";
        private const string PostgresPasswordKey = "POSTGRES_PASSWORD";
        private const string PostgresPodLabel = "clio-postgres";
        private const string PostgresSecretName = "clio-postgres-secret";
        private const string PostgresUserNameKey = "POSTGRES_USER";
        private const string PostgresVolumeMountName = "postgres-backup-images";

        #endregion

        #region Constants: Internal

        internal const string RedisAppName = "clio-redis";
        internal const string RedisInternalServiceName = "redis-service-internal";
        internal const string RedisLoadBalancerServiceName = "redis-service-lb";

        #endregion

        #region Fields: Internal

        internal readonly string ContainerName;
        internal readonly string PodLabel;
        internal readonly string VolumeMountName;
        internal readonly string FolderInVolumeMountName;
        internal readonly string AppName;
        internal readonly string UsernameKey;
        internal readonly string PasswordKey;
        internal readonly string SecretName;
        internal readonly string InternalServiceName;

        #endregion

        #region Constructors: Public

        public ActivePod(PodType podType)
        {
            (ContainerName, PodLabel, VolumeMountName, FolderInVolumeMountName, AppName, UsernameKey, PasswordKey,
             SecretName, InternalServiceName) = podType switch
                                                {
                                                    PodType.Postgres => (
                                                        PostgresContainerName, PostgresPodLabel,
                                                        PostgresVolumeMountName, string.Empty,
                                                        PostgresAppName, PostgresUserNameKey, PostgresPasswordKey,
                                                        PostgresSecretName, PostgresInternalServiceName),
                                                    PodType.Mssql => (
                                                        MssqlContainerName, MssqlPodLabel, MssqlVolumeMountName,
                                                        MssqlFolderInVolumeMountName,
                                                        MssqlAppName, "", MssqlPasswordKey, MssqlSecretName,
                                                        MssqlInternalServiceName),
                                                    var _ => throw new InvalidOperationException(
                                                        $"Unsupported PodType: {podType}")
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
        (currentPodType, pod, destFileName) =>
        {
            string mountpath = pod.Spec.Containers
                                  .FirstOrDefault(c => c.Name == currentPodType.ContainerName)?
                                  .VolumeMounts.FirstOrDefault(vm => vm.Name == currentPodType.VolumeMountName)
                                  ?.MountPath;
            return string.IsNullOrWhiteSpace(currentPodType.FolderInVolumeMountName) switch
                   {
                       true => $"{mountpath}/{destFileName}",
                       var _ => $"{mountpath}/{currentPodType.FolderInVolumeMountName}/{destFileName}"
                   };
        };

    private readonly IKubernetes _client;

    #endregion

    #region Constructors: Public

    public k8Commands(IKubernetes client)
    {
        _client = client;
    }

    #endregion

    #region Methods: Private

    private async Task<string> ExecInPod(ActivePod currentPod, V1Pod pod, IEnumerable<string> commandToExecute)
    {
        try
        {
            WebSocket webSocket =
                await _client.WebSocketNamespacedPodExecAsync(pod.Metadata.Name, K8NNameSpace, commandToExecute,
                    currentPod.ContainerName).ConfigureAwait(false);

            using StreamDemuxer demux = new(webSocket);
            demux.Start();
            await using Stream stream = demux.GetStream(1, 1);
            using MemoryStream ms = new();
            await stream.CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);

            StreamReader sr = new(ms);
            return await sr.ReadToEndAsync();
        }
        catch (Exception e)
        {
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

    private V1Pod GetPodByLabel(string appName)
    {
        V1Pod pod = _client.CoreV1
                           .ListNamespacedPod(K8NNameSpace, null, null, null, $"app={appName}")
                           .Items.FirstOrDefault();
        return pod;
    }

    private V1ServicePort GetServicePort(string appName, string serviceName)
    {
        V1Service service = _client.CoreV1.ListNamespacedService(K8NNameSpace, labelSelector: $"app={appName}")
                                   .Items.FirstOrDefault(s => s.Metadata.Name == serviceName);
        return service?.Spec.Ports.FirstOrDefault();
    }

    #endregion

    #region Methods: Public

    public void CopyBackupFileToPod(PodType podType, string src, string destFileName)
    {
        ActivePod currentPod = new(podType);
        V1Pod pod = GetPodByLabel(currentPod.PodLabel);
        string fullDestFilePath = GetBackupFullDestPath(currentPod, pod, destFileName);
        Cp cp = new(_client);
        cp.Copy(pod, K8NNameSpace, currentPod.ContainerName, src, $"{fullDestFilePath}").GetAwaiter().GetResult();
    }

    public string DeleteBackupImage(PodType podType, string fileName)
    {
        ActivePod currentPod = new(podType);
        V1Pod pod = GetPodByLabel(currentPod.PodLabel);
        string fullBackupFileName = GetBackupFullDestPath(currentPod, pod, fileName);
        string[] command = new[]
        {
            "rm", fullBackupFileName
        };
        return ExecInPod(currentPod, pod, command).GetAwaiter().GetResult();
    }

    public ConnectionStringParams GetMssqlConnectionString()
    {
        ActivePod currentPod = new(PodType.Mssql);
        V1StatefulSet statefulSet = _client.AppsV1.ListNamespacedStatefulSet(K8NNameSpace)
                                           .Items.FirstOrDefault(s => s.Metadata.Name == currentPod.AppName);
        string serviceName = statefulSet.Spec.ServiceName;

        V1ServicePort dbPort = GetServicePort(currentPod.AppName, serviceName);
        V1ServicePort dbPortInternal = GetServicePort(currentPod.AppName, currentPod.InternalServiceName);
        V1ServicePort redisPort = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisLoadBalancerServiceName);
        V1ServicePort redisPortInternal = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisInternalServiceName);

        V1Secret secrets = _client.CoreV1.ListNamespacedSecret(K8NNameSpace)
                                  .Items.FirstOrDefault(s => s.Metadata.Name == currentPod.SecretName);
        byte[] password = secrets?.Data[currentPod.PasswordKey];
        string passwordStr = Encoding.UTF8.GetString(password ?? Array.Empty<byte>());

        return new ConnectionStringParams(dbPort.Port, dbPortInternal.Port, redisPort.Port, redisPortInternal.Port,
            "sa", passwordStr);
    }

    public ConnectionStringParams GetPostgresConnectionString()
    {
        ActivePod currentPod = new(PodType.Postgres);
        V1StatefulSet statefulSet = _client.AppsV1.ListNamespacedStatefulSet(K8NNameSpace)
                                           .Items.FirstOrDefault(s => s.Metadata.Name == currentPod.AppName);
        string serviceName = statefulSet.Spec.ServiceName;

        V1ServicePort pgPort = GetServicePort(currentPod.AppName, serviceName);
        V1ServicePort dbPortInternal = GetServicePort(currentPod.AppName, currentPod.InternalServiceName);
        V1ServicePort redisPort = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisLoadBalancerServiceName);
        V1ServicePort redisPortInternal = GetServicePort(ActivePod.RedisAppName, ActivePod.RedisInternalServiceName);

        V1Secret secrets = _client.CoreV1.ListNamespacedSecret(K8NNameSpace)
                                  .Items.FirstOrDefault(s => s.Metadata.Name == currentPod.SecretName);

        byte[] password = secrets?.Data[currentPod.PasswordKey];
        byte[] username = secrets?.Data[currentPod.UsernameKey];
        string passwordStr = Encoding.UTF8.GetString(password ?? Array.Empty<byte>());
        string usernameStr = Encoding.UTF8.GetString(username ?? Array.Empty<byte>());
        return new ConnectionStringParams(pgPort.Port, dbPortInternal.Port, redisPort.Port, redisPortInternal.Port,
            usernameStr, passwordStr);
    }

    public string RestorePgDatabase(string backupFileName, string dbName)
    {
        ActivePod currentPod = new(PodType.Postgres);
        V1Pod pod = GetPodByLabel(currentPod.PodLabel);
        string[] command = new[]
        {
            "pg_restore", $"/usr/local/backup-images/{backupFileName}", $"--dbname={dbName}", "--verbose", "--no-owner",
            "--no-privileges", "--jobs=4", "--username=postgres"
        };
        string result = ExecInPod(currentPod, pod, command).GetAwaiter().GetResult();
        return result;
    }

    #endregion

    public record ConnectionStringParams(int DbPort, int DbInternalPort, int RedisPort, int RedisInternalPort,
        string DbUsername, string DbPassword);

}
