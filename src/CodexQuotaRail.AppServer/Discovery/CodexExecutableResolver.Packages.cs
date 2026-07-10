namespace CodexQuotaRail.AppServer.Discovery;

public sealed partial class CodexExecutableResolver
{
    private string[] GetOfficialPackageCandidates()
    {
        var candidates = new List<string>();
        foreach (var package in _probe.RegisteredPackages)
        {
            if (!package.IdentityName.Equals(
                OfficialPackageIdentity,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var root = _probe.GetCanonicalPath(package.InstallLocation);
            if (root is null)
            {
                continue;
            }

            var expectedPath = Path.Combine(root, "app", "resources", "codex.exe");
            var canonicalPath = _probe.GetCanonicalPath(expectedPath);
            if (canonicalPath is not null && IsWithinRoot(canonicalPath, root))
            {
                candidates.Add(canonicalPath);
            }
        }

        return [.. candidates.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
            !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
