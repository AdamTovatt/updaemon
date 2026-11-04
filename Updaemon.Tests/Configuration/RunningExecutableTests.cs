using Updaemon.Configuration;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Configuration
{
    /// <summary>
    /// The running file is given fully resolved, as the OS reports it; the command path may lead to it through links.
    /// </summary>
    public class RunningExecutableTests : IDisposable
    {
        private readonly TempFileHelper _temp = new TempFileHelper();
        private readonly string _binary;

        public RunningExecutableTests()
        {
            _binary = _temp.CreateTempFile("real/versions/1.0.0/updaemon", "");
        }

        public void Dispose() => _temp.Dispose();

        private RunningExecutable Running => new RunningExecutable(_binary);

        private string Link(string relativePath, string target)
        {
            string path = _temp.GetFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.CreateSymbolicLink(path, target);
            return path;
        }

        [Fact]
        public void IsReachedThrough_LinkToTheFile_IsTrue()
        {
            Assert.True(Running.IsReachedThrough(Link("bin/updaemon", _binary)));
        }

        [Fact]
        public void IsReachedThrough_ChainOfLinks_IsTrue()
        {
            string first = Link("bin/first", _binary);

            Assert.True(Running.IsReachedThrough(Link("bin/updaemon", first)));
        }

        [Fact]
        public void IsReachedThrough_TheFileItself_IsTrue()
        {
            Assert.True(Running.IsReachedThrough(_binary));
        }

        [Fact]
        public void IsReachedThrough_LinkThroughALinkedDirectory_IsTrue()
        {
            // As when the scope root is given through a link, such as a linked /home.
            string linkedRoot = Link("alias", _temp.GetFullPath("real"));

            Assert.True(Running.IsReachedThrough(Link("bin/updaemon", Path.Combine(linkedRoot, "versions", "1.0.0", "updaemon"))));
        }

        [Fact]
        public void IsReachedThrough_MissingCommand_IsFalse()
        {
            Assert.False(Running.IsReachedThrough(_temp.GetFullPath("bin/updaemon")));
        }

        [Fact]
        public void IsReachedThrough_AnotherFile_IsFalse()
        {
            Assert.False(Running.IsReachedThrough(_temp.CreateTempFile("bin/updaemon", "")));
        }
    }
}
