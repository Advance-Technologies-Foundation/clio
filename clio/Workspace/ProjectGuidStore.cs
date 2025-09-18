using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Clio.Common;

namespace Clio.Workspaces
{
    public class ProjectGuidStore
    {
        private readonly IFileSystem _fileSystem;
        private readonly string _storePath;
        private Dictionary<string, Guid> _guids;

        public ProjectGuidStore(IFileSystem fileSystem, string storePath)
        {
            _fileSystem = fileSystem;
            _storePath = storePath;
            Load();
        }

        private void Load()
        {
            if (_fileSystem.GetFileSize(_storePath) > 0)
            {
                var json = System.Text.Encoding.UTF8.GetString(_fileSystem.ReadAllBytes(_storePath));
                _guids = JsonConvert.DeserializeObject<Dictionary<string, Guid>>(json) ?? new Dictionary<string, Guid>();
            }
            else
            {
                _guids = new Dictionary<string, Guid>();
            }
        }

        public Guid GetOrCreateGuid(string projectName)
        {
            if (_guids.TryGetValue(projectName, out var guid))
                return guid;
            guid = Guid.NewGuid();
            _guids[projectName] = guid;
            Save();
            return guid;
        }

        private void Save()
        {
            var json = JsonConvert.SerializeObject(_guids, Formatting.Indented);
            _fileSystem.WriteAllTextToFile(_storePath, json);
        }
    }
}
