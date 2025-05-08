using System;
using System.Collections.Generic;
using System.Management;

namespace Clio.Command;

public interface IWindowsFeatureProvider
{

    #region Methods: Public

    IEnumerable<string> GetActiveWindowsFeatures();

    List<WindowsFeature> GetWindowsFeatures();

    #endregion

}

public class WindowsFeatureProvider : IWindowsFeatureProvider
{

    #region Methods: Public

    public IEnumerable<string> GetActiveWindowsFeatures()
    {
        List<string> features = new();
        try
        {
            ManagementObjectSearcher searcher = new("SELECT * FROM Win32_OptionalFeature WHERE InstallState = 1");
            ManagementObjectCollection featureCollection = searcher.Get();
            foreach (ManagementObject featureObject in featureCollection)
            {
                string featureName = featureObject["Name"].ToString();
                features.Add(featureName);
                string featureCaption = featureObject["Caption"].ToString();
                features.Add(featureCaption);
            }
        }
        catch (Exception)
        { }
        return features;
    }

    public List<WindowsFeature> GetWindowsFeatures()
    {
        List<WindowsFeature> features = new();
        ManagementObjectSearcher searcher = new("SELECT * FROM Win32_OptionalFeature");
        ManagementObjectCollection featureCollection = searcher.Get();
        foreach (ManagementObject featureObject in featureCollection)
        {
            features.Add(new WindowsFeature
            {
                Name = featureObject["Name"].ToString(), Caption = featureObject["Caption"].ToString()
            });
        }
        return features;
    }

    #endregion

}
