using Updaemon.Interfaces;
using Updaemon.Services;

namespace Updaemon.Tests.Mocks
{
    public class MockFilePermissionManager : IFilePermissionManager
    {
        private readonly FilePermissionManager _real = new FilePermissionManager(new MockOutputWriter());

        /// <summary>Whether modes are set for real, too. Owners are not: that needs root.</summary>
        public bool SetsModes { get; set; }

        public List<string> MethodCalls { get; } = new List<string>();
        public List<string> ExecutablePermissionsCalls { get; } = new List<string>();
        public List<string> TreesMadeReadable { get; } = new List<string>();

        public Task SetExecutablePermissionsAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            string methodCall = $"{nameof(SetExecutablePermissionsAsync)}:{executablePath}";
            MethodCalls.Add(methodCall);
            ExecutablePermissionsCalls.Add(executablePath);
            return SetsModes ? _real.SetExecutablePermissionsAsync(executablePath, cancellationToken) : Task.CompletedTask;
        }

        public Task MakeTreeReadableAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string methodCall = $"{nameof(MakeTreeReadableAsync)}:{directoryPath}";
            MethodCalls.Add(methodCall);
            TreesMadeReadable.Add(directoryPath);
            OnMakeTreeReadable?.Invoke(directoryPath);
            return SetsModes ? _real.MakeTreeReadableAsync(directoryPath, cancellationToken) : Task.CompletedTask;
        }

        public Task MakeDirectoryEnterableAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(MakeDirectoryEnterableAsync)}:{directoryPath}");
            return SetsModes ? _real.MakeDirectoryEnterableAsync(directoryPath, cancellationToken) : Task.CompletedTask;
        }

        /// <summary>Called with the path at each <see cref="MakeTreeReadableAsync"/>, to look at what is there then.</summary>
        public Action<string>? OnMakeTreeReadable { get; set; }

        /// <summary>Users <see cref="UserExistsAsync"/> reports as existing.</summary>
        public HashSet<string> ExistingUsers { get; } = new HashSet<string>();

        /// <summary>Called with the path and user at each <see cref="SetOwnerAsync"/>, to look at the file as it is then.</summary>
        public Action<string, string>? OnSetOwner { get; set; }

        public Task SetOwnerAsync(string path, string user, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SetOwnerAsync)}:{path}:{user}");
            OnSetOwner?.Invoke(path, user);
            return Task.CompletedTask;
        }

        public Task<bool> UserExistsAsync(string user, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingUsers.Contains(user));
        }
    }
}

