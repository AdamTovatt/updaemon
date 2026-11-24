using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Updaemon.Serialization;

namespace Updaemon.Models
{
    /// <summary>
    /// What a release is and how to run it: the <c>updaemon.json</c> at the release archive root,
    /// or the equivalent built from <c>install --command/--service</c> for a release without one.
    /// The app's kind follows from which of <see cref="Command"/> and <see cref="Service"/> are present.
    /// </summary>
    public sealed partial record AppManifest
    {
        public const string FileName = "updaemon.json";

        /// <summary>The variable updaemon sets to the app's data directory; not a declarable setting.</summary>
        public const string DataDirectoryVariable = "UPDAEMON_DATA_DIR";

        /// <summary>Path of the executable, relative to the release root.</summary>
        public required string Executable { get; init; }

        /// <summary>The name linked on PATH. Present means the app is a command.</summary>
        public string? Command { get; init; }

        /// <summary>Present means the app is a service.</summary>
        public ServiceSpec? Service { get; init; }

        /// <summary>Environment variables the app reads, by name.</summary>
        // A setter, not init: the source-generated deserializer assigns every init property, null for absent ones.
        public Dictionary<string, SettingSpec> Settings { get; set; } = new Dictionary<string, SettingSpec>();

        /// <summary>Present means the app loads extensions, which updaemon installs into it.</summary>
        public ExtensionsSpec? Extensions { get; init; }

        [JsonIgnore]
        public bool IsCommand => Command != null;

        [JsonIgnore]
        public bool IsService => Service != null;

        /// <summary>"command", "service" or "command + service".</summary>
        [JsonIgnore]
        public string KindLabel => (IsCommand, IsService) switch
        {
            (true, true) => "command + service",
            (true, false) => "command",
            _ => "service",
        };

        /// <summary>Whether updaemon itself sets the variable <paramref name="key"/>, so no value can be stored for it.</summary>
        public bool IsSetByUpdaemon(string key) => key == DataDirectoryVariable || key == Extensions?.Variable;

        /// <summary>Why no value can be stored for it.</summary>
        public static string SetByUpdaemon(string key) => $"{key} is set by updaemon.";

        public bool IsSecret(string key) => Settings.TryGetValue(key, out SettingSpec? spec) && spec.Secret;

        /// <summary>Required settings (those without a default) whose key is not in <paramref name="presentKeys"/>, in name order.</summary>
        public IReadOnlyList<string> MissingRequiredSettings(IEnumerable<string> presentKeys)
        {
            HashSet<string> present = new HashSet<string>(presentKeys, StringComparer.Ordinal);
            return Settings
                .Where(setting => setting.Value.IsRequired && !present.Contains(setting.Key))
                .Select(setting => setting.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Parses and validates a manifest.
        /// </summary>
        /// <exception cref="ManifestException">The manifest is invalid; the message names what is wrong.</exception>
        public static AppManifest Parse(string json)
        {
            AppManifest? manifest;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("executablePath", out _)
                        && !root.TryGetProperty("command", out _)
                        && !root.TryGetProperty("service", out _))
                    {
                        throw new ManifestException(
                            "it uses the old format ('executablePath'). Use 'executable' plus 'command' and/or 'service'; see PublishingWithUpdaemon.md.");
                    }
                }

                manifest = JsonSerializer.Deserialize(json, UpdaemonJsonContext.Default.AppManifest);
            }
            catch (JsonException ex)
            {
                throw new ManifestException(ex.Message);
            }

            if (manifest == null)
            {
                throw new ManifestException("it is empty.");
            }

            // An explicit null deserializes to null despite the non-nullable types; read it as absent.
            manifest.Settings ??= new Dictionary<string, SettingSpec>();
            if (manifest.Service != null)
            {
                manifest.Service.Args ??= new List<string>();
            }

            if (manifest.Extensions != null)
            {
                manifest.Extensions.Known ??= new Dictionary<string, string>();
            }

            manifest.Validate();
            return manifest;
        }

        /// <summary>Reads and validates the manifest in <paramref name="directory"/>, or returns null when there is none.</summary>
        public static async Task<AppManifest?> ReadAsync(string directory, CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            return Parse(await File.ReadAllTextAsync(path, cancellationToken));
        }

        public async Task WriteAsync(string directory, CancellationToken cancellationToken = default)
        {
            string json = JsonSerializer.Serialize(this, UpdaemonJsonContext.Default.AppManifest);
            await File.WriteAllTextAsync(Path.Combine(directory, FileName), json, cancellationToken);
        }

        public static bool IsValidName(string name) => NameRegex().IsMatch(name);

        /// <summary>Updaemon's own name, which its timer unit and command take, so no app may.</summary>
        public const string ReservedAppName = "updaemon";

        public static bool IsValidAppName(string name) => IsValidName(name) && name != ReservedAppName;

        public static bool IsValidSettingKey(string key) => SettingKeyRegex().IsMatch(key);

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(Executable))
            {
                throw new ManifestException("'executable' is empty.");
            }

            if (!SafeText.IsSafe(Executable) || Path.IsPathRooted(Executable) || Executable.Split('/').Contains(".."))
            {
                throw new ManifestException($"'executable' must be a path inside the release, got '{Executable}'.");
            }

            if (Service != null && !Service.Args.All(SafeText.IsSafe))
            {
                throw new ManifestException("'service.args' contains a control character, such as a line break.");
            }

            if (Service != null && Service.HealthCheckSeconds is < ServiceSpec.MinHealthCheckSeconds or > ServiceSpec.MaxHealthCheckSeconds)
            {
                throw new ManifestException(
                    $"'service.healthCheckSeconds' must be {ServiceSpec.MinHealthCheckSeconds} to {ServiceSpec.MaxHealthCheckSeconds}, got {Service.HealthCheckSeconds}.");
            }

            if (!IsCommand && !IsService)
            {
                throw new ManifestException("it has neither 'command' nor 'service'; at least one is required.");
            }

            if (Command != null && !IsValidName(Command))
            {
                throw new ManifestException($"'command' must be a plain file name (letters, digits, '.', '_', '-'), got '{Command}'.");
            }

            if (Extensions != null)
            {
                ValidateExtensions(Extensions);
            }

            foreach (KeyValuePair<string, SettingSpec> setting in Settings)
            {
                if (!IsValidSettingKey(setting.Key))
                {
                    throw new ManifestException($"setting '{setting.Key}' is not a valid environment variable name.");
                }

                if (IsSetByUpdaemon(setting.Key))
                {
                    throw new ManifestException($"setting '{setting.Key}' is reserved; updaemon sets it.");
                }

                if (setting.Value.Default != null && !SafeText.IsSafe(setting.Value.Default))
                {
                    throw new ManifestException($"the default of '{setting.Key}' contains a control character, such as a line break.");
                }
            }
        }

        private static void ValidateExtensions(ExtensionsSpec extensions)
        {
            if (string.IsNullOrEmpty(extensions.Variable))
            {
                throw new ManifestException("'extensions.variable' is missing.");
            }

            if (!IsValidSettingKey(extensions.Variable))
            {
                throw new ManifestException($"'extensions.variable' must be an environment variable name, got '{extensions.Variable}'.");
            }

            if (extensions.Variable == DataDirectoryVariable)
            {
                throw new ManifestException($"'extensions.variable' cannot be {DataDirectoryVariable}; updaemon sets it.");
            }

            if (extensions.Check?.All(argument => argument != null && SafeText.IsSafe(argument)) == false)
            {
                throw new ManifestException("'extensions.check' contains a null or a control character, such as a line break.");
            }

            foreach (KeyValuePair<string, string> known in extensions.Known)
            {
                if (!IsValidAppName(known.Key))
                {
                    throw new ManifestException($"'extensions.known' has '{known.Key}', which is not a valid extension id (letters, digits, '.', '_', '-').");
                }

                if (known.Value == null || PackageSource.TryParse(known.Value) == null)
                {
                    throw new ManifestException($"the source of '{known.Key}' in 'extensions.known' must be <plugin>:<remote>, got '{known.Value}'.");
                }
            }
        }

        [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
        private static partial Regex NameRegex();

        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
        private static partial Regex SettingKeyRegex();
    }

    public sealed class ServiceSpec
    {
        public const int DefaultHealthCheckSeconds = 10;
        public const int MinHealthCheckSeconds = 1;
        public const int MaxHealthCheckSeconds = 300;

        /// <summary>Arguments the service runs with.</summary>
        // A setter, not init: see AppManifest.Settings.
        public List<string> Args { get; set; } = new List<string>();

        /// <summary>How long the service must keep running after a (re)start to count as healthy.</summary>
        public int HealthCheckSeconds { get; set; } = DefaultHealthCheckSeconds;

        [JsonIgnore]
        public TimeSpan HealthCheck => TimeSpan.FromSeconds(HealthCheckSeconds);
    }

    /// <summary>How the app loads extensions: folders of files updaemon installs for it.</summary>
    public sealed class ExtensionsSpec
    {
        /// <summary>The environment variable that carries the folder of extensions.</summary>
        // Validated, not 'required', so leaving it out is reported as every other invalid field is.
        public string? Variable { get; set; }

        /// <summary>
        /// Arguments to the executable, followed by an extension's folder, that make it say whether it would
        /// load that extension. Null means it accepts every extension.
        /// </summary>
        public List<string>? Check { get; set; }

        /// <summary>Where extensions the app knows of come from, as <c>&lt;plugin&gt;:&lt;remote&gt;</c>, by id.</summary>
        // A setter, not init: see AppManifest.Settings.
        public Dictionary<string, string> Known { get; set; } = new Dictionary<string, string>();
    }

    public sealed class SettingSpec
    {
        public string? Description { get; init; }

        public bool Required { get; init; }

        public bool Secret { get; init; }

        public string? Default { get; init; }

        /// <summary>A setting with a default is never required.</summary>
        [JsonIgnore]
        public bool IsRequired => Required && Default == null;
    }

    /// <summary>
    /// An invalid <c>updaemon.json</c>.
    /// </summary>
    public sealed class ManifestException : Exception
    {
        public ManifestException(string problem)
            : base($"Invalid {AppManifest.FileName}: {problem}")
        {
        }
    }
}
