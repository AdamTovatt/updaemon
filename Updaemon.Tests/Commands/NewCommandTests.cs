using Updaemon.Commands;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class NewCommandTests
    {
        private class NewCommandTestBuilder : IDisposable
        {
            public MockConfigManager ConfigManager { get; } = new MockConfigManager();
            public MockOutputWriter OutputWriter { get; } = new MockOutputWriter();
            public TempFileHelper TempHelper { get; } = new TempFileHelper();
            public string ServiceDirectory { get; }

            public NewCommandTestBuilder()
            {
                ServiceDirectory = TempHelper.TempDirectory;
            }

            public NewCommand Build()
            {
                return new NewCommand(ConfigManager, OutputWriter, ServiceDirectory);
            }

            /// <summary>
            /// Installs a plugin so the command passes validation.
            /// </summary>
            public async Task InstallPluginAsync(string pluginAlias = "github")
            {
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = pluginAlias, Path = "/path/to/plugin" };
                await ConfigManager.AddOrUpdatePluginAsync(pluginInfo);
            }

            public void Dispose()
            {
                TempHelper.Dispose();
            }
        }

        [Fact]
        public async Task ExecuteAsync_RegistersServiceWithConfigManager()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.ConfigManager.MethodCalls, call => call.Contains("RegisterServiceAsync:my-api:my-api"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_DoesNotCreateUnitFileOrEnableService()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                // No unit file should be created anywhere in the service directory
                string[] serviceFiles = Directory.GetFiles(Path.Combine(b.ServiceDirectory, "my-api"), "*", SearchOption.AllDirectories);
                Assert.Empty(serviceFiles);
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesSameNameForLocalAndRemoteByDefault()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "test-service", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.ConfigManager.MethodCalls, call => call.Contains("RegisterServiceAsync:test-service:test-service"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithRemoteFlag_UsesRemoteNameForRegistration()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github", "--remote", "owner/repo" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.ConfigManager.MethodCalls, call => call.Contains("RegisterServiceAsync:my-api:owner/repo"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_OutputMentionsInitCommand()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.OutputWriter.Messages, m => m.Contains("updaemon init"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotFound_ReturnsError()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-service", "--from", "non-existent-plugin" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("not installed"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutAppName_ReturnsErrorCode()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Missing required argument"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutFromFlag_ReturnsErrorCode()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-service" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Missing required flag"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithTypeCli_RegistersWithCliType()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-tool", "--from", "github", "--type", "cli" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.ConfigManager.MethodCalls, call => call.Contains("RegisterServiceAsync:my-tool:my-tool:github:Cli"));

                RegisteredService? service = await b.ConfigManager.GetServiceAsync("my-tool");
                Assert.NotNull(service);
                Assert.Equal(ServiceType.Cli, service.ServiceType);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithTypeService_RegistersWithServiceType()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github", "--type", "service" });

                Assert.Equal(0, exitCode);

                RegisteredService? service = await b.ConfigManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.Equal(ServiceType.Service, service.ServiceType);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutTypeFlag_DefaultsToService()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);

                RegisteredService? service = await b.ConfigManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.Equal(ServiceType.Service, service.ServiceType);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithInvalidType_ReturnsError()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-tool", "--from", "github", "--type", "daemon" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Invalid type"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithTypeCli_OutputSaysCliTool()
        {
            using (NewCommandTestBuilder b = new NewCommandTestBuilder())
            {
                await b.InstallPluginAsync();

                NewCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-tool", "--from", "github", "--type", "cli" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.OutputWriter.Messages, m => m.Contains("CLI tool"));
            }
        }
    }
}
