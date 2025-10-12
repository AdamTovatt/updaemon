using Updaemon.Services;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class FilePermissionManagerTests
    {
        [Fact]
        public async Task SetExecutablePermissionsAsync_DoesNotThrowException()
        {
            // Arrange
            MockOutputWriter outputWriter = new MockOutputWriter();
            FilePermissionManager filePermissionManager = new FilePermissionManager(outputWriter);
            string testPath = "/tmp/test-executable";

            // Act & Assert - Should not throw
            await filePermissionManager.SetExecutablePermissionsAsync(testPath);
        }

        [Fact]
        public async Task SetDirectoryPermissionsAsync_DoesNotThrowException()
        {
            // Arrange
            MockOutputWriter outputWriter = new MockOutputWriter();
            FilePermissionManager filePermissionManager = new FilePermissionManager(outputWriter);
            string testPath = "/tmp/test-directory";

            // Act & Assert - Should not throw
            await filePermissionManager.SetDirectoryPermissionsAsync(testPath);
        }

        [Fact]
        public async Task SetExecutablePermissionsAsync_WritesOutputMessage()
        {
            // Arrange
            MockOutputWriter outputWriter = new MockOutputWriter();
            FilePermissionManager filePermissionManager = new FilePermissionManager(outputWriter);
            string testPath = "/tmp/test-executable";

            // Act
            await filePermissionManager.SetExecutablePermissionsAsync(testPath);

            // Assert - Should write either success or warning message
            Assert.NotEmpty(outputWriter.Messages);
        }

        [Fact]
        public async Task SetDirectoryPermissionsAsync_WritesOutputMessage()
        {
            // Arrange
            MockOutputWriter outputWriter = new MockOutputWriter();
            FilePermissionManager filePermissionManager = new FilePermissionManager(outputWriter);
            string testPath = "/tmp/test-directory";

            // Act
            await filePermissionManager.SetDirectoryPermissionsAsync(testPath);

            // Assert - Should write either success or warning message
            Assert.NotEmpty(outputWriter.Messages);
        }
    }
}

