using System.Security;
using Microsoft.Win32;

namespace GgufLauncher.Shell;

/// <summary>
/// Owns the per-user Windows file association for the <c>.gguf</c> extension.
/// </summary>
/// <remarks>
/// Every key is written below <c>HKEY_CURRENT_USER</c>, so registering never needs administrator
/// rights. The <c>UserChoice</c> key is never touched: it is protected by the User Choice
/// Protection Driver and writing it is unsupported.
/// </remarks>
public sealed class FileAssociation
{
    /// <summary>Extension the launcher claims.</summary>
    public const string DefaultExtension = ".gguf";

    /// <summary>ProgID the launcher claims for the extension by default.</summary>
    public const string DefaultProgId = "GgufLauncher.GGUF";

    /// <summary>Capabilities key referenced from the registered-applications list.</summary>
    public const string DefaultCapabilitiesPath = @"SOFTWARE\GgufLauncher\Capabilities";

    private const string ClassesPath = @"Software\Classes";
    private const string RegisteredApplicationsPath = @"SOFTWARE\RegisteredApplications";
    private const string UserChoicePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
    private const string ApplicationName = "GgufLauncher";

    /// <summary>Describes the association that should be written to the registry.</summary>
    /// <param name="progId">ProgID written next to the extension, for example <c>GgufLauncher.GGUF</c>.</param>
    /// <param name="executablePath">Full path of the launcher executable that opens a model.</param>
    /// <param name="exeFileName">File name of the executable; defaults to the file name of <paramref name="executablePath"/>.</param>
    /// <param name="capabilitiesPath">Capabilities key path; defaults to <see cref="DefaultCapabilitiesPath"/>.</param>
    public FileAssociation(string progId, string executablePath, string? exeFileName = null, string? capabilitiesPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ProgId = progId;
        ExecutablePath = executablePath;
        ExecutableFileName = string.IsNullOrWhiteSpace(exeFileName) ? Path.GetFileName(executablePath) : exeFileName;
        CapabilitiesPath = string.IsNullOrWhiteSpace(capabilitiesPath) ? DefaultCapabilitiesPath : capabilitiesPath;
        if (string.IsNullOrWhiteSpace(ExecutableFileName))
        {
            throw new ArgumentException("The executable path must contain a file name.", nameof(executablePath));
        }
    }

    /// <summary>ProgID that identifies the launcher inside the registry.</summary>
    public string ProgId { get; }

    /// <summary>Full path of the executable Windows starts for a double-clicked model.</summary>
    public string ExecutablePath { get; }

    /// <summary>File name of the executable, used for the per-user <c>Applications</c> key.</summary>
    public string ExecutableFileName { get; }

    /// <summary>Extension handled by this association; always starts with a dot.</summary>
    public string Extension { get; init; } = DefaultExtension;

    /// <summary>Capabilities key path relative to <c>HKEY_CURRENT_USER</c>.</summary>
    public string CapabilitiesPath { get; }

    /// <summary>Path of the extension key relative to <c>HKEY_CURRENT_USER</c>.</summary>
    public string ExtensionKeyPath => $@"{ClassesPath}\{Extension}";

    /// <summary>Path of the ProgID key relative to <c>HKEY_CURRENT_USER</c>.</summary>
    public string ProgIdKeyPath => $@"{ClassesPath}\{ProgId}";

    /// <summary>Path of the per-user application key relative to <c>HKEY_CURRENT_USER</c>.</summary>
    public string ApplicationsKeyPath => $@"{ClassesPath}\Applications\{ExecutableFileName}";

    /// <summary>
    /// Creates or refreshes every registry key that binds the extension to the launcher.
    /// </summary>
    /// <remarks>Calling this twice is harmless: existing values are simply overwritten.</remarks>
    public void Register()
    {
        try
        {
            WriteRegistryKeys();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"could not write the .gguf file association: {ex.Message}", ex);
        }

        ShellNotify.NotifyAssociationsChanged();
    }

    /// <summary>
    /// Removes every key this association created and leaves the extension key in a sane state.
    /// </summary>
    /// <remarks>
    /// Calling this when nothing is registered is harmless. When another application already owns
    /// the extension, only this launcher's own entries are removed.
    /// </remarks>
    public void Unregister()
    {
        try
        {
            DeleteSubTree(ProgIdKeyPath);
            DeleteSubTree(ApplicationsKeyPath);
            DeleteSubTree(CapabilitiesPath);
            DeleteEmptyParent(CapabilitiesPath);
            RemoveRegisteredApplication();
            CleanExtensionKey();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"could not remove the .gguf file association: {ex.Message}", ex);
        }

        ShellNotify.NotifyAssociationsChanged();
    }

    /// <summary>Resolves how an extension is associated right now without changing anything.</summary>
    /// <param name="extension">Extension to inspect, with or without the leading dot.</param>
    /// <param name="ourProgId">ProgID to treat as the launcher's own; defaults to <see cref="DefaultProgId"/>.</param>
    /// <returns>A snapshot describing the effective handler.</returns>
    public static FileAssociationStatus Query(string extension, string? ourProgId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.Trim();
        if (!normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = "." + normalized;
        }
        (string? ProgId, string? Source) resolution = ResolveProgId(normalized);
        string? command = resolution.ProgId is null ? null : ReadOpenCommand(resolution.ProgId);
        string expected = ourProgId ?? DefaultProgId;
        bool isOurs = resolution.ProgId is not null
            && string.Equals(resolution.ProgId, expected, StringComparison.OrdinalIgnoreCase);
        return new FileAssociationStatus
        {
            Extension = normalized,
            ProgId = resolution.ProgId,
            OpenCommand = command,
            IsOurProgId = isOurs,
            ProgIdSource = resolution.Source,
        };
    }

    private string OpenCommand => $"\"{ExecutablePath}\" \"%1\"";

    private string IconReference => $"\"{ExecutablePath}\",0";

    /// <summary>Writes the extension key, the ProgID key, the application key and the capabilities key.</summary>
    private void WriteRegistryKeys()
    {
        using RegistryKey extensionKey = Registry.CurrentUser.CreateSubKey(ExtensionKeyPath, writable: true);
        extensionKey.SetValue("", ProgId, RegistryValueKind.String);
        extensionKey.SetValue("Content Type", "application/octet-stream", RegistryValueKind.String);
        extensionKey.SetValue("PerceivedType", "document", RegistryValueKind.String);
        SetValue(extensionKey, "OpenWithProgids", ProgId, (Array.Empty<byte>(), RegistryValueKind.None));
        using RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey(ProgIdKeyPath, writable: true);
        progIdKey.SetValue("", "GGUF Model", RegistryValueKind.String);
        progIdKey.SetValue("FriendlyTypeName", "GGUF Model", RegistryValueKind.String);
        SetValue(progIdKey, "DefaultIcon", "", (IconReference, RegistryValueKind.String));
        using RegistryKey progIdOpen = progIdKey.CreateSubKey(@"shell\open", writable: true);
        progIdOpen.SetValue("FriendlyName", "Open with GGUF Launcher", RegistryValueKind.String);
        progIdOpen.SetValue("Icon", IconReference, RegistryValueKind.String);
        SetValue(progIdOpen, "command", "", (OpenCommand, RegistryValueKind.String));
        using RegistryKey applicationKey = Registry.CurrentUser.CreateSubKey(ApplicationsKeyPath, writable: true);
        applicationKey.SetValue("FriendlyAppName", "GGUF Launcher", RegistryValueKind.String);
        SetValue(applicationKey, "DefaultIcon", "", (IconReference, RegistryValueKind.String));
        SetValue(applicationKey, "SupportedTypes", Extension, (Array.Empty<byte>(), RegistryValueKind.None));
        using RegistryKey applicationOpen = applicationKey.CreateSubKey(@"shell\open", writable: true);
        SetValue(applicationOpen, "command", "", (OpenCommand, RegistryValueKind.String));
        using RegistryKey capabilitiesKey = Registry.CurrentUser.CreateSubKey(CapabilitiesPath, writable: true);
        capabilitiesKey.SetValue("ApplicationDescription", "Start a llama.cpp server for a GGUF model", RegistryValueKind.String);
        capabilitiesKey.SetValue("ApplicationName", ApplicationName, RegistryValueKind.String);
        capabilitiesKey.SetValue("ApplicationIcon", IconReference, RegistryValueKind.String);
        using RegistryKey associations = capabilitiesKey.CreateSubKey("FileAssociations", writable: true);
        associations.SetValue(Extension, ProgId, RegistryValueKind.String);
        using RegistryKey registeredKey = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsPath, writable: true);
        registeredKey.SetValue(ApplicationName, CapabilitiesPath, RegistryValueKind.String);
    }

    /// <summary>Creates a subkey below the parent key and stores one value in it.</summary>
    private static void SetValue(RegistryKey parent, string subKeyPath, string valueName, (object Value, RegistryValueKind Kind) entry)
    {
        using RegistryKey sub = parent.CreateSubKey(subKeyPath, writable: true);
        sub.SetValue(valueName, entry.Value, entry.Kind);
    }

    private static void DeleteSubTree(string keyPath)
    {
        using RegistryKey root = Registry.CurrentUser;
        root.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    /// <summary>Deletes the parent of a key, but only when it is completely empty.</summary>
    private static void DeleteEmptyParent(string keyPath)
    {
        int separator = keyPath.LastIndexOf('\\');
        if (separator <= 0)
        {
            return;
        }

        string parentPath = keyPath[..separator];
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(parentPath);
        if (parent is not null && parent.GetSubKeyNames().Length == 0 && parent.GetValueNames().Length == 0)
        {
            using RegistryKey root = Registry.CurrentUser;
            root.DeleteSubKey(parentPath, throwOnMissingSubKey: false);
        }
    }

    private void RemoveRegisteredApplication()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsPath, writable: true);
        if (key is not null && key.GetValueNames().Contains(ApplicationName, StringComparer.OrdinalIgnoreCase))
        {
            key.DeleteValue(ApplicationName, throwOnMissingValue: false);
        }
    }

    /// <summary>Removes only the entries this launcher wrote and leaves the rest of the extension key alone.</summary>
    private void CleanExtensionKey()
    {
        using RegistryKey? extensionKey = Registry.CurrentUser.OpenSubKey(ExtensionKeyPath, writable: true);
        if (extensionKey is null)
        {
            return;
        }

        if (string.Equals(extensionKey.GetValue("") as string, ProgId, StringComparison.OrdinalIgnoreCase))
        {
            extensionKey.DeleteValue("", throwOnMissingValue: false);
        }

        using RegistryKey? openWith = extensionKey.OpenSubKey("OpenWithProgids", writable: true);
        if (openWith is null)
        {
            return;
        }

        openWith.DeleteValue(ProgId, throwOnMissingValue: false);
        if (openWith.GetValueNames().Length == 0 && openWith.GetSubKeyNames().Length == 0)
        {
            extensionKey.DeleteSubKey("OpenWithProgids", throwOnMissingSubKey: false);
        }
    }

    private static (string? ProgId, string? Source) ResolveProgId(string extension)
    {
        string? progId = ReadString(Registry.CurrentUser, $@"{UserChoicePath}\{extension}\UserChoice", "ProgId");
        if (progId is not null)
        {
            return (progId, "UserChoice");
        }

        progId = ReadString(Registry.CurrentUser, $@"{ClassesPath}\{extension}");
        if (progId is not null)
        {
            return (progId, "CurrentUser");
        }

        progId = ReadString(Registry.LocalMachine, $@"{ClassesPath}\{extension}");
        return progId is null ? (null, null) : (progId, "LocalMachine");
    }

    private static string? ReadOpenCommand(string progId)
    {
        string? command = ReadString(Registry.CurrentUser, $@"{ClassesPath}\{progId}\shell\open\command");
        return command ?? ReadString(Registry.LocalMachine, $@"{ClassesPath}\{progId}\shell\open\command");
    }

    /// <summary>Reads a string value below the given root; a missing key simply yields null.</summary>
    private static string? ReadString(RegistryKey root, string keyPath, string valueName = "")
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(keyPath);
            string? value = key?.GetValue(valueName) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"could not read the .gguf file association: {ex.Message}", ex);
        }
    }
}
