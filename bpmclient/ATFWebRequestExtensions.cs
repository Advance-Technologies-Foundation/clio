using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Bpmonline.Client
{
	public static class ATFWebRequestExtension
	{
		public static string GetServiceResponse(this HttpWebRequest request) {
			using (WebResponse response = request.GetResponse()) {
				using (var dataStream = response.GetResponseStream()) {
					using (StreamReader reader = new StreamReader(dataStream)) {
						return reader.ReadToEnd();
					}
				}
			}
		}

		public static void SaveToFile(this HttpWebRequest request, string filePath) {
			using (WebResponse response = request.GetResponse()) {
				using (var dataStream = response.GetResponseStream()) {
					if (dataStream != null) {
						using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write)) {
							dataStream.CopyTo(fileStream);
						}
					}
				}
			}
		}
	}
}
