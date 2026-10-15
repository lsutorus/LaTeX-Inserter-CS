using LaTeXInserter.Models;
using LaTeXInserter.Services;
using Xunit;

namespace LaTeXInserter.Tests;

public class AppDataPathProviderTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "latexinserter-test-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void MacOs_UsesLibraryApplicationSupport()
    {
        var home = TempRoot();
        var sut = new DefaultAppDataPathProvider(PlatformKind.MacOS, home);

        var expectedSuffix = "Library/Application Support/LaTeX Inserter";
        var actual = sut.GetAppDataDirectory().Replace('\\', '/');

        Assert.EndsWith(expectedSuffix, actual);
        Assert.True(Directory.Exists(sut.GetAppDataDirectory()));
    }

    [Fact]
    public void Windows_UsesRoamingAppData()
    {
        var appData = TempRoot();
        var sut = new DefaultAppDataPathProvider(PlatformKind.Windows, appData);

        var expectedSuffix = "LaTeX Inserter";
        var actual = sut.GetAppDataDirectory().Replace('\\', '/');

        Assert.EndsWith(expectedSuffix, actual);
        Assert.True(Directory.Exists(sut.GetAppDataDirectory()));
    }
}
