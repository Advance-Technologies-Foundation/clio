using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace Clio.Command;

public interface IWindowsFeatureProvider
{
    IEnumerable<string> GetActiveWindowsFeatures();

    List<WindowsFeature> GetWindowsFeatures();
}

public class WindowsFeatureProvider : IWindowsFeatureProvider
{
    public IEnumerable<string> GetActiveWindowsFeatures()
    {
        List<string> features = [];
        try
        {
            ManagementObjectSearcher searcher = new("SELECT * FROM Win32_OptionalFeature WHERE InstallState = 1");
            ManagementObjectCollection featureCollection = searcher.Get();
            foreach (ManagementObject featureObject in featureCollection.Cast<ManagementObject>())
            {
                string featureName = featureObject["Name"].ToString();
                features.Add(featureName);
                string featureCaption = featureObject["Caption"].ToString();
                features.Add(featureCaption);
            }
        }
        catch (Exception)
        {
        }

        return features;
    }

    public List<WindowsFeature> GetWindowsFeatures()
    {
        List<WindowsFeature> features = [];
        ManagementObjectSearcher searcher = new("SELECT * FROM Win32_OptionalFeature");
        ManagementObjectCollection featureCollection = searcher.Get();
        foreach (ManagementObject featureObject in featureCollection.Cast<ManagementObject>())
        {
            features.Add(new WindowsFeature
            {
                Name = featureObject["Name"].ToString(), Caption = featureObject["Caption"].ToString()
            });
        }

        return features;
    }
}
