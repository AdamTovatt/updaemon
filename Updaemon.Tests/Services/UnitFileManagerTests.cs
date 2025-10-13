using Updaemon.Services;

namespace Updaemon.Tests.Services
{
    public class UnitFileManagerTests
    {
        [Fact]
        public async Task ReadTemplateAsync_ShouldReturnTemplateContent()
        {
            // Arrange
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = new UnitFileManager(testDirectory);

                // Act
                string template = await manager.ReadTemplateAsync();

                // Assert
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
        public async Task ReadTemplateWithSubstitutionsAsync_ShouldSubstitutePlaceholders()
        {
            // Arrange
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = new UnitFileManager(testDirectory);
                string serviceName = "test-service";
                string symlinkPath = "/opt/test-service/current";
                string executableName = "test-service";

                // Act
                string result = await manager.ReadTemplateWithSubstitutionsAsync(serviceName, symlinkPath, executableName);

                // Assert
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
        public async Task ReadTemplateAsync_ShouldCreateTemplateFileOnFirstCall()
        {
            // Arrange
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                UnitFileManager manager = new UnitFileManager(testDirectory);
                string templatePath = Path.Combine(testDirectory, "default-unit.template");

                // Assert template doesn't exist yet
                Assert.False(File.Exists(templatePath));

                // Act
                await manager.ReadTemplateAsync();

                // Assert
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
            // Arrange
            string testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDirectory);

            try
            {
                string templatePath = Path.Combine(testDirectory, "default-unit.template");
                string customContent = "[Unit]\nDescription=Custom Template\n";
                await File.WriteAllTextAsync(templatePath, customContent);

                UnitFileManager manager = new UnitFileManager(testDirectory);

                // Act
                string template = await manager.ReadTemplateAsync();

                // Assert
                Assert.Equal(customContent, template);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }
}

