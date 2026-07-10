namespace CodexQuotaRail.Windows.Windows;

internal static class CodexWindowIdentityMatcher
{
    private const string CodexExecutableName = "Codex.exe";
    private const string CodexPackageName = "OpenAI.Codex";

    public static bool IsMatch(WindowProcessIdentity? identity)
    {
        if (identity is null)
        {
            return false;
        }

        if (MatchesPackage(identity.PackageFullName))
        {
            return true;
        }

        return Path.GetFileName(identity.ExecutablePath)
                   .Equals(CodexExecutableName, StringComparison.OrdinalIgnoreCase) &&
               HasOpenAiSigner(identity.SignerSubject);
    }

    private static bool MatchesPackage(string? packageFullName)
    {
        if (string.IsNullOrWhiteSpace(packageFullName))
        {
            return false;
        }

        var separator = packageFullName.IndexOf('_');
        var name = separator < 0 ? packageFullName : packageFullName[..separator];
        return name.Equals(CodexPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasOpenAiSigner(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        foreach (var component in subject.Split(','))
        {
            var value = component.Trim();
            if (!value.StartsWith("O=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var organization = value[2..].Trim().Trim('"');
            return organization.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
