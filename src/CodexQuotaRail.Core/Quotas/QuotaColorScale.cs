namespace CodexQuotaRail.Core.Quotas;

public readonly record struct RgbColor(byte R, byte G, byte B);

public static class QuotaColorScale
{
    private static readonly (int Percent, RgbColor Color)[] Anchors =
    [
        (0, new(255, 97, 93)),
        (1, new(255, 97, 93)),
        (21, new(255, 196, 91)),
        (51, new(201, 239, 99)),
        (100, new(145, 239, 107))
    ];

    public static RgbColor ForAvailable(int availablePercent)
    {
        var value = Math.Clamp(availablePercent, 0, 100);
        for (var index = 1; index < Anchors.Length; index++)
        {
            var upper = Anchors[index];
            var lower = Anchors[index - 1];
            if (value <= upper.Percent)
            {
                var ratio = (double)(value - lower.Percent) / (upper.Percent - lower.Percent);
                return new(
                    Lerp(lower.Color.R, upper.Color.R, ratio),
                    Lerp(lower.Color.G, upper.Color.G, ratio),
                    Lerp(lower.Color.B, upper.Color.B, ratio));
            }
        }

        return Anchors[^1].Color;
    }

    private static byte Lerp(byte start, byte end, double ratio) =>
        (byte)Math.Round(start + ((end - start) * ratio), MidpointRounding.AwayFromZero);
}
