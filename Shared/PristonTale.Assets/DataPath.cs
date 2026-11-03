namespace PristonTale.Assets;

// A data file the server or a tool keeps (the database, the certificate): a relative path is taken from the repository
// root when run from a checkout, else from the executable's folder.
public static class DataPath
{
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PristonTale.sln")))
            {
                return Path.GetFullPath(Path.Combine(directory.FullName, path));
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }
}
