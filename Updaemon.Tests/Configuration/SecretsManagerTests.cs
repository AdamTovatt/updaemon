using Updaemon.Configuration;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Configuration
{
    public class SecretsManagerTests
    {
        [Fact]
        public async Task SetSecretAsync_CreatesFileAndStoresSecret()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                string? value = await secretsManager.GetSecretAsync("github", "nonExistent");

                Assert.Null(value);
            }
        }

        [Fact]
        public async Task SetSecretAsync_UpdatesExistingKey()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                string? formatted = await secretsManager.GetPluginSecretsFormattedAsync("github");

                Assert.Null(formatted);
            }
        }

        [Fact]
        public async Task RemoveSecretAsync_RemovesKey()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                await secretsManager.SetSecretAsync("github", "key1", "value1");
                await secretsManager.SetSecretAsync("github", "key2", "value2");
                await secretsManager.SetSecretAsync("github", "key3", "value3");

                // Create a new instance to test persistence
                SecretsManager newInstance = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

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
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                string path = secretsManager.GetPluginSecretsPath("github");

                string expectedPath = Path.Combine(tempHelper.TempDirectory, "plugins", "github", "secrets.txt");
                Assert.Equal(expectedPath, path);
            }
        }

        [Fact]
        public void GetPluginSecretsPath_DifferentAliases_ReturnDifferentPaths()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                string githubPath = secretsManager.GetPluginSecretsPath("github");
                string byteshelfPath = secretsManager.GetPluginSecretsPath("byteshelf");

                Assert.NotEqual(githubPath, byteshelfPath);
                Assert.Contains("github", githubPath);
                Assert.Contains("byteshelf", byteshelfPath);
            }
        }
    }
}

