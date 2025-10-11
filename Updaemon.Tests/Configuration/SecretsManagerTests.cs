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

                await secretsManager.SetSecretAsync("apiKey", "abc123");

                string? value = await secretsManager.GetSecretAsync("apiKey");
                Assert.Equal("abc123", value);
            }
        }

        [Fact]
        public async Task GetSecretAsync_ReturnsStoredValue()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                await secretsManager.SetSecretAsync("tenantId", "550e8400-e29b-41d4-a716-446655440000");

                string? value = await secretsManager.GetSecretAsync("tenantId");
                Assert.Equal("550e8400-e29b-41d4-a716-446655440000", value);
            }
        }

        [Fact]
        public async Task GetSecretAsync_NonExistentKey_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                string? value = await secretsManager.GetSecretAsync("nonExistent");

                Assert.Null(value);
            }
        }

        [Fact]
        public async Task SetSecretAsync_UpdatesExistingKey()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                await secretsManager.SetSecretAsync("apiKey", "oldValue");
                await secretsManager.SetSecretAsync("apiKey", "newValue");

                string? value = await secretsManager.GetSecretAsync("apiKey");
                Assert.Equal("newValue", value);
            }
        }

        [Fact]
        public async Task GetAllSecretsFormattedAsync_ReturnsKeyValueFormat()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                await secretsManager.SetSecretAsync("apiKey", "abc123");
                await secretsManager.SetSecretAsync("tenantId", "550e8400");

                string? formatted = await secretsManager.GetAllSecretsFormattedAsync();

                Assert.NotNull(formatted);
                Assert.Contains("apiKey=abc123", formatted);
                Assert.Contains("tenantId=550e8400", formatted);
            }
        }

        [Fact]
        public async Task GetAllSecretsFormattedAsync_NoSecrets_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                string? formatted = await secretsManager.GetAllSecretsFormattedAsync();

                Assert.Null(formatted);
            }
        }

        [Fact]
        public async Task RemoveSecretAsync_RemovesKey()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                await secretsManager.SetSecretAsync("apiKey", "abc123");
                await secretsManager.RemoveSecretAsync("apiKey");

                string? value = await secretsManager.GetSecretAsync("apiKey");
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
                string secretsFilePath = Path.Combine(tempHelper.TempDirectory, "secrets.txt");
                await File.WriteAllTextAsync(secretsFilePath, "validKey=validValue\nmalformedLine\n=noKey\nkey=");

                string? value = await secretsManager.GetSecretAsync("validKey");
                Assert.Equal("validValue", value);

                // Malformed lines should be ignored
                string? emptyValue = await secretsManager.GetSecretAsync("key");
                Assert.Equal("", emptyValue);
            }
        }

        [Fact]
        public async Task SetSecretAsync_MultipleKeys_AllPersisted()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                SecretsManager secretsManager = new SecretsManager(tempHelper.TempDirectory);

                await secretsManager.SetSecretAsync("key1", "value1");
                await secretsManager.SetSecretAsync("key2", "value2");
                await secretsManager.SetSecretAsync("key3", "value3");

                // Create a new instance to test persistence
                SecretsManager newInstance = new SecretsManager(tempHelper.TempDirectory);
                
                string? value1 = await newInstance.GetSecretAsync("key1");
                string? value2 = await newInstance.GetSecretAsync("key2");
                string? value3 = await newInstance.GetSecretAsync("key3");

                Assert.Equal("value1", value1);
                Assert.Equal("value2", value2);
                Assert.Equal("value3", value3);
            }
        }
    }
}

