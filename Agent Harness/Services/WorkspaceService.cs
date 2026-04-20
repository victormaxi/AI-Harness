using System;
using System.IO;
using System.Threading.Tasks;

namespace Agent_Harness.Services
{
    public interface IWorkspaceService
    {
        Task<string> SaveFileAsync(string fileName, string content);
        Task<string> ReadFileAsync(string fileName);
        string[] ListFiles();
        bool FileExists(string fileName);
    }

    public class LocalWorkspaceService : IWorkspaceService
    {
        private readonly string _workspacePath;

        public LocalWorkspaceService()
        {
            _workspacePath = Path.Combine(Directory.GetCurrentDirectory(), "AgentWorkspace");
            if (!Directory.Exists(_workspacePath))
            {
                Directory.CreateDirectory(_workspacePath);
            }
        }

        public async Task<string> SaveFileAsync(string fileName, string content)
        {
            // Sanitize file name to prevent directory traversal
            var safeName = Path.GetFileName(fileName);
            var filePath = Path.Combine(_workspacePath, safeName);
            
            await File.WriteAllTextAsync(filePath, content);
            return $"File '{safeName}' saved to workspace. Size: {content.Length} characters.";
        }

        public async Task<string> ReadFileAsync(string fileName)
        {
            var safeName = Path.GetFileName(fileName);
            var filePath = Path.Combine(_workspacePath, safeName);

            if (!File.Exists(filePath))
                return "Error: File not found in workspace.";

            return await File.ReadAllTextAsync(filePath);
        }

        public string[] ListFiles()
        {
            return Directory.GetFiles(_workspacePath, "*", SearchOption.TopDirectoryOnly);
        }

        public bool FileExists(string fileName)
        {
            var safeName = Path.GetFileName(fileName);
            return File.Exists(Path.Combine(_workspacePath, safeName));
        }
    }
}
