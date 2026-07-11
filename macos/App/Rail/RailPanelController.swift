import AppKit
import CodexQuotaKit
import Combine
import SwiftUI

@MainActor
final class RailPanelController {
    private let model: ApplicationModel
    private let railPanel: NonActivatingPanel
    private let detailPanel: NonActivatingPanel
    private var cancellables = Set<AnyCancellable>()
    private var railPointerInside = false
    private var detailPointerInside = false
    private var closeTask: Task<Void, Never>?

    init(model: ApplicationModel) {
        self.model = model
        railPanel = Self.makePanel()
        detailPanel = Self.makePanel()
        observeModel()
        render()
    }

    func close() {
        closeTask?.cancel()
        railPanel.orderOut(nil)
        detailPanel.orderOut(nil)
    }

    private static func makePanel() -> NonActivatingPanel {
        let panel = NonActivatingPanel(
            contentRect: .zero,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.animationBehavior = .none
        panel.collectionBehavior = [.fullScreenAuxiliary, .transient, .ignoresCycle]
        panel.level = .normal
        return panel
    }

    private func observeModel() {
        model.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] in
                DispatchQueue.main.async { self?.render() }
            }
            .store(in: &cancellables)
    }

    private func render() {
        let placement = model.placement
        guard placement.mode != .hidden else {
            railPanel.orderOut(nil)
            detailPanel.orderOut(nil)
            return
        }
        railPanel.alphaValue = placement.opacity
        railPanel.ignoresMouseEvents = placement.mode == .compactRail
        railPanel.setFrame(placement.frame.cgRect, display: true)
        railPanel.contentView = NSHostingView(
            rootView: RailView(
                state: model.displayState,
                mode: placement.mode,
                settings: model.settings,
                customTheme: model.customTheme,
                onClick: { [weak self] in self?.model.toggleDetails() },
                onPointerChanged: { [weak self] inside in
                    self?.setRailPointer(inside)
                }))
        if placement.relativeWindowNumber == 0 {
            railPanel.orderFront(nil)
        } else {
            railPanel.order(.above, relativeTo: placement.relativeWindowNumber)
        }
        renderDetails(relativeTo: placement)
    }

    private func renderDetails(relativeTo placement: RailPlacement) {
        guard model.isDetailsVisible, placement.mode == .externalRail else {
            detailPanel.orderOut(nil)
            return
        }
        let width = 280.0
        let height = max(104.0, 64.0 + (Double(model.displayState.windows.count) * 42.0))
        let x = max(placement.frame.x, placement.frame.maxX - width)
        let frame = NSRect(
            x: x,
            y: placement.frame.y - height - 4,
            width: width,
            height: height)
        detailPanel.alphaValue = placement.opacity
        detailPanel.setFrame(frame, display: true)
        detailPanel.contentView = NSHostingView(
            rootView: QuotaDetailsView(
                state: model.displayState,
                settings: model.settings,
                customTheme: model.customTheme,
                onPointerChanged: { [weak self] inside in
                    self?.setDetailPointer(inside)
                }))
        detailPanel.order(.above, relativeTo: railPanel.windowNumber)
    }

    private func setRailPointer(_ inside: Bool) {
        railPointerInside = inside
        updateCloseSchedule()
    }

    private func setDetailPointer(_ inside: Bool) {
        detailPointerInside = inside
        updateCloseSchedule()
    }

    private func updateCloseSchedule() {
        closeTask?.cancel()
        guard !railPointerInside, !detailPointerInside, model.isDetailsVisible else {
            return
        }
        closeTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(160))
            guard !Task.isCancelled else {
                return
            }
            self?.model.closeDetails()
        }
    }
}

private extension MacRect {
    var cgRect: CGRect {
        CGRect(x: x, y: y, width: width, height: height)
    }
}
