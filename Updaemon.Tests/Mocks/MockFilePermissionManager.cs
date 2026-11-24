using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    public class MockFilePermissionManager : IFilePermissionManager
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public List<string> ExecutablePermissionsCalls { get; } = new List<string>();
        public List<string> TreesMadeReadable { get; } = new List<string>();

        public Task SetExecutablePermissionsAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            string methodCall = $"SetExecutablePermissionsAsync:{executablePath}";
            MethodCalls.Add(methodCall);
            ExecutablePermissionsCalls.Add(executablePath);
            return Task.CompletedTask;
        }

        public Task MakeTreeReadableAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string methodCall = $"MakeTreeReadableAsync:{directoryPath}";
            MethodCalls.Add(methodCall);
            TreesMadeReadable.Add(directoryPath);
            OnMakeTreeReadable?.Invoke(directoryPath);
            return Task.CompletedTask;
        }

        public Task MakeDirectoryEnterableAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"MakeDirectoryEnterableAsync:{directoryPath}");
            return Task.CompletedTask;
        }

        /// <summary>Called with the path at each <see cref="MakeTreeReadableAsync"/>, to look at what is there then.</summary>
        public Action<string>? OnMakeTreeReadable { get; set; }

        /// <summary>Users <see cref="UserExistsAsync"/> reports as existing.</summary>
        public HashSet<string> ExistingUsers { get; } = new HashSet<string>();

        /// <summary>Called with the path and user at each <see cref="SetOwnerAsync"/>, to look at the file as it is then.</summary>
        public Action<string, string>? OnSetOwner { get; set; }

        public Task SetOwnerAsync(string path, string user, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"SetOwnerAsync:{path}:{user}");
            OnSetOwner?.Invoke(path, user);
            return Task.CompletedTask;
        }

        public Task<bool> UserExistsAsync(string user, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingUsers.Contains(user));
        }
    }
}

