using Updaemon.Configuration;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Configuration
{
    public class EnvFileTests
    {
        private const string TrickyValue = "a \"quoted\" $HOME \\back `tick` 'single' %p ${X}";

        [Fact]
        public void Parse_ReadsBackWhatFormatWrote()
        {
            string content = EnvFile.Format(new Dictionary<string, string> { ["B"] = TrickyValue, ["A"] = "" });

            Dictionary<string, string> parsed = EnvFile.Parse(content);

            Assert.Equal(TrickyValue, parsed["B"]);
            Assert.Equal("", parsed["A"]);
            Assert.StartsWith("A=", content);
        }

        [Fact]
        public async Task Format_IsReadLiterallyByShell()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string envPath = tempHelper.CreateTempFile("app.env", EnvFile.Format(new Dictionary<string, string> { ["VALUE"] = TrickyValue }));
                string script = tempHelper.CreateTempFile("print.sh", $"set -a\n. '{envPath}'\nprintf '%s' \"$VALUE\"\n");

                string output = await Shell.RunAsync(script);

                Assert.Equal(TrickyValue, output);
            }
        }

        [Fact]
        public void Format_ValueWithLineBreak_Throws()
        {
            Assert.Throws<ArgumentException>(() => EnvFile.Format(new Dictionary<string, string> { ["A"] = "x\ny" }));
        }
    }
}
