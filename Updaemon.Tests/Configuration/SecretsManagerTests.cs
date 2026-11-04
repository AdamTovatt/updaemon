using Updaemon.Configuration;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Configuration
{
    public class SecretsManagerTests
    {
        [Fact]
        public async Task SetSecretAsync_CreatesFileAndStoresSecret()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                await secretsManager.SetSecretAsync("github", "apiKey", "abc123");

                string? value = await secretsManager.GetSecretAsync("github", "apiKey");
                Assert.Equal("abc123", value);
            }
        }

        [Fact]
        public async Task GetSecretAsync_ReturnsStoredValue()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                await secretsManager.SetSecretAsync("byteshelf", "tenantId", "550e8400-e29b-41d4-a716-446655440000");

                string? value = await secretsManager.GetSecretAsync("byteshelf", "tenantId");
                Assert.Equal("550e8400-e29b-41d4-a716-446655440000", value);
            }
        }

        [Fact]
        public async Task GetSecretAsync_NonExistentKey_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                string? value = await secretsManager.GetSecretAsync("github", "nonExistent");

                Assert.Null(value);
            }
        }

        [Fact]
        public async Task SetSecretAsync_UpdatesExistingKey()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                await secretsManager.SetSecretAsync("github", "apiKey", "oldValue");
                await secretsManager.SetSecretAsync("github", "apiKey", "newValue");

                string? value = await secretsManager.GetSecretAsync("github", "apiKey");
                Assert.Equal("newValue", value);
            }
        }

        [Fact]
        public async Task GetPluginSecretsFormattedAsync_ReturnsKeyValueFormat()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                await secretsManager.SetSecretAsync("github", "apiKey", "abc123");
                await secretsManager.SetSecretAsync("github", "tenantId", "550e8400");

                string? formatted = await secretsManager.GetPluginSecretsFormattedAsync("github");

                Assert.NotNull(formatted);
                Assert.Contains("apiKey=abc123", formatted);
                Assert.Contains("tenantId=550e8400", formatted);
            }
        }

        [Fact]
        public async Task GetPluginSecretsFormattedAsync_NoSecrets_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                string? formatted = await secretsManager.GetPluginSecretsFormattedAsync("github");

                Assert.Null(formatted);
            }
        }

        [Fact]
        public async Task RemoveSecretAsync_RemovesKey()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                await secretsManager.SetSecretAsync("github", "apiKey", "abc123");
                await secretsManager.RemoveSecretAsync("github", "apiKey");

                string? value = await secretsManager.GetSecretAsync("github", "apiKey");
                Assert.Null(value);
            }
        }

        [Fact]
        public async Task LoadSecretsAsync_HandlesMalformedLines()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                // Create a secrets file with malformed lines
                string secretsFilePath = secretsManager.GetPluginSecretsPath("github");
                Directory.CreateDirectory(Path.GetDirectoryName(secretsFilePath)!);
                await File.WriteAllTextAsync(secretsFilePath, "validKey=validValue\nmalformedLine\n=noKey\nkey=");

                string? value = await secretsManager.GetSecretAsync("github", "validKey");
                Assert.Equal("validValue", value);

                // Malformed lines should be ignored
                string? emptyValue = await secretsManager.GetSecretAsync("github", "key");
                Assert.Equal("", emptyValue);
            }
        }

        [Fact]
        public async Task SetSecretAsync_MultipleKeys_AllPersisted()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                await secretsManager.SetSecretAsync("github", "key1", "value1");
                await secretsManager.SetSecretAsync("github", "key2", "value2");
                await secretsManager.SetSecretAsync("github", "key3", "value3");

                // Create a new instance to test persistence
                SecretsManager newInstance = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                string? value1 = await newInstance.GetSecretAsync("github", "key1");
                string? value2 = await newInstance.GetSecretAsync("github", "key2");
                string? value3 = await newInstance.GetSecretAsync("github", "key3");

                Assert.Equal("value1", value1);
                Assert.Equal("value2", value2);
                Assert.Equal("value3", value3);
            }
        }

        [Fact]
        public async Task SecretsAreIsolatedPerPlugin()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                await secretsManager.SetSecretAsync("github", "apiKey", "github-key");
                await secretsManager.SetSecretAsync("byteshelf", "apiKey", "byteshelf-key");

                string? githubKey = await secretsManager.GetSecretAsync("github", "apiKey");
                string? byteshelfKey = await secretsManager.GetSecretAsync("byteshelf", "apiKey");

                Assert.Equal("github-key", githubKey);
                Assert.Equal("byteshelf-key", byteshelfKey);
            }
        }

        [Fact]
        public void GetPluginSecretsPath_ReturnsCorrectPath()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                string path = secretsManager.GetPluginSecretsPath("github");

                string expectedPath = Path.Combine(tempHelper.TempDirectory, "root", "plugins", "github", "secrets.txt");
                Assert.Equal(expectedPath, path);
            }
        }

        [Fact]
        public async Task AppSecrets_RoundTripInAnOwnerOnlyFileInADirectoryOthersCanOnlyPass()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                MockFilePermissionManager permissions = new MockFilePermissionManager();
                SecretsManager secretsManager = new SecretsManager(scope, permissions);

                await secretsManager.SaveAppSecretsAsync("byteshelf", owner: null, new Dictionary<string, string> { ["API_KEY"] = "a\"b$c" });

                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(scope.GetSecretsFilePath("byteshelf")));
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute,
                    File.GetUnixFileMode(scope.SecretsDirectory));
                Assert.Equal("a\"b$c", (await secretsManager.GetAppSecretsAsync("byteshelf"))["API_KEY"]);
                // Without an owner, the file stays the scope user's.
                Assert.Empty(permissions.MethodCalls);
            }
        }

        [Fact]
        public async Task SaveAppSecretsAsync_WithAnOwner_GivesTheNewFileToItBeforeItReplacesTheOld()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                MockFilePermissionManager permissions = new MockFilePermissionManager();
                SecretsManager secretsManager = new SecretsManager(scope, permissions);
                string path = scope.GetSecretsFilePath("api");
                await secretsManager.SaveAppSecretsAsync("api", "shelf", new Dictionary<string, string> { ["API_KEY"] = "old" });
                List<(string User, string Content, string? InPlace)> handovers = new List<(string, string, string?)>();
                permissions.OnSetOwner = (file, user) => handovers.Add((user, File.ReadAllText(file), File.Exists(path) ? File.ReadAllText(path) : null));

                await secretsManager.SaveAppSecretsAsync("api", "shelf", new Dictionary<string, string> { ["API_KEY"] = "new" });

                (string user, string content, string? inPlace) = Assert.Single(handovers);
                Assert.Equal("shelf", user);
                Assert.Equal("API_KEY=\"new\"\n", content);
                Assert.Equal("API_KEY=\"old\"\n", inPlace);
                Assert.Equal("new", (await secretsManager.GetAppSecretsAsync("api"))["API_KEY"]);
            }
        }

        [Fact]
        public async Task SaveAppSecretsAsync_EmptySet_DeletesTheFileAndHandsNothingOver()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                MockFilePermissionManager permissions = new MockFilePermissionManager();
                SecretsManager secretsManager = new SecretsManager(scope, permissions);
                await secretsManager.SaveAppSecretsAsync("byteshelf", "shelf", new Dictionary<string, string> { ["API_KEY"] = "k" });
                permissions.MethodCalls.Clear();

                await secretsManager.SaveAppSecretsAsync("byteshelf", "shelf", new Dictionary<string, string>());

                Assert.Empty(permissions.MethodCalls);

                Assert.False(File.Exists(scope.GetSecretsFilePath("byteshelf")));
            }
        }

        [Fact]
        public void GetPluginSecretsPath_DifferentAliases_ReturnDifferentPaths()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(TestScope.Create(tempHelper.TempDirectory), new MockFilePermissionManager());

                string githubPath = secretsManager.GetPluginSecretsPath("github");
                string byteshelfPath = secretsManager.GetPluginSecretsPath("byteshelf");

                Assert.NotEqual(githubPath, byteshelfPath);
                Assert.Contains("github", githubPath);
                Assert.Contains("byteshelf", byteshelfPath);
            }
        }
    }
}

