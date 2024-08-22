using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Command
{

	public interface IWindowsFeatureProvider
	{
		IEnumerable<string> GetActiveWindowsFeatures();
		List<WindowsFeature>  GetWindowsFeatures();
	}

	public class WindowsFeatureProvider : IWindowsFeatureProvider
	{
		public IEnumerable<string> GetActiveWindowsFeatures() {
			var features = new List<string>();
			try {
				ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OptionalFeature WHERE InstallState = 1");
				ManagementObjectCollection featureCollection = searcher.Get();
				foreach (ManagementObject featureObject in featureCollection) {
					string featureName = featureObject["Name"].ToString();
					features.Add(featureName);
					string featureCaption = featureObject["Caption"].ToString();
					features.Add(featureCaption);
				}
			} catch (Exception) {
			}
			return features;
		}

		public List<WindowsFeature> GetWindowsFeatures() {
			var features = new List<WindowsFeature>();
			ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OptionalFeature");
			ManagementObjectCollection featureCollection = searcher.Get();
			foreach (ManagementObject featureObject in featureCollection) {
				features.Add(new WindowsFeature() {
					Name = featureObject["Name"].ToString(),
					Caption = featureObject["Caption"].ToString()
				});
			}
			return features;
		}

	}
}
