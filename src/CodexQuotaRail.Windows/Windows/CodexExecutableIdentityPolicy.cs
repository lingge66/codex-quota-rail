namespace CodexQuotaRail.Windows.Windows;

public static class CodexExecutableIdentityPolicy
{
    public static bool RequiresSignerLookup(string executablePath, string? packageFullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return packageFullName is null &&
            Path.GetFileName(executablePath)
                .Equals("Codex.exe", StringComparison.OrdinalIgnoreCase);
    }
}
