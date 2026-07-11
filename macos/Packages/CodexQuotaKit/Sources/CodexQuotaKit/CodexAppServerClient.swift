import Foundation

public actor CodexAppServerClient<Transport: JSONLineTransport> {
    public nonisolated let notifications: AsyncStream<String>

    private let transport: Transport
    private let clientVersion: String
    private let notificationContinuation: AsyncStream<String>.Continuation
    private var pendingRequests: [Int: CheckedContinuation<JSONValue, Error>] = [:]
    private var nextRequestID = 0
    private var readTask: Task<Void, Never>?
    private var started = false
    private var authenticated = false

    public init(transport: Transport, clientVersion: String) {
        var continuation: AsyncStream<String>.Continuation!
        notifications = AsyncStream { continuation = $0 }
        notificationContinuation = continuation
        self.transport = transport
        self.clientVersion = clientVersion
    }

    public func start() async throws {
        guard !started else {
            return
        }
        try await transport.start()
        started = true
        readTask = Task { [weak self] in
            await self?.readLoop()
        }
        do {
            _ = try await request(
                method: "initialize",
                params: .object([
                    "clientInfo": .object([
                        "name": .string("codex_quota_rail_macos"),
                        "title": .string("Codex Quota Rail"),
                        "version": .string(clientVersion),
                    ]),
                ]))
            try await notify(method: "initialized", params: .object([:]))
        } catch {
            await stop()
            throw error
        }
    }

    public func refresh(receivedAt: Date = .now) async throws -> RawQuotaSnapshot {
        guard started else {
            throw AppServerError.processClosed
        }
        if !authenticated {
            let accountResult = try await request(
                method: "account/read",
                params: .object([:]))
            guard let account = accountResult.objectValue?["account"] else {
                throw AppServerError.invalidResponse
            }
            guard account != .null else {
                throw AppServerError.authenticationRequired
            }
            authenticated = true
        }
        let rateLimitResult = try await request(
            method: "account/rateLimits/read",
            params: .object([:]))
        let data = try JSONEncoder().encode(rateLimitResult)
        return try RateLimitMapper.map(data, receivedAt: receivedAt)
    }

    public func stop() async {
        guard started else {
            return
        }
        started = false
        authenticated = false
        readTask?.cancel()
        readTask = nil
        await transport.stop()
        failAllPending(with: AppServerError.processClosed)
    }

    private func request(method: String, params: JSONValue) async throws -> JSONValue {
        nextRequestID += 1
        let requestID = nextRequestID
        let message = JSONRPCEnvelope(id: requestID, method: method, params: params)
        let data = try JSONEncoder().encode(message)
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                pendingRequests[requestID] = continuation
                Task { [weak self] in
                    guard let self else {
                        continuation.resume(throwing: AppServerError.processClosed)
                        return
                    }
                    do {
                        try await self.transport.send(data)
                    } catch {
                        await self.failRequest(requestID, with: AppServerError.processClosed)
                    }
                }
            }
        } onCancel: {
            Task { [weak self] in
                await self?.failRequest(requestID, with: CancellationError())
            }
        }
    }

    private func notify(method: String, params: JSONValue) async throws {
        let message = JSONRPCEnvelope(method: method, params: params)
        try await transport.send(try JSONEncoder().encode(message))
    }

    private func readLoop() async {
        do {
            while !Task.isCancelled {
                let data = try await transport.receive()
                let envelope = try decode(data)
                if let requestID = envelope.id {
                    completeRequest(requestID, envelope: envelope)
                } else if let method = envelope.method {
                    notificationContinuation.yield(method)
                }
            }
        } catch is CancellationError {
        } catch {
            if started {
                started = false
                failAllPending(with: AppServerError.processClosed)
            }
        }
    }

    private func completeRequest(_ requestID: Int, envelope: JSONRPCEnvelope) {
        guard let continuation = pendingRequests.removeValue(forKey: requestID) else {
            return
        }
        if let error = envelope.error {
            continuation.resume(throwing: AppServerError.requestFailed(code: error.code))
        } else if let result = envelope.result {
            continuation.resume(returning: result)
        } else {
            continuation.resume(throwing: AppServerError.invalidResponse)
        }
    }

    private func failRequest(_ requestID: Int, with error: Error) {
        pendingRequests.removeValue(forKey: requestID)?.resume(throwing: error)
    }

    private func failAllPending(with error: Error) {
        let continuations = pendingRequests.values
        pendingRequests.removeAll()
        for continuation in continuations {
            continuation.resume(throwing: error)
        }
    }

    private func decode(_ data: Data) throws -> JSONRPCEnvelope {
        do {
            return try JSONDecoder().decode(JSONRPCEnvelope.self, from: data)
        } catch {
            throw AppServerError.invalidResponse
        }
    }
}
