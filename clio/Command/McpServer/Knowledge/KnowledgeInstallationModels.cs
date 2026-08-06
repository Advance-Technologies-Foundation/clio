namespace Clio.Command.McpServer.Knowledge;

internal enum KnowledgeInstallationStatus {
	Installed,
	Updated,
	AlreadyInstalled,
	Deleted,
	NotInstalled,
	ConfirmationRequired,
	Rejected,
	Failed
}

internal sealed record KnowledgeInstallationResult(
	KnowledgeInstallationStatus Status,
	string Message,
	string? PackageVersion = null,
	string? RootPath = null) {
	internal bool IsSuccess => Status is KnowledgeInstallationStatus.Installed
		or KnowledgeInstallationStatus.Updated
		or KnowledgeInstallationStatus.AlreadyInstalled
		or KnowledgeInstallationStatus.Deleted;
}
