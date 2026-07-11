using System.Drawing;
using System.IO;

namespace CodexQuotaRail.App.Tray;

public static class ApplicationIconLoader
{
    public static Icon Load(string? executablePath)
    {
        if (executablePath is { Length: > 0 } path && File.Exists(path))
        {
            using var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted is not null)
            {
                return (Icon)extracted.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
