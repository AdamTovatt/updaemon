using Updaemon.Configuration;
using Updaemon.Services;

namespace Updaemon.Tests.Services
{
    public class UnitFileManagerTests
    {
        private static UnitFileManager NewSystemdManager(string testDirectory) =>
            new UnitFileManager(testDirectory, UnitFilePlatform.Systemd);

        private static UnitFileManager NewLaunchdManager(string testDirectory) =>
            new UnitFileManager(testDirectory, UnitFilePlatform.Launchd);

        [Fact]
        public async Task ReadTemplateAsync_SystemdTemplate_ShouldReturnTemplateContent()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = NewSystemdManager(testDirectory);

                string template = await manager.ReadTemplateAsync();

                Assert.NotEmpty(template);
                Assert.Contains("[Unit]", template);
                Assert.Contains("[Service]", template);
                Assert.Contains("[Install]", template);
                Assert.Contains("{SERVICE_NAME}", template);
                Assert.Contains("{DESCRIPTION}", template);
                Assert.Contains("{WORKING_DIRECTORY}", template);
                Assert.Contains("{EXECUTABLE_NAME}", template);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ReadTemplateAsync_LaunchdTemplate_ShouldReturnTemplateContent()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = NewLaunchdManager(testDirectory);

                string template = await manager.ReadTemplateAsync();

                Assert.NotEmpty(template);
                Assert.Contains("<plist", template);
                Assert.Contains("Label", template);
                Assert.Contains("ProgramArguments", template);
                Assert.Contains("KeepAlive", template);
                Assert.Contains("{SERVICE_NAME}", template);
                Assert.Contains("{WORKING_DIRECTORY}", template);
                Assert.Contains("{EXECUTABLE_NAME}", template);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ReadTemplateWithSubstitutionsAsync_SystemdTemplate_ShouldSubstitutePlaceholders()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = NewSystemdManager(testDirectory);
                string serviceName = "test-service";
                string symlinkPath = "/opt/test-service/current";
                string executableName = "test-service";

                string result = await manager.ReadTemplateWithSubstitutionsAsync(serviceName, symlinkPath, executableName);

                Assert.NotEmpty(result);
                Assert.Contains($"Description={serviceName} service managed by updaemon", result);
                Assert.Contains($"WorkingDirectory={symlinkPath}", result);
                Assert.Contains($"ExecStart={symlinkPath}/{executableName}", result);
                Assert.Contains($"SyslogIdentifier={serviceName}", result);
                Assert.DoesNotContain("{SERVICE_NAME}", result);
                Assert.DoesNotContain("{DESCRIPTION}", result);
                Assert.DoesNotContain("{WORKING_DIRECTORY}", result);
                Assert.DoesNotContain("{EXECUTABLE_NAME}", result);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ReadTemplateWithSubstitutionsAsync_LaunchdTemplate_ShouldSubstitutePlaceholders()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = NewLaunchdManager(testDirectory);
                string serviceName = "test-service";
                string symlinkPath = "/usr/local/opt/test-service/current";
                string executableName = "test-service";

                string result = await manager.ReadTemplateWithSubstitutionsAsync(serviceName, symlinkPath, executableName);

                Assert.NotEmpty(result);
                Assert.Contains($"<string>com.updaemon.{serviceName}</string>", result);
                Assert.Contains($"<string>{symlinkPath}</string>", result);
                Assert.Contains($"<string>{symlinkPath}/{executableName}</string>", result);
                Assert.DoesNotContain("{SERVICE_NAME}", result);
                Assert.DoesNotContain("{WORKING_DIRECTORY}", result);
                Assert.DoesNotContain("{EXECUTABLE_NAME}", result);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ReadTemplateWithSubstitutionsAsync_LaunchdTemplate_EscapesXmlMetacharactersInServiceName()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = NewLaunchdManager(testDirectory);

                string result = await manager.ReadTemplateWithSubstitutionsAsync(
                    "weird&name<x>", "/usr/local/opt/x/current", "exec");

                Assert.Contains("weird&amp;name&lt;x&gt;", result);
                Assert.DoesNotContain("weird&name<x>", result);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ReadTemplateWithSubstitutionsAsync_SystemdTemplate_DoesNotEscape()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = NewSystemdManager(testDirectory);

                // systemd unit files take literal values; we should not XML-encode them.
                string result = await manager.ReadTemplateWithSubstitutionsAsync(
                    "name&with&amp", "/path", "exec");

                Assert.Contains("name&with&amp", result);
                Assert.DoesNotContain("&amp;amp", result);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ReadTemplateAsync_ShouldCreateTemplateFileOnFirstCall()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = NewSystemdManager(testDirectory);
                string templatePath = Path.Combine(testDirectory, UnitFilePlatform.Systemd.TemplateFileName);

                Assert.False(File.Exists(templatePath));

                await manager.ReadTemplateAsync();

                Assert.True(File.Exists(templatePath));
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task ReadTemplateAsync_ShouldUseExistingTemplateIfPresent()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                string templatePath = Path.Combine(testDirectory, UnitFilePlatform.Systemd.TemplateFileName);
                string customContent = "[Unit]\nDescription=Custom Template\n";
                await File.WriteAllTextAsync(templatePath, customContent);

                UnitFileManager manager = NewSystemdManager(testDirectory);

                string template = await manager.ReadTemplateAsync();

                Assert.Equal(customContent, template);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        [Fact]
        public void GetUnitFilePath_SystemdManager_ReturnsBareNameWithServiceExtension()
        {
            UnitFileManager manager = NewSystemdManager(Path.GetTempPath());

            string path = manager.GetUnitFilePath("my-service");

            Assert.Equal(Path.Combine(UnitFilePlatform.Systemd.UnitFileDirectory, "my-service.service"), path);
        }

        [Fact]
        public void GetUnitFilePath_LaunchdManager_PrefixesLabelAndUsesPlistExtension()
        {
            UnitFileManager manager = NewLaunchdManager(Path.GetTempPath());

            string path = manager.GetUnitFilePath("my-service");

            Assert.Equal(Path.Combine(UnitFilePlatform.Launchd.UnitFileDirectory, "com.updaemon.my-service.plist"), path);
        }

        [Fact]
        public async Task EnsureWritableAsync_DirectoryDoesNotExist_Throws()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            UnitFilePlatform missing = UnitFilePlatform.Systemd with { UnitFileDirectory = testDirectory };
            UnitFileManager manager = new UnitFileManager(Path.GetTempPath(), missing);

            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.EnsureWritableAsync());
        }

        [Fact]
        public async Task EnsureWritableAsync_DirectoryExistsAndWritable_Succeeds()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFilePlatform writable = UnitFilePlatform.Systemd with { UnitFileDirectory = testDirectory };
                UnitFileManager manager = new UnitFileManager(Path.GetTempPath(), writable);

                await manager.EnsureWritableAsync();
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }
}
