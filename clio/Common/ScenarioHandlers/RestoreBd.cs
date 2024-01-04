using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OneOf;

namespace Clio.Common.ScenarioHandlers;

public class RestoreBdRequest : BaseHandlerRequest
{ }

public class RestoreBdResponse : BaseHandlerResponse
{ }

internal class RestoreBdRequestHandler : IRequestHandler<RestoreBdRequest, OneOf<BaseHandlerResponse, HandlerError>>
{

	#region Properties: Public

	public Dictionary<string, string> Arguments { get; set; }

	#endregion

	#region Methods: Private

	private void ConnectToSQl(string connectionString){
		using SqlConnection connection = new SqlConnection(connectionString);
		connection.Open();
		RestoreDbFromFile(connection);
		CreateDbUser(connection);
		GrantPermissionToUser(connection);
	}

	private void CopyBackUpFile(){
		string backupFolderPath = Arguments["backupFolderPath"];
		string backUpSrc = Arguments["backUpSrc"];

		string dbBackUpFileFolderPath = Path.Join(backUpSrc, "db");
		string[] files = Directory.GetFiles(dbBackUpFileFolderPath);
		FileInfo src = new(files.FirstOrDefault());
		string backupFileName = Path.Join(backupFolderPath, src.Name);
		src.CopyTo(backupFileName, true);
	}

	private void CreateDbUser(SqlConnection connection){
		string dbUserName = Arguments["dbUserName"];
		string dbPassword = Arguments["dbPassword"];
		string query = @$"
                if not Exists (select loginname from master.dbo.syslogins where name = '{dbUserName}')
                Begin
	                declare @SqlStatement as nvarchar(max) = 'USE [master]'
	                EXEC sp_executesql @SqlStatement
	                select @SqlStatement = 'CREATE LOGIN [{dbUserName}] WITH PASSWORD=N'+'''{dbPassword}'''+', DEFAULT_DATABASE=[master], CHECK_EXPIRATION=OFF, CHECK_POLICY=OFF'
	                EXEC sp_executesql @SqlStatement
                End
            ";

		SqlCommand sqlCommand = new(query, connection);
		sqlCommand.ExecuteNonQuery();
	}

	private void GrantPermissionToUser(SqlConnection connection){
		string dbName = Arguments["dbName"];
		string dbUserName = Arguments["dbUserName"];
		string query = @$"USE [{dbName}]
                CREATE USER [{dbUserName}] FOR LOGIN [{dbUserName}];
                ALTER ROLE [db_owner] ADD MEMBER [{dbUserName}];
                ";
		SqlCommand sqlCommand = new(query, connection);
		sqlCommand.ExecuteNonQuery();
	}

	private void RestoreDbFromFile(SqlConnection connection){
		string backupFolderPath = Arguments["backupFolderPath"];
		string dataFolderPath = Arguments["dataFolderPath"];
		string backUpSrc = Arguments["backUpSrc"];
		string dbName = Arguments["dbName"];

		string dbBackUpFileFolderPath = Path.Join(backUpSrc, "db");
		string[] files = Directory.GetFiles(dbBackUpFileFolderPath);
		FileInfo ffi = new(files.FirstOrDefault());
		string backupFileName = Path.Join(backupFolderPath, ffi.Name);

		string mdf = Path.Join(dataFolderPath, dbName + ".mdf");
		string ldf = Path.Join(dataFolderPath, dbName + ".ldf");

		string query = @$"
                USE [master] RESTORE DATABASE [{dbName}] 
                FROM  DISK = N'{backupFileName}' WITH  FILE = 1, 
                MOVE N'TSOnline_Data' TO N'{mdf}', 
                MOVE N'TSOnline_Log' TO N'{ldf}', 
                NOUNLOAD,  STATS = 5
            ";
		
		SqlCommand sqlCommand = new(query, connection);
		sqlCommand.ExecuteNonQuery();
	}

	#endregion

	#region Methods: Public

	public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(RestoreBdRequest request,
		CancellationToken cancellationToken){
		Arguments = request.Arguments;
		string adminConectionString = request.Arguments["adminConectionString"];
		CopyBackUpFile();
		ConnectToSQl(adminConectionString);

		return new RestoreBdResponse {
			Status = BaseHandlerResponse.CompletionStatus.Success
		};
	}

	#endregion

}