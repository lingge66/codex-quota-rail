import Foundation

public protocol JSONLineTransport: Sendable {
    func start() async throws
    func send(_ line: Data) async throws
    func receive() async throws -> Data
    func stop() async
}

public actor ProcessJSONLineTransport: JSONLineTransport {
    private let launchSpec: ProcessLaunchSpec
    private let process = Process()
    private let inputPipe = Pipe()
    private let outputPipe = Pipe()
    private let errorPipe = Pipe()
    private var outputBuffer = Data()
    private var errorDrainTask: Task<Void, Never>?
    private var started = false

    public init(launchSpec: ProcessLaunchSpec) {
        self.launchSpec = launchSpec
    }

    public func start() async throws {
        guard !started else {
            return
        }
        process.executableURL = launchSpec.executableURL
        process.arguments = launchSpec.arguments
        process.standardInput = inputPipe
        process.standardOutput = outputPipe
        process.standardError = errorPipe
        try process.run()
        started = true
        let handle = errorPipe.fileHandleForReading
        errorDrainTask = Task.detached(priority: .utility) {
            while !Task.isCancelled {
                let data = handle.availableData
                if data.isEmpty {
                    return
                }
            }
        }
    }

    public func send(_ line: Data) async throws {
        guard started, process.isRunning else {
            throw AppServerError.processClosed
        }
        var payload = line
        payload.append(0x0A)
        try inputPipe.fileHandleForWriting.write(contentsOf: payload)
    }

    public func receive() async throws -> Data {
        while true {
            if let line = extractLine() {
                return line
            }
            let handle = outputPipe.fileHandleForReading
            let chunk = await Task.detached(priority: .utility) {
                handle.availableData
            }.value
            guard !chunk.isEmpty else {
                throw AppServerError.processClosed
            }
            outputBuffer.append(chunk)
        }
    }

    public func stop() async {
        errorDrainTask?.cancel()
        errorDrainTask = nil
        if process.isRunning {
            process.terminate()
        }
        try? inputPipe.fileHandleForWriting.close()
        try? outputPipe.fileHandleForReading.close()
        try? errorPipe.fileHandleForReading.close()
        started = false
        outputBuffer.removeAll(keepingCapacity: false)
    }

    private func extractLine() -> Data? {
        guard let newline = outputBuffer.firstIndex(of: 0x0A) else {
            return nil
        }
        var line = outputBuffer[..<newline]
        outputBuffer.removeSubrange(...newline)
        if line.last == 0x0D {
            line = line.dropLast()
        }
        return Data(line)
    }
}
