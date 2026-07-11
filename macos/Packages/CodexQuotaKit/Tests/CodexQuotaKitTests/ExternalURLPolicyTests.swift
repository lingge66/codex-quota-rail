import Foundation
import Testing
@testable import CodexQuotaKit

@Suite("外部网址策略")
struct ExternalURLPolicyTests {
    @Test("只允许绝对 HTTPS 地址")
    func allowsOnlyAbsoluteHTTPS() {
        #expect(ExternalURLPolicy.allows(URL(string: "https://lingge66.pages.dev/")!))
        #expect(!ExternalURLPolicy.allows(URL(string: "http://example.com")!))
        #expect(!ExternalURLPolicy.allows(URL(string: "file:///tmp/a")!))
        #expect(!ExternalURLPolicy.allows(URL(string: "javascript:alert(1)")!))
        #expect(!ExternalURLPolicy.allows(URL(string: "https:///missing-host")!))
    }
}
