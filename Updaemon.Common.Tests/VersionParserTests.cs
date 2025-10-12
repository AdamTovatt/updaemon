using Updaemon.Common.Utilities;

namespace Updaemon.GithubDistributionService.Tests.Services
{
    public class VersionParserTests
    {
        [Fact]
        public void Parse_SimpleVersionWithV_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("v1.2.3");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void Parse_UnderscoreSeparators_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("curl-8_16_0");

            Assert.NotNull(result);
            Assert.Equal(new Version(8, 16, 0), result);
        }

        [Fact]
        public void Parse_WithPrefixAndSuffix_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("release-2.0.1-beta");

            Assert.NotNull(result);
            Assert.Equal(new Version(2, 0, 1), result);
        }

        [Fact]
        public void Parse_ComplexPrefix_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("version-3.14.159");

            Assert.NotNull(result);
            Assert.Equal(new Version(3, 14, 159), result);
        }

        [Fact]
        public void Parse_OnlyNumbers_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("1.0.0");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 0, 0), result);
        }

        [Fact]
        public void Parse_TwoPartVersion_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("v2.5");

            Assert.NotNull(result);
            Assert.Equal(new Version(2, 5), result);
        }

        [Fact]
        public void Parse_FourPartVersion_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("v1.2.3.4");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3, 4), result);
        }

        [Fact]
        public void Parse_EmptyString_ReturnsNull()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("");

            Assert.Null(result);
        }

        [Fact]
        public void Parse_WhitespaceOnly_ReturnsNull()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("   ");

            Assert.Null(result);
        }

        [Fact]
        public void Parse_NoNumbers_ReturnsNull()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("release-beta");

            Assert.Null(result);
        }

        [Fact]
        public void Parse_OnlyLetters_ReturnsNull()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("abc");

            Assert.Null(result);
        }

        [Fact]
        public void Parse_MixedSeparators_ReturnsVersion()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("app-1_2.3");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void Parse_TrailingNumbers_IncludesAll()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("v1.2.3.456");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3, 456), result);
        }

        [Fact]
        public void Parse_NumbersWithText_ExtractsNumbers()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("release1alpha2beta3");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void Parse_LeadingZeros_PreservesValue()
        {
            VersionParser parser = new VersionParser();

            Version? result = parser.Parse("v01.02.03");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }
    }
}

