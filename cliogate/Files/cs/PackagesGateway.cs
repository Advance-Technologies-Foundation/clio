using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using System.Threading;
using System.Threading.Tasks;
using ClioGate.Functions.SQL;
using Newtonsoft.Json;
using Terrasoft.Core.Configuration;
using Terrasoft.Core.ConfigurationBuild;
using Terrasoft.Core.DB;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Factories;
using Terrasoft.Core.Packages;
using Terrasoft.Web.Common;
#if NETSTANDARD2_0
using System.Globalization;
using Terrasoft.Web.Http.Abstractions;
#endif

namespace cliogate.Files.cs;

#region Class: PackagesGateway

[ServiceContract]
[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
public class PackagesGateway : BaseService
{
    #region Constructor: Public

    public PackagesGateway()
    {
        _packageInstallerServiceInternalType = new Lazy<Type>(() =>
        {
            Assembly coreServiceModelContractAssembly = FindAssembly("Terrasoft.Core.ServiceModelContract");
            return coreServiceModelContractAssembly.GetType(
                "Terrasoft.Core.ServiceModelContract.PackageInstaller.PackageInstallerServiceInternal");
        });
        _packageInstallerServiceInternal = new Lazy<object>(() =>
        {
            Assembly coreDIAssembly = FindAssembly("Terrasoft.Core.DI");
            Type coreApiContainerType = coreDIAssembly.GetType("Terrasoft.Core.DI.CoreApiContainer");
            MethodInfo resolveMethod = FindMethod(coreApiContainerType, "Resolve", true);
            Type interfacePackageInstallerServiceInternalType =
                PackageInstallerServiceInternalType.GetInterface(
                    "Terrasoft.Core.ServiceModelContract.PackageInstaller.IPackageInstallerServiceInternal");
            MethodInfo resolveMethodInfo = resolveMethod
                .MakeGenericMethod(interfacePackageInstallerServiceInternalType);
            return resolveMethodInfo.Invoke(null, new object[] { });
        });
        _methodInfo = new Lazy<MethodInfo>(() =>
            PackageInstallerServiceInternalType.GetMethod("LogInfo", _bindingFlagsAll));
        _coreAssembly = new Lazy<Assembly>(() => FindAssembly("Terrasoft.Core"));
        _getFullPathForUploadingFile = new Lazy<MethodInfo>(() =>
            PackageInstallerServiceInternalType.GetMethod("GetFullPathForUploadingFile", _bindingFlagsAll));
        _tempDirectoryPath = new Lazy<string>(() =>
            (string)GetProperty(PackageInstallerServiceInternalType,
                "TempDirectoryPath", PackageInstallerServiceInternal));
        _packageInstallOptionsType = new Lazy<Type>(() =>
            CoreAssembly.GetType("Terrasoft.Core.Packages.PackageInstallOptions"));
        _packageInstallOptions = new Lazy<object>(() =>
            Activator.CreateInstance(PackageInstallOptionsType));
    }

    #endregion

    #region Methods: Private

    private static readonly BindingFlags _bindingFlagsAll = BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Static | BindingFlags.Instance;

    private static Assembly FindAssembly(string assemblyName) =>
        AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => a.GetName(true).Name == assemblyName);

    private static MethodInfo FindMethod(Type type, string methodName, bool isGenericMethod = false) =>
        type
            .GetMethods(_bindingFlagsAll)
            .FirstOrDefault(m => m.Name == methodName &&
                                 m.IsGenericMethod == isGenericMethod);

    private static object GetProperty(Type type, string propertyName, object source)
    {
        PropertyInfo propertyInfo = type.GetProperty(propertyName, _bindingFlagsAll);
        return propertyInfo?.GetValue(source);
    }

    #endregion

    #region Properties: Private

    private readonly Lazy<Type> _packageInstallerServiceInternalType;
    private Type PackageInstallerServiceInternalType => _packageInstallerServiceInternalType.Value;

    private readonly Lazy<object> _packageInstallerServiceInternal;
    private object PackageInstallerServiceInternal => _packageInstallerServiceInternal.Value;

    private readonly Lazy<MethodInfo> _methodInfo;
    private readonly Lazy<Assembly> _coreAssembly;
    private Assembly CoreAssembly => _coreAssembly.Value;

    private readonly Lazy<MethodInfo> _getFullPathForUploadingFile;
    private readonly Lazy<string> _tempDirectoryPath;
    private string TempDirectoryPath => _tempDirectoryPath.Value;

    private readonly Lazy<Type> _packageInstallOptionsType;
    private Type PackageInstallOptionsType => _packageInstallOptionsType.Value;

    private readonly Lazy<object> _packageInstallOptions;

    #endregion

    #region Methods: Public

    [OperationContract]
    [WebInvoke(Method = "POST", UriTemplate = "DeleteExistsPackagesZip", BodyStyle = WebMessageBodyStyle.WrappedRequest,
        RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
    public string DeleteExistsPackagesZip()
    {
        if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution"))
        {
            string path = TempDirectoryPath;
            //string packagesPath = Path.Combine(path, "Packages");
            string currentDestinationPath = Path.Combine(path, "Packages.zip");
            if (File.Exists(currentDestinationPath))
            {
                File.Delete(currentDestinationPath);
            }

            return string.Empty;
        }
        else
        {
            throw new Exception("You don't have permission for operation CanManageSolution");
        }
    }

    [OperationContract]
    [WebInvoke(Method = "POST", UriTemplate = "ExistsPackageZip", BodyStyle = WebMessageBodyStyle.WrappedRequest,
        RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
    public bool ExistsPackageZip()
    {
        if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution"))
        {
            string path = TempDirectoryPath;
            string currentDestinationPath = Path.Combine(path, "Packages.zip");
            if (File.Exists(currentDestinationPath))
            {
                return true;
            }

            return false;
        }
        else
        {
            throw new Exception("You don't have permission for operation CanManageSolution");
        }
    }

    [OperationContract]
    [WebInvoke(Method = "POST", UriTemplate = "DownloadExistsPackageZip",
        BodyStyle = WebMessageBodyStyle.WrappedRequest,
        RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
    public Stream DownloadExistsPackageZip()
    {
        if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution"))
        {
            string path = TempDirectoryPath;
            string currentDestinationPath = Path.Combine(path, "Packages.zip");
            if (File.Exists(currentDestinationPath))
            {
                //return true;
                return File.OpenRead(currentDestinationPath);
            }

            throw new Exception("Zip file not found");
        }
        else
        {
            throw new Exception("You don't have permission for operation CanManageSolution");
        }
    }

    #endregion
}

#endregion
