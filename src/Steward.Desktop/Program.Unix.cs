using Steward.App.Services;
using Steward.Desktop.Services;

namespace Steward.Desktop;

public static partial class Program
{
    static partial void ConfigurePlatform()
    {
        if (OperatingSystem.IsLinux())
        {
            SecretProtection.SecretService = new SecretServiceKeyring();
        }
    }
}
