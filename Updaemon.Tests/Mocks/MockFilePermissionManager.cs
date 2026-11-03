using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    public class MockFilePermissionManager : IFilePermissionManager
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public List<string> ExecutablePermissionsCalls { get; } = new List<string>();
        public List<string> DirectoryPermissionsCalls { get; } = new List<string>();

        public Task SetExecutablePermissionsAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            string methodCall = $"SetExecutablePermissionsAsync:{executablePath}";
            MethodCalls.Add(methodCall);
            ExecutablePermissionsCalls.Add(executablePath);
            return Task.CompletedTask;
        }

        public Task SetDirectoryPermissionsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string methodCall = $"SetDirectoryPermissionsAsync:{directoryPath}";
            MethodCalls.Add(methodCall);
            DirectoryPermissionsCalls.Add(directoryPath);
            return Task.CompletedTask;
        }

        /// <summary>Users <see cref="UserExistsAsync"/> reports as existing.</summary>
        public HashSet<string> ExistingUsers { get; } = new HashSet<string>();

        public Task SetOwnerAsync(string path, string user, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"SetOwnerAsync:{path}:{user}");
            return Task.CompletedTask;
        }

        public Task<bool> UserExistsAsync(string user, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingUsers.Contains(user));
        }
    }
}

