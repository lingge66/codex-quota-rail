import Foundation
import Testing
@testable import CodexQuotaKit

@Suite("Codex App Server 客户端")
struct CodexAppServerClientTests {
    @Test("初始化后读取真实额度")
    func initializesAndReadsQuota() async throws {
        let transport = FakeJSONLineTransport(responses: [
            #"{"jsonrpc":"2.0","id":1,"result":{}}"#,
            #"{"jsonrpc":"2.0","id":2,"result":{"account":{"type":"chatgpt"}}}"#,
            #"{"jsonrpc":"2.0","id":3,"result":{"rateLimits":{"primary":{"usedPercent":68,"windowDurationMins":300}}}}"#,
        ])
        let client = CodexAppServerClient(transport: transport, clientVersion: "0.1.0")

        try await client.start()
        let snapshot = try await client.refresh(receivedAt: Date(timeIntervalSince1970: 1_800_000_000))

        #expect(snapshot.primary?.usedPercent == 68)
        #expect(await transport.methods() == [
            "initialize",
            "initialized",
            "account/read",
            "account/rateLimits/read",
        ])
    }

    @Test("未登录时返回认证状态")
    func reportsAuthenticationRequired() async throws {
        let transport = FakeJSONLineTransport(responses: [
            #"{"jsonrpc":"2.0","id":1,"result":{}}"#,
            #"{"jsonrpc":"2.0","id":2,"result":{"account":null}}"#,
        ])
        let client = CodexAppServerClient(transport: transport, clientVersion: "0.1.0")

        try await client.start()

        await #expect(throws: AppServerError.authenticationRequired) {
            try await client.refresh(receivedAt: .now)
        }
    }

    @Test("服务器错误不暴露远端错误文案")
    func sanitizesServerError() async throws {
        let transport = FakeJSONLineTransport(responses: [
            #"{"jsonrpc":"2.0","id":1,"result":{}}"#,
            #"{"jsonrpc":"2.0","id":2,"error":{"code":-32601,"message":"internal secret"}}"#,
        ])
        let client = CodexAppServerClient(transport: transport, clientVersion: "0.1.0")

        try await client.start()

        await #expect(throws: AppServerError.requestFailed(code: -32601)) {
            try await client.refresh(receivedAt: .now)
        }
    }
}

private actor FakeJSONLineTransport: JSONLineTransport {
    private var responses: [Data]
    private var sent: [Data] = []

    init(responses: [String]) {
        self.responses = responses.map { Data($0.utf8) }
    }

    func start() async throws {}

    func send(_ line: Data) async throws {
        sent.append(line)
    }

    func receive() async throws -> Data {
        guard !responses.isEmpty else {
            throw AppServerError.processClosed
        }
        return responses.removeFirst()
    }

    func stop() async {}

    func methods() throws -> [String] {
        try sent.map { data in
            let object = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
            return try #require(object["method"] as? String)
        }
    }
}
