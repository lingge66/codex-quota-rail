using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.RateLimits;

public sealed record RateLimitSourceDependencies(
    CodexExecutableResolver ExecutableResolver,
    IRateLimitConnectionFactory ConnectionFactory,
    IRateLimitAvailabilitySignal Availability,
    TimeProvider TimeProvider,
    Version ClientVersion);
