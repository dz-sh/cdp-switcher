using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CdpSwitcher.Core.Chrome;

public sealed class ChromeLocator
{
    public string? FindChrome()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var candidate in RegistryCandidates().Concat(FileCandidates()))
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string?> RegistryCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        const string suffix =
            @"\Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";

        yield return ReadRegistryDefaultValue(
            RegistryHive.CurrentUser,
            suffix);
        yield return ReadRegistryDefaultValue(
            RegistryHive.LocalMachine,
            suffix,
            RegistryView.Registry64);
        yield return ReadRegistryDefaultValue(
            RegistryHive.LocalMachine,
            suffix,
            RegistryView.Registry32);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryDefaultValue(
        RegistryHive hive,
        string subKey,
        RegistryView view = RegistryView.Default)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);

            return key?.GetValue(null) as string;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static IEnumerable<string> FileCandidates()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);

        yield return Path.Combine(
            localAppData,
            "Google",
            "Chrome",
            "Application",
            "chrome.exe");
        yield return Path.Combine(
            programFiles,
            "Google",
            "Chrome",
            "Application",
            "chrome.exe");
        yield return Path.Combine(
            programFilesX86,
            "Google",
            "Chrome",
            "Application",
            "chrome.exe");
    }
}
