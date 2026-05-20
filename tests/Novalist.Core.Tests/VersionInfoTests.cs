using System.Reflection;
using System.Reflection.Emit;
using Novalist.Core;
using Xunit;

namespace Novalist.Core.Tests;

public class VersionInfoTests
{
    [Fact]
    public void Version_IsNonEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(VersionInfo.Version));

    [Fact]
    public void ReadVersion_WithInformationalVersion_StripsGitHash()
    {
        // System.Private.CoreLib's InformationalVersion includes a "+<commit>" suffix.
        var result = VersionInfo.ReadVersion(typeof(object).Assembly);
        Assert.DoesNotContain("+", result);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void ReadVersion_NoAttribute_UsesAssemblyNameVersion()
    {
        var an = new AssemblyName("DynVersioned") { Version = new Version(2, 3, 4) };
        var ab = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
        Assert.Equal("2.3.4", VersionInfo.ReadVersion(ab));
    }

    [Fact]
    public void ReadVersion_NoAttributeNoVersion_FallsBackToZeros()
    {
        var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DynNoVersion"), AssemblyBuilderAccess.Run);
        Assert.Equal("0.0.0", VersionInfo.ReadVersion(ab));
    }

    [Fact]
    public void IsDev_MatchesVersionSuffix()
        => Assert.Equal(VersionInfo.Version.Contains("-dev"), VersionInfo.IsDev);

    [Fact]
    public void IsCompatibleWith_BlankMinVersion_True()
        => Assert.True(VersionInfo.IsCompatibleWith("  "));

    [Fact]
    public void IsCompatibleWith_OlderRequirement_True()
        => Assert.True(VersionInfo.IsCompatibleWith("0.0.0"));

    [Fact]
    public void IsCompatibleWith_FutureRequirement_False()
        => Assert.False(VersionInfo.IsCompatibleWith("9999.0.0"));

    [Fact]
    public void IsCompatibleWith_EqualVersion_True()
        => Assert.True(VersionInfo.IsCompatibleWith(VersionInfo.Version));

    [Fact]
    public void IsCompatibleWith_IgnoresPreReleaseSuffix()
        => Assert.True(VersionInfo.IsCompatibleWith("0.0.1-beta.1"));

    [Fact]
    public void IsCompatibleWith_StripsGitHashSuffix()
        => Assert.True(VersionInfo.IsCompatibleWith("0.0.0+abc123"));

    [Fact]
    public void IsCompatibleWith_NonNumericParts_TreatedAsZero()
        => Assert.True(VersionInfo.IsCompatibleWith("x.y.z"));

    [Fact]
    public void IsCompatibleWith_HandlesPatchLevelDifference()
    {
        // host is at least 0.0.x; a 0.0.0 requirement passes, a far-future patch fails.
        Assert.True(VersionInfo.IsCompatibleWith("0.0"));
        Assert.False(VersionInfo.IsCompatibleWith("9999.9999.9999"));
    }
}
