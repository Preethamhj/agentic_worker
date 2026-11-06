namespace Jarvis.TestSupport;

/// <summary>Locates the helper application copied into a test output directory.</summary>
public static class TestApps
{
    /// <summary>Returns the helper apphost path or explains how to add its build reference.</summary>
    public static string Locate()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Jarvis.TestApp.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                $"Jarvis.TestApp.exe was not copied to '{AppContext.BaseDirectory}'. " +
                "Add a Jarvis.TestApp project reference with ReferenceOutputAssembly=false and copy its build output after the test project builds.",
                executable);
        }

        return executable;
    }
}
