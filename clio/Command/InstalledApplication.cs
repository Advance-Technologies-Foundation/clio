using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;

namespace Clio.Command;

public interface IInstalledApplication
{

    #region Methods: Public

    InstalledAppInfo GetInstalledAppInfo(string appCode);

    #endregion

}

public class InstalledApplication : IInstalledApplication
{

    #region Fields: Private

    private readonly IDataProvider _provider;

    #endregion

    #region Constructors: Public

    public InstalledApplication(IDataProvider provider)
    {
        _provider = provider;
    }

    #endregion

    #region Methods: Public

    public InstalledAppInfo GetInstalledAppInfo(string appCode)
    {
        return AppDataContextFactory.GetAppDataContext(_provider)
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

    #endregion

}

public class InstalledAppInfo
{

    #region Fields: Private

    private readonly IDataProvider _provider;

    #endregion

    #region Constructors: Public

    public InstalledAppInfo(IDataProvider provider)
    {
        _provider = provider;
    }

    #endregion

    #region Properties: Public

    public string Code { get; set; }

    public string Description { get; set; }

    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Version { get; set; }

    #endregion

    #region Methods: Public

    public IEnumerable<string> GetPackages()
    {
        List<Guid> sysPackageIds = AppDataContextFactory.GetAppDataContext(_provider)
                                                        .Models<SysPackageInInstalledApp>()
                                                        .Where(m => m.SysInstalledAppId == Id)
                                                        .Select(m => m.SysPackageId)
                                                        .ToList();
        foreach (Guid sysPackageId in sysPackageIds)
        {
            SysPackage package = AppDataContextFactory.GetAppDataContext(_provider)
                                                      .GetModel<SysPackage>(sysPackageId);
            yield return package.Name;
        }
    }

    #endregion

}
