import CodexQuotaKit
import Foundation

@MainActor
final class RateLimitCoordinator {
    private let model: ApplicationModel
    private let locator: MacCodexLocator
    private let log: ApplicationLog
    private var client: CodexAppServerClient<ProcessJSONLineTransport>?
    private var refreshTask: Task<Void, Never>?
    private var pollingTask: Task<Void, Never>?
    private var notificationTask: Task<Void, Never>?
    private var started = false

    init(model: ApplicationModel, locator: MacCodexLocator, log: ApplicationLog) {
        self.model = model
        self.locator = locator
        self.log = log
    }

    func start() {
        guard !started else {
            return
        }
        started = true
        refresh()
        pollingTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(60))
                guard !Task.isCancelled else {
                    return
                }
                self?.refresh()
            }
        }
    }

    func refresh() {
        guard refreshTask == nil else {
            return
        }
        refreshTask = Task { [weak self] in
            await self?.refreshNow()
            self?.refreshTask = nil
        }
    }

    func stop() async {
        started = false
        refreshTask?.cancel()
        pollingTask?.cancel()
        notificationTask?.cancel()
        refreshTask = nil
        pollingTask = nil
        notificationTask = nil
        if let client {
            await client.stop()
        }
        client = nil
    }

    private func refreshNow() async {
        do {
            let client = try await ensureClient()
            let snapshot = try await client.refresh()
            model.apply(snapshot: snapshot)
            await log.write(level: "information", event: "rate_limits_updated")
        } catch AppServerError.authenticationRequired {
            model.apply(connection: .authenticationRequired, message: "请先在 Codex 中登录")
        } catch AppServerError.unsupported {
            await resetClient()
            model.apply(connection: .unsupported, message: "当前 Codex 版本暂不支持额度读取")
        } catch {
            await resetClient()
            let connection: QuotaConnectionState = model.displayState.windows.isEmpty
                ? .unavailable
                : .stale
            model.apply(connection: connection, message: "连接暂时中断")
            await log.write(level: "warning", event: "rate_limits_refresh_failed")
        }
    }

    private func ensureClient() async throws -> CodexAppServerClient<ProcessJSONLineTransport> {
        if let client {
            return client
        }
        let application = locator.runningApplication()
        let resolver = CodexExecutableResolver(
            fileSystem: LocalFileSystemAccess(),
            environment: ProcessInfo.processInfo.environment,
            desktopCandidates: locator.appServerCandidates(for: application))
        switch resolver.resolve() {
        case let .found(executableURL):
            let transport = ProcessJSONLineTransport(
                launchSpec: ProcessLaunchSpec(
                    executableURL: executableURL,
                    arguments: ["app-server", "--listen", "stdio://"]))
            let newClient = CodexAppServerClient(
                transport: transport,
                clientVersion: Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String
                    ?? "0.1.0")
            try await newClient.start()
            client = newClient
            observeNotifications(from: newClient)
            return newClient
        case .missing:
            throw AppServerError.processClosed
        case .unsupported:
            throw AppServerError.unsupported
        }
    }

    private func observeNotifications(
        from client: CodexAppServerClient<ProcessJSONLineTransport>
    ) {
        notificationTask?.cancel()
        notificationTask = Task { [weak self] in
            for await method in client.notifications {
                guard !Task.isCancelled else {
                    return
                }
                if method == "account/rateLimits/updated" {
                    self?.refresh()
                }
            }
        }
    }

    private func resetClient() async {
        notificationTask?.cancel()
        notificationTask = nil
        if let client {
            await client.stop()
        }
        client = nil
    }
}
