using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;

namespace Clio.Command;

public interface IInstalledApplication
{
    InstalledAppInfo GetInstalledAppInfo(string appCode);
}

public class InstalledApplication(IDataProvider provider) : IInstalledApplication
{
    private readonly IDataProvider _provider = provider;

    public InstalledAppInfo GetInstalledAppInfo(string appCode) =>
        AppDataContextFactory.GetAppDataContext(_provider)
            .Models<SysInstalledApp>()
            .Where(m => m.Code == appCode)
            .ToList()
            .Select(m =>
                new InstalledAppInfo(_provider)
                {
                    Id = m.Id,
                    Code = m.Code,
                    Name = m.Name,
                    Description = m.Description,
                    Version = m.Version
                }).FirstOrDefault();
}

public class InstalledAppInfo(IDataProvider provider)
{
    private readonly IDataProvider _provider = provider;

    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Code { get; set; }

    public string Description { get; set; }

    public string Version { get; set; }

    public IEnumerable<string> GetPackages()
    {
        List<Guid> sysPackageIds =
        [
            .. AppDataContextFactory.GetAppDataContext(_provider)
                .Models<SysPackageInInstalledApp>()
                .Where(m => m.SysInstalledAppId == Id)
                .Select(m => m.SysPackageId)
        ];
        foreach (Guid sysPackageId in sysPackageIds)
        {
            SysPackage? package = AppDataContextFactory.GetAppDataContext(_provider)
                .GetModel<SysPackage>(sysPackageId);
            yield return package.Name;
        }
    }
}
