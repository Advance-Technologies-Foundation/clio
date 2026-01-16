We have a command called `uninstall-creatio`. 

Recently we introduced a feature where clio will look in the app settings and take a path to my local database server.
When we do this then the installation is going to happen not in the Kubernetes cluster but rather on the local database.

Uninstall command is unaware of local database concept, and it's trying to connect to a local kubernetes cluster which may or may not exist, or database may or may not exist.
I want the `uninstall-creatio` command to go inside file content of Creatio instance  find `ConnectionStrings.config` file take a look at the database connection 
string and use that to connect to the relevant server instead of trying to construct it from the Kubernetes cluster.

- Command should try to connect to the database specified in the `ConnectionStrings.config` and then drop the database.
- Relevant code lives in `UninstallByPath` method in `CreatioUninstaller` class.
Instead of using 

```csharp
k8Commands.ConnectionStringParams cn = info.DbType switch {
    "MsSql" => _k8Commands.GetMssqlConnectionString(),
    "PostgreSql" => _k8Commands.GetPostgresConnectionString(),
    var _ => throw new Exception("Unknown db type")
};
```
I want to read `ConnectionStrings.config` located in the path specified in `EnvironmentPath` key in clio appsettings file.


Here is example of `ConnectionStrings.config` file for PostgreSQL database. You should use `db` key for connection string.
```xml

<?xml version="1.0" encoding="utf-8"?>
<connectionStrings>
  <add name="db" connectionString="Server=127.0.0.1;Port=5432;Database=d_n8;User ID=postgres;password=root;Timeout=500; CommandTimeout=400;MaxPoolSize=1024;" />
  <add name="dbPostgreSql" connectionString="Server=127.0.0.1;Port=5432;Database=d_n8;User ID=postgres;password=root;Timeout=500; CommandTimeout=400;MaxPoolSize=1024;" />
  <add name="redis" connectionString="host=127.0.0.1;db=2;port=6379" />
  <add name="dbMssqlCore" connectionString="Data Source=tscore-ms-01\mssql2008; Initial Catalog=BPMonlineCore; Persist Security Info=True; MultipleActiveResultSets=True; Integrated Security=SSPI; Pooling = true; Max Pool Size = 100; Async = true" />
  <add name="dbMssqlUnitTest" connectionString="Data Source=TSAppHost-02; Initial Catalog=BPMonlineUnitTest; Persist Security Info=True; MultipleActiveResultSets=True; User ID=UnitTest; Password=UnitTest; Async = true" />
  <add name="tempDirectoryPath" connectionString="%TEMP%/%USER%/%APPLICATION%" />
  <add name="consumerInfoServiceUri" connectionString="http://sso.bpmonline.com:4566/ConsumerInfoService.svc" />
  <add name="consumerInfoServiceAccessInfoPageUri" connectionString="http://sso.bpmonline.com:4566/AccessInfoPage.aspx" />
  <add name="logstashConfigFolderPath" connectionString="%TEMP%\%APPLICATION%\LogstashConfig" />
  <add name="elasticsearchCredentials" connectionString="User=gs-es; Password=DEQpJMfKqUVTWg9wYVgi;" />
  <add name="influx" connectionString="url=http://10.0.7.161:30359; user=; password=; batchIntervalMs=5000" />
  <add name="clientPerformanceLoggerServiceUri" connectionString="http://tsbuild-k8s-m1:30001/" />
  <add name="messageBroker" connectionString="amqp://guest:guest@localhost/BPMonlineSolution" />
</connectionStrings>
```


here is example of `ConnectionStrings.config` file for MsSql database, You should use `db` key for connection string.
```xml
<?xml version="1.0" encoding="utf-8"?>
<connectionStrings>
  <add name="redis" connectionString="host=ts1-agent39;db=0;port=6379" />
  <add name="defPackagesWorkingCopyPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\TerrasoftPackages" />
  <add name="tempDirectoryPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\" />
  <add name="sourceControlAuthPath" connectionString="%TEMP%\%APPLICATION%\%APPPOOLIDENTITY%\%WORKSPACE%\Svn" />
  <add name="elasticsearchCredentials" connectionString="User=gs-es; Password=DEQpJMfKqUVTWg9wYVgi;" />
  <add name="influx" connectionString="url=http://10.0.7.161:30359; user=; password=; batchIntervalMs=5000" />
  <add name="messageBroker" connectionString="amqp://guest:guest@localhost/BPMonlineSolution" />
  <add name="db" connectionString="Data Source=ts1-agent39;Initial Catalog=StudioENU_13663235_0108;Integrated Security=SSPI;MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100; Encrypt=False; TrustServerCertificate=True;" />
</connectionStrings>
```
