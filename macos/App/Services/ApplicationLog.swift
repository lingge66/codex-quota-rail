import Foundation

actor ApplicationLog {
    private struct Entry: Encodable {
        let timestamp: Date
        let level: String
        let event: String
    }

    private let directoryURL: URL
    private let fileURL: URL

    init(directoryURL: URL) {
        self.directoryURL = directoryURL
        fileURL = directoryURL.appendingPathComponent("codex-quota-rail.jsonl")
    }

    func write(level: String, event: String) async {
        do {
            try FileManager.default.createDirectory(
                at: directoryURL,
                withIntermediateDirectories: true)
            let entry = Entry(timestamp: .now, level: level, event: event)
            var data = try JSONEncoder().encode(entry)
            data.append(0x0A)
            if !FileManager.default.fileExists(atPath: fileURL.path) {
                try data.write(to: fileURL, options: .atomic)
                return
            }
            let handle = try FileHandle(forWritingTo: fileURL)
            try handle.seekToEnd()
            try handle.write(contentsOf: data)
            try handle.close()
        } catch {
        }
    }
}
