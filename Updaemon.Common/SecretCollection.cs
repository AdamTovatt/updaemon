namespace Updaemon.Common
{
    /// <summary>
    /// Represents a collection of secrets as key-value pairs.
    /// Provides convenient methods for accessing secrets with both case-sensitive and case-insensitive lookups.
    /// </summary>
    public class SecretCollection
    {
        private readonly Dictionary<string, string> _secrets;
        private readonly Dictionary<string, string> _secretsIgnoreCase;

        /// <summary>
        /// Initializes a new instance of the SecretCollection class from a dictionary.
        /// </summary>
        /// <param name="secrets">Dictionary of secret key-value pairs.</param>
        public SecretCollection(Dictionary<string, string> secrets)
        {
            _secrets = new Dictionary<string, string>(secrets);
            _secretsIgnoreCase = new Dictionary<string, string>(secrets, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a SecretCollection from a string containing key=value pairs separated by line breaks.
        /// </summary>
        /// <param name="secrets">String containing secrets in key=value format, or null/empty.</param>
        /// <returns>A SecretCollection instance. Returns empty collection if input is null or empty.</returns>
        public static SecretCollection FromString(string? secrets)
        {
            Dictionary<string, string> parsedSecrets = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(secrets))
            {
                return new SecretCollection(parsedSecrets);
            }

            string[] lines = secrets.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();
                    parsedSecrets[key] = value;
                }
            }

            return new SecretCollection(parsedSecrets);
        }

        /// <summary>
        /// Gets the value associated with the specified key (case-sensitive).
        /// </summary>
        /// <param name="key">The key of the secret to get.</param>
        /// <returns>The value if found, otherwise null.</returns>
        public string? GetValue(string key)
        {
            if (_secrets.TryGetValue(key, out string? value))
                return value;

            return null;
        }

        /// <summary>
        /// Gets the value associated with the specified key (case-insensitive).
        /// </summary>
        /// <param name="key">The key of the secret to get.</param>
        /// <returns>The value if found, otherwise null.</returns>
        public string? GetValueIgnoreCase(string key)
        {
            if (_secretsIgnoreCase.TryGetValue(key, out string? value))
                return value;

            return null;
        }

        /// <summary>
        /// Tries to get the value associated with the specified key (case-sensitive).
        /// </summary>
        /// <param name="key">The key of the secret to get.</param>
        /// <param name="value">The value if found, otherwise null.</param>
        /// <returns>True if the key was found, otherwise false.</returns>
        public bool TryGetValue(string key, out string? value)
        {
            return _secrets.TryGetValue(key, out value);
        }

        /// <summary>
        /// Tries to get the value associated with the specified key (case-insensitive).
        /// </summary>
        /// <param name="key">The key of the secret to get.</param>
        /// <param name="value">The value if found, otherwise null.</param>
        /// <returns>True if the key was found, otherwise false.</returns>
        public bool TryGetValueIgnoreCase(string key, out string? value)
        {
            return _secretsIgnoreCase.TryGetValue(key, out value);
        }

        /// <summary>
        /// Converts the SecretCollection to a dictionary for serialization.
        /// </summary>
        /// <returns>A dictionary containing all secret key-value pairs.</returns>
        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>(_secrets);
        }
    }
}

