using Updaemon.Common.Tests.Helpers;
using Updaemon.Common.Utilities;

namespace Updaemon.Common.Tests
{
    public class Sha256SumsTests
    {
        // sha256 of "hello\n", as sha256sum prints it.
        private const string HelloHash = "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03";

        private static async Task VerifyAsync(string sums, string content, string assetName = "app-linux-arm64")
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string file = temp.CreateTempFile("download", content);
                await Sha256Sums.Parse(sums).VerifyAsync(file, assetName);
            }
        }

        [Theory]
        [InlineData($"{HelloHash}  app-linux-arm64\n")]
        [InlineData($"{HelloHash} *app-linux-arm64\r\n")]
        [InlineData($"{HelloHash}  dist/app-linux-arm64\n")]
        [InlineData($"0000000000000000000000000000000000000000000000000000000000000000  other\n{HelloHash}  app-linux-arm64")]
        [InlineData($"# checksums\nSHA256 (other) = 0000\n\\0000  odd\\\\name\n{HelloHash}  app-linux-arm64\n")]
        public async Task VerifyAsync_FileMatchesItsLine_Passes(string sums)
        {
            await VerifyAsync(sums, "hello\n");
        }

        [Fact]
        public async Task VerifyAsync_UppercaseHash_Passes()
        {
            await VerifyAsync($"{HelloHash.ToUpperInvariant()}  app-linux-arm64\n", "hello\n");
        }

        [Fact]
        public async Task VerifyAsync_FileDiffers_IsRefused()
        {
            ChecksumException exception = await Assert.ThrowsAsync<ChecksumException>(() => VerifyAsync($"{HelloHash}  app-linux-arm64\n", "tampered\n"));

            Assert.Equal("app-linux-arm64 does not match its SHA-256 in SHA256SUMS; the download is corrupt or was tampered with.", exception.Message);
        }

        [Theory]
        [InlineData($"{HelloHash}  app-osx-arm64\n")]
        [InlineData($"{HelloHash} app-linux-arm64\n")]
        [InlineData("zz91b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03  app-linux-arm64\n")]
        [InlineData("5891b5b522  app-linux-arm64\n")]
        public async Task VerifyAsync_NoWellFormedLineForTheAsset_IsRefused(string sums)
        {
            ChecksumException exception = await Assert.ThrowsAsync<ChecksumException>(() => VerifyAsync(sums, "hello\n"));

            Assert.Equal("SHA256SUMS has no '<sha-256>  app-linux-arm64' line, so app-linux-arm64 cannot be verified.", exception.Message);
        }

        [Fact]
        public async Task VerifyAsync_LinesThatDisagreeAboutTheAsset_AreRefused()
        {
            string sums = $"{HelloHash}  linux/app-linux-arm64\n{new string('0', 64)}  other/app-linux-arm64\n";

            ChecksumException exception = await Assert.ThrowsAsync<ChecksumException>(() => VerifyAsync(sums, "hello\n"));

            Assert.Equal("SHA256SUMS has lines with different SHA-256s for app-linux-arm64, so it cannot be verified.", exception.Message);
        }
    }
}
