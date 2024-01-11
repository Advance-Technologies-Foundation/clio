using System;
using System.Management;

public class WindowsFeatureManager { 
	public void EnableFeature(string featureName) {
		try {
			ManagementScope scope = new ManagementScope("\\\\.\\ROOT\\CIMV2");
			scope.Connect();

			ObjectGetOptions options = new ObjectGetOptions();
			ManagementPath path = new ManagementPath("Win32_OptionalFeature");

			using (ManagementClass featureClass = new ManagementClass(scope, path, options)) {
				ManagementBaseObject inParams = featureClass.GetMethodParameters("Install");
				inParams["Name"] = featureName;

				ManagementBaseObject outParams = featureClass.InvokeMethod("Install", inParams, null);

				if (outParams != null && outParams["ReturnValue"] != null) {
					Console.WriteLine($"Feature installation result: {outParams["ReturnValue"]}");
				}
			}
		} catch (ManagementException e) {
			Console.WriteLine("An error occurred: " + e.Message);
		}
	}
}