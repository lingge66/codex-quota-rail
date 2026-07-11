import AppKit
import ApplicationServices
import CodexQuotaKit

private func accessibilityWindowCallback(
    observer: AXObserver,
    element: AXUIElement,
    notification: CFString,
    context: UnsafeMutableRawPointer?
) {
    guard let context else {
        return
    }
    let tracker = Unmanaged<AccessibilityWindowTracker>
        .fromOpaque(context)
        .takeUnretainedValue()
    Task { @MainActor in
        tracker.refresh()
    }
}

@MainActor
final class AccessibilityWindowTracker: TargetWindowTracking {
    let snapshots: AsyncStream<TrackedMacWindowSnapshot?>

    private let continuation: AsyncStream<TrackedMacWindowSnapshot?>.Continuation
    private let permissionService: AccessibilityPermissionService
    private let locator: MacCodexLocator
    private var workspaceObservers: [NSObjectProtocol] = []
    private var calibrationTimer: Timer?
    private var axObserver: AXObserver?
    private var observedPID: pid_t?
    private var observedWindow: AXUIElement?
    private var started = false

    init(
        targetBundleIdentifiers: [String],
        permissionService: AccessibilityPermissionService
    ) {
        var streamContinuation: AsyncStream<TrackedMacWindowSnapshot?>.Continuation!
        snapshots = AsyncStream { streamContinuation = $0 }
        continuation = streamContinuation
        locator = MacCodexLocator(targetBundleIdentifiers: targetBundleIdentifiers)
        self.permissionService = permissionService
    }

    func start() async {
        guard !started else {
            return
        }
        started = true
        installWorkspaceObservers()
        calibrationTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) {
            [weak self] _ in
            Task { @MainActor in self?.refresh() }
        }
        refresh()
    }

    func stop() async {
        started = false
        calibrationTimer?.invalidate()
        calibrationTimer = nil
        for observer in workspaceObservers {
            NSWorkspace.shared.notificationCenter.removeObserver(observer)
        }
        workspaceObservers.removeAll()
        clearAXObserver()
        continuation.yield(nil)
        continuation.finish()
    }

    func refresh() {
        guard started, permissionService.isTrusted else {
            continuation.yield(nil)
            return
        }
        guard let application = locator.runningApplication() else {
            clearAXObserver()
            continuation.yield(nil)
            return
        }
        configureAXObserverIfNeeded(for: application.processIdentifier)
        guard let snapshot = readSnapshot(for: application) else {
            continuation.yield(nil)
            return
        }
        continuation.yield(snapshot)
    }

    private func installWorkspaceObservers() {
        let center = NSWorkspace.shared.notificationCenter
        let names: [Notification.Name] = [
            NSWorkspace.didLaunchApplicationNotification,
            NSWorkspace.didTerminateApplicationNotification,
            NSWorkspace.didActivateApplicationNotification,
            NSWorkspace.didHideApplicationNotification,
            NSWorkspace.didUnhideApplicationNotification,
        ]
        workspaceObservers = names.map { name in
            center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                Task { @MainActor in self?.refresh() }
            }
        }
    }

    private func configureAXObserverIfNeeded(for processIdentifier: pid_t) {
        guard observedPID != processIdentifier else {
            return
        }
        clearAXObserver()
        var observer: AXObserver?
        guard AXObserverCreate(
            processIdentifier,
            accessibilityWindowCallback,
            &observer) == .success,
            let observer
        else {
            return
        }
        let applicationElement = AXUIElementCreateApplication(processIdentifier)
        let context = Unmanaged.passUnretained(self).toOpaque()
        for notification in [
            kAXFocusedWindowChangedNotification,
            kAXWindowCreatedNotification,
            kAXApplicationActivatedNotification,
            kAXApplicationDeactivatedNotification,
        ] {
            AXObserverAddNotification(observer, applicationElement, notification as CFString, context)
        }
        CFRunLoopAddSource(
            CFRunLoopGetMain(),
            AXObserverGetRunLoopSource(observer),
            .commonModes)
        axObserver = observer
        observedPID = processIdentifier
    }

    private func observeWindow(_ window: AXUIElement) {
        guard let observer = axObserver else {
            return
        }
        if let previous = observedWindow {
            removeWindowNotifications(previous, observer: observer)
        }
        observedWindow = window
        let context = Unmanaged.passUnretained(self).toOpaque()
        for notification in [
            kAXMovedNotification,
            kAXResizedNotification,
            kAXUIElementDestroyedNotification,
            kAXWindowMiniaturizedNotification,
            kAXWindowDeminiaturizedNotification,
        ] {
            AXObserverAddNotification(observer, window, notification as CFString, context)
        }
    }

    private func removeWindowNotifications(_ window: AXUIElement, observer: AXObserver) {
        for notification in [
            kAXMovedNotification,
            kAXResizedNotification,
            kAXUIElementDestroyedNotification,
            kAXWindowMiniaturizedNotification,
            kAXWindowDeminiaturizedNotification,
        ] {
            AXObserverRemoveNotification(observer, window, notification as CFString)
        }
    }

    private func clearAXObserver() {
        if let observer = axObserver {
            if let window = observedWindow {
                removeWindowNotifications(window, observer: observer)
            }
            CFRunLoopRemoveSource(
                CFRunLoopGetMain(),
                AXObserverGetRunLoopSource(observer),
                .commonModes)
        }
        axObserver = nil
        observedPID = nil
        observedWindow = nil
    }

    private func readSnapshot(
        for application: NSRunningApplication
    ) -> TrackedMacWindowSnapshot? {
        let applicationElement = AXUIElementCreateApplication(application.processIdentifier)
        guard let window = axElement(
                  applicationElement,
                  attribute: kAXFocusedWindowAttribute as CFString),
              let position = axPoint(window, attribute: kAXPositionAttribute as CFString),
              let size = axSize(window, attribute: kAXSizeAttribute as CFString),
              size.width > 0,
              size.height > 0
        else {
            return nil
        }
        observeWindow(window)
        let axFrame = CGRect(origin: position, size: size)
        let windowNumber = matchingWindowNumber(
            processIdentifier: application.processIdentifier,
            axFrame: axFrame)
        guard windowNumber != 0 else {
            return nil
        }
        let frame = appKitFrame(fromAXFrame: axFrame)
        let screen = bestScreen(for: frame)
        let screenFrame = screen?.frame ?? NSScreen.main?.frame ?? frame
        let minimized = axBoolean(
            window,
            attribute: kAXMinimizedAttribute as CFString) ?? false
        let fullScreen = axBoolean(window, attribute: "AXFullScreen" as CFString)
            ?? approximatelyEqual(frame, screenFrame)
        return TrackedMacWindowSnapshot(
            frame: frame.macRect,
            screenFrame: screenFrame.macRect,
            isVisible: !application.isHidden,
            isMinimized: minimized,
            isFullScreen: fullScreen,
            isFocused: NSWorkspace.shared.frontmostApplication?.processIdentifier
                == application.processIdentifier,
            windowNumber: windowNumber)
    }

    private func axElement(_ element: AXUIElement, attribute: CFString) -> AXUIElement? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute, &value) == .success,
              let value,
              CFGetTypeID(value) == AXUIElementGetTypeID()
        else {
            return nil
        }
        return unsafeDowncast(value, to: AXUIElement.self)
    }

    private func axPoint(_ element: AXUIElement, attribute: CFString) -> CGPoint? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute, &value) == .success,
              let value,
              CFGetTypeID(value) == AXValueGetTypeID()
        else {
            return nil
        }
        var point = CGPoint.zero
        guard AXValueGetValue(unsafeDowncast(value, to: AXValue.self), .cgPoint, &point) else {
            return nil
        }
        return point
    }

    private func axSize(_ element: AXUIElement, attribute: CFString) -> CGSize? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute, &value) == .success,
              let value,
              CFGetTypeID(value) == AXValueGetTypeID()
        else {
            return nil
        }
        var size = CGSize.zero
        guard AXValueGetValue(unsafeDowncast(value, to: AXValue.self), .cgSize, &size) else {
            return nil
        }
        return size
    }

    private func axBoolean(_ element: AXUIElement, attribute: CFString) -> Bool? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute, &value) == .success,
              let value,
              CFGetTypeID(value) == CFBooleanGetTypeID()
        else {
            return nil
        }
        return CFBooleanGetValue(unsafeDowncast(value, to: CFBoolean.self))
    }

    private func matchingWindowNumber(processIdentifier: pid_t, axFrame: CGRect) -> Int {
        guard let windowInfo = CGWindowListCopyWindowInfo(
            [.optionOnScreenOnly, .excludeDesktopElements],
            kCGNullWindowID) as? [[String: Any]]
        else {
            return 0
        }
        return windowInfo
            .compactMap { info -> (number: Int, distance: CGFloat)? in
                guard let ownerPID = info[kCGWindowOwnerPID as String] as? Int,
                      ownerPID == Int(processIdentifier),
                      let number = info[kCGWindowNumber as String] as? Int,
                      let bounds = info[kCGWindowBounds as String] as? [String: Any],
                      let frame = CGRect(dictionaryRepresentation: bounds as CFDictionary)
                else {
                    return nil
                }
                let distance = abs(frame.minX - axFrame.minX)
                    + abs(frame.minY - axFrame.minY)
                    + abs(frame.width - axFrame.width)
                    + abs(frame.height - axFrame.height)
                return (number, distance)
            }
            .min(by: { $0.distance < $1.distance })?
            .number ?? 0
    }

    private func appKitFrame(fromAXFrame frame: CGRect) -> CGRect {
        let mainDisplayHeight = NSScreen.screens
            .first(where: { $0.frame.origin == .zero })?
            .frame.height ?? NSScreen.main?.frame.height ?? 0
        return CGRect(
            x: frame.minX,
            y: mainDisplayHeight - frame.maxY,
            width: frame.width,
            height: frame.height)
    }

    private func bestScreen(for frame: CGRect) -> NSScreen? {
        NSScreen.screens.max { left, right in
            intersectionArea(left.frame, frame) < intersectionArea(right.frame, frame)
        }
    }

    private func intersectionArea(_ first: CGRect, _ second: CGRect) -> CGFloat {
        let intersection = first.intersection(second)
        return intersection.isNull ? 0 : intersection.width * intersection.height
    }

    private func approximatelyEqual(_ first: CGRect, _ second: CGRect) -> Bool {
        abs(first.minX - second.minX) <= 2
            && abs(first.minY - second.minY) <= 2
            && abs(first.width - second.width) <= 2
            && abs(first.height - second.height) <= 2
    }
}

private extension CGRect {
    var macRect: MacRect {
        MacRect(
            x: Double(origin.x),
            y: Double(origin.y),
            width: Double(size.width),
            height: Double(size.height))
    }
}
