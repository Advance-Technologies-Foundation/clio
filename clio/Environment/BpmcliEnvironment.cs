using System;
using System.IO;
using System.Linq;

namespace clio.environment
{
	internal class CreatioEnvironment : ICreatioEnvironment
	{
		private const string PathVariableName = "PATH";

		private IResult RegisterPath(string path, EnvironmentVariableTarget target) {
			var result = new EnvironmentResult();
			string pathValue = Environment.GetEnvironmentVariable(PathVariableName, target);
			if (string.IsNullOrEmpty(pathValue)) {
				result.AppendMessage($"{PathVariableName} variable is empty!");
				return result;
			}
			if (pathValue.Contains(path)) {
				result.AppendMessage($"{PathVariableName} variable already registered!");
				return result;
			}
			result.AppendMessage($"register path {path} in {PathVariableName} variable.");
			var value = string.Concat(pathValue, Path.PathSeparator + path.Trim(Path.PathSeparator));
			Environment.SetEnvironmentVariable(PathVariableName, value, target);
			result.AppendMessage($"{PathVariableName} variable registered.");
			return result;
		}

		public string GetRegisteredPath() {
			var environmentPath = Environment.GetEnvironmentVariable(PathVariableName);
			string[] cliPath = (environmentPath?.Split(Path.PathSeparator));
			return cliPath?.FirstOrDefault(p => p.Contains("clio"));
		}

		public IResult UserRegisterPath(string path) {
			return RegisterPath(path, EnvironmentVariableTarget.User);
		}

		public IResult MachineRegisterPath(string path) {
			return RegisterPath(path, EnvironmentVariableTarget.Machine);
		}

	}
}
