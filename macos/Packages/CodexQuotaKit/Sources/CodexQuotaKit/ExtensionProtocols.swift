import Foundation

public protocol QuotaDataProviding: Sendable {
    func start() async throws
    func refresh(receivedAt: Date) async throws -> RawQuotaSnapshot
    func stop() async
}

public protocol ThemeLoading: Sendable {
    func loadTheme(from url: URL, fallback: QuotaTheme) throws -> QuotaTheme
}

public struct JSONThemeLoader: ThemeLoading {
    public init() {}

    public func loadTheme(from url: URL, fallback: QuotaTheme) throws -> QuotaTheme {
        let data = try Data(contentsOf: url)
        return try JSONDecoder().decode(QuotaTheme.self, from: data).validated(fallback: fallback)
    }
}

extension CodexAppServerClient: QuotaDataProviding {}
