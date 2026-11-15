namespace Updaemon.Distribution.ByteShelfDistribution.Tests
{
    public class ByteShelfRemoteTests
    {
        private const string Form = "<shelf>/<app>, or <shelf>/<app>/<pattern> to pick the asset";

        [Theory]
        [InlineData("tools/myapp", null)]
        [InlineData("/tools/myapp/*-x64.zip/", "*-x64.zip")]
        public void Parse_ShelfAndApp_NamesTheAppsDirectory(string remote, string? pattern)
        {
            ByteShelfRemote parsed = ByteShelfRemote.Parse(remote);

            Assert.Equal("tools", parsed.Shelf);
            Assert.Equal("/myapp", parsed.AppDirectory);
            Assert.Equal(pattern, parsed.Pattern);
            Assert.Equal("tools/myapp", parsed.ToString());
        }

        [Theory]
        [InlineData("myapp", "'myapp' names no shelf: remotes are <shelf>/<app> since ByteShelf 2.0. Run 'updaemon remove <app>', then 'updaemon install <app> --from byteshelf:<shelf>/<app>'.")]
        [InlineData("tools/myapp/x/y", $"'tools/myapp/x/y' is not a ByteShelf remote: use {Form}.")]
        [InlineData("tools//x", $"'tools//x' is not a ByteShelf remote: use {Form}.")]
        [InlineData("MyTools/myapp", $"'MyTools/myapp' is not a ByteShelf remote ({Form}): A shelf name starts with a lowercase letter or a digit.")]
        [InlineData("tools/..", $"'tools/..' is not a ByteShelf remote ({Form}): the app is not a directory name: '.' and '..' are not names.")]
        public void Parse_InNeitherForm_IsRefused(string remote, string expected)
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() => ByteShelfRemote.Parse(remote));

            Assert.Equal(expected, ex.Message);
        }
    }
}
