[assembly: AssemblyFixture(typeof(Steward.Core.Tests.TempFolderFixture))]

namespace Steward.Core.Tests;

// The test processes of each target framework run at once, and AppUpdater writes a fixed file name under the temp folder.
public sealed class TempFolderFixture : IDisposable
{
    private static readonly string[] Variables = ["TMP", "TEMP", "TMPDIR"];

    private readonly string _folder = Directory.CreateTempSubdirectory("steward-core-tests-").FullName;

    public TempFolderFixture()
    {
        foreach (var variable in Variables)
        {
            Environment.SetEnvironmentVariable(variable, _folder);
        }
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
