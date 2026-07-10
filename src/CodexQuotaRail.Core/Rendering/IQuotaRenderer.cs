using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.Core.Rendering;

public interface IQuotaRenderer
{
    void Render(QuotaDisplayState state);
}
