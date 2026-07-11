public enum MenuCommand: String, CaseIterable, Sendable {
    case openSettings = "open-settings"
    case refresh
    case toggleFollow = "toggle-follow"
    case themeAutomatic = "theme-automatic"
    case themeDark = "theme-dark"
    case themeLight = "theme-light"
    case toggleReduceMotion = "toggle-reduce-motion"
    case toggleLaunchAtLogin = "toggle-launch-at-login"
    case openLogs = "open-logs"
    case troubleshoot
    case openWebsite = "open-lingge-website"
    case checkUpdates = "check-updates"
    case quit
}
