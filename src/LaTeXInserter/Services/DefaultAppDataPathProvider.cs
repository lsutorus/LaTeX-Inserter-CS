using System.IO;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

/// <summary>
/// Environment.SpecialFolder.ApplicationData maps to ~/.config on macOS on several
/// .NET/macOS combinations, which is not where a Mac app is expected to store data.
/// The macOS branch therefore builds ~/Library/Application Support explicitly.
/// </summary>
public sealed class DefaultAppDataPathProvider : IAppDataPathProvider
{
    private const string AppFolderName = "LaTeX Inserter";

    private readonly PlatformKind _platform;
    private readonly string _root;

    public DefaultAppDataPathProvider()
        : this(PlatformKinds.Current, ResolveRoot(PlatformKinds.Current))
    {
    }

    public DefaultAppDataPathProvider(PlatformKind platform, string root)
    {
        _platform = platform;
        _root = root;
    }

    public string GetAppDataDirectory()
    {
        var dir = _platform switch
        {
            PlatformKind.MacOS => Path.Combine(_root, "Library", "Application Support", AppFolderName),
            _ => Path.Combine(_root, AppFolderName)
        };
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ResolveRoot(PlatformKind platform) => platform switch
    {
        PlatformKind.MacOS => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        _ => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
    };
}
