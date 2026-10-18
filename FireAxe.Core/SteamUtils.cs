using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ValveKeyValue;

namespace FireAxe;

public static class SteamUtils
{
    public const uint Left4Dead2AppId = 550;

    private const string LocalConfigFileName = "localconfig.vdf";
    private const string LaunchOptionsKey = "LaunchOptions";

    /// <summary>
    /// Finds the Steam installation directory, or null when it can't be determined.
    /// </summary>
    public static string? TryGetSteamDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var localMachineRegistry = Registry.LocalMachine;
                var currentUserRegistry = Registry.CurrentUser;
                using var key = currentUserRegistry.OpenSubKey(@"SOFTWARE\Valve\Steam")
                    ?? localMachineRegistry.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                    ?? localMachineRegistry.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key is not null)
                {
                    // Newer Steam builds store "SteamPath"; older ones "InstallPath".
                    foreach (var valueName in new[] { "SteamPath", "InstallPath" })
                    {
                        if (key.GetValue(valueName)?.ToString() is { Length: > 0 } path
                            && FileSystemUtils.IsValidPath(path)
                            && Directory.Exists(path))
                        {
                            return FileSystemUtils.NormalizePath(path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception occurred during reading the Steam registry key.");
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the launch options the user configured for the app in Steam's properties dialog.
    /// Returns null when they can't be determined. Returns an empty string when the user set none.
    /// </summary>
    public static string? TryGetLaunchOptions(uint appId)
    {
        var steamDirectory = TryGetSteamDirectory();
        if (steamDirectory is null)
        {
            Log.Warning("The Steam directory was not found, so the launch options can't be read.");
            return null;
        }

        var userDataDirectory = Path.Join(steamDirectory, "userdata");
        IEnumerable<string> userDirectories;
        try
        {
            if (!Directory.Exists(userDataDirectory))
            {
                return null;
            }
            userDirectories = Directory.EnumerateDirectories(userDataDirectory);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception occurred during enumerating the Steam userdata directory.");
            return null;
        }

        // Multiple accounts may exist; prefer whichever one actually configured launch options.
        string? emptyResult = null;
        foreach (var userDirectory in userDirectories)
        {
            var configFilePath = Path.Join(userDirectory, "config", LocalConfigFileName);
            if (!File.Exists(configFilePath))
            {
                continue;
            }

            var launchOptions = TryReadLaunchOptionsFromFile(configFilePath, appId);
            if (launchOptions is null)
            {
                continue;
            }
            if (launchOptions.Length > 0)
            {
                return launchOptions;
            }
            emptyResult ??= launchOptions;
        }

        return emptyResult;
    }

    private static string? TryReadLaunchOptionsFromFile(string configFilePath, uint appId)
    {
        KVObject? root;
        try
        {
            using var stream = File.OpenRead(configFilePath);
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            root = serializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception occurred during deserializing the Steam config file: {FilePath}", configFilePath);
            return null;
        }

        if (root is null)
        {
            return null;
        }

        // The deserialized root already is UserLocalConfigStore, so start below it.
        // Path: Software -> Valve -> Steam -> apps -> <appId> -> LaunchOptions
        var appNode = root;
        foreach (var name in new[] { "Software", "Valve", "Steam", "apps" })
        {
            appNode = TryGetChild(appNode, name);
        }

        var appObject = TryGetChild(appNode, appId.ToString(CultureInfo.InvariantCulture));
        var launchOptionsValue = TryGetChild(appObject, LaunchOptionsKey);
        if (launchOptionsValue is null)
        {
            return null;
        }

        return launchOptionsValue.Value.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private static KVObject? TryGetChild(KVObject? obj, string name)
    {
        if (obj is null)
        {
            return null;
        }

        foreach (var child in obj.Children)
        {
            if (child is not null && string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the path of the game executable inside the given game directory.
    /// </summary>
    public static string GetGameExecutablePath(string gamePath)
    {
        ArgumentNullException.ThrowIfNull(gamePath);

        return Path.Join(gamePath, "left4dead2.exe");
    }

    /// <summary>
    /// Splits a raw launch options string into individual arguments, honouring double quotes.
    /// </summary>
    public static IReadOnlyList<string> ParseArgumentString(string argumentString)
    {
        ArgumentNullException.ThrowIfNull(argumentString);

        var arguments = new List<string>();
        var currentArgument = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool hasContent = false;

        foreach (char c in argumentString)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasContent = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasContent)
                {
                    arguments.Add(currentArgument.ToString());
                    currentArgument.Clear();
                    hasContent = false;
                }
            }
            else
            {
                currentArgument.Append(c);
                hasContent = true;
            }
        }

        if (hasContent)
        {
            arguments.Add(currentArgument.ToString());
        }

        return arguments;
    }
}
