using System.Runtime.InteropServices;

using Microsoft.Win32;

using Steward.Core;

namespace Steward.App.Services;

public static class CurseForgeDefaultQuery
{
    private const int UrlProtocol = 1;
    private const int Effective = 1;

    public static string? Aumid => App.IsPackaged ? Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App" : null;

    public static bool IsDefault(string aumid)
    {
        var registration = (IApplicationAssociationRegistration)new ApplicationAssociationRegistration();
        var hr = registration.QueryCurrentDefault("curseforge", UrlProtocol, Effective, out var progId);
        return hr >= 0 && CurseForgeDefaultHandler.IsOurs(progId, aumid, LookupAumid);
    }

    private static string? LookupAumid(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(progId + @"\Application");
        return key?.GetValue("AppUserModelID") as string;
    }

    [ComImport, Guid("591209c7-767b-42b2-9fba-44ee4615f2c7")]
    private class ApplicationAssociationRegistration;

    [ComImport, Guid("4e530b0a-e611-4c77-a3ac-9031d022281b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistration
    {
        [PreserveSig]
        int QueryCurrentDefault([MarshalAs(UnmanagedType.LPWStr)] string query, int queryType, int queryLevel, [MarshalAs(UnmanagedType.LPWStr)] out string association);
    }
}
