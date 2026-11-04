using System.Runtime.CompilerServices;

namespace ProjectDelve.Web.Tests;

// Hosts without an explicit Workspace configuration must never read or edit a
// developer's application-local authoring content. Per-test command-line paths
// retain precedence over this process-local default.
internal static class TestWorkspaceIsolation
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "delve-web-test-workspace-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ContentWorkspace__Path", Path.Combine(directory, "workspace"));
        Environment.SetEnvironmentVariable("CanonicalContent__Directory", Path.Combine(directory, "canonical"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
