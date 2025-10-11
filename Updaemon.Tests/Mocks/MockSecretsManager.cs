using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of ISecretsManager with in-memory storage.
    /// </summary>
    public class MockSecretsManager : ISecretsManager
    {
        private readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();
        public List<string> MethodCalls { get; } = new List<string>();

        public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SetSecretAsync)}:{key}:{value}");
            _secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(GetSecretAsync)}:{key}");
            return Task.FromResult(_secrets.GetValueOrDefault(key));
        }

        public Task<string?> GetAllSecretsFormattedAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(GetAllSecretsFormattedAsync));

            if (_secrets.Count == 0)
            {
                return Task.FromResult<string?>(null);
            }

            string formatted = string.Join(Environment.NewLine, _secrets.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            return Task.FromResult<string?>(formatted);
        }

        public Task RemoveSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(RemoveSecretAsync)}:{key}");
            _secrets.Remove(key);
            return Task.CompletedTask;
        }
    }
}

