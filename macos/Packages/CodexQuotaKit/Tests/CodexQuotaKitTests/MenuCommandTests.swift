import Testing
@testable import CodexQuotaKit

@Suite("菜单命令")
struct MenuCommandTests {
    @Test("全部点击入口具有稳定标识")
    func everyCommandHasAStableIdentifier() {
        let identifiers = MenuCommand.allCases.map(\.rawValue)

        #expect(identifiers.count == 13)
        #expect(Set(identifiers).count == identifiers.count)
        #expect(identifiers.contains("open-lingge-website"))
    }
}
