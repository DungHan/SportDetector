// Standalone Vision-framework OCR helper for MacVisionOcrEngine.cs.
//
// Vision (VNRecognizeTextRequest) has no C API, and NBA.Capture/Mac/Interop's existing convention is to only
// P/Invoke plain C APIs (CoreGraphics/CoreFoundation) rather than bridge into the Objective-C runtime from C#.
// This tool keeps that boundary intact: it is a separate native process, invoked via Process.Start and talked
// to over argv/stdout, so the .NET side never touches ObjC directly.
//
// Usage: scoreboard-ocr <path-to-png>
// Output: a JSON array of {"text": "...", "confidence": 0.0-1.0} on stdout, one entry per recognized line.

import Foundation
import Vision
import CoreGraphics

struct RecognizedLine: Encodable {
    let text: String
    let confidence: Float
}

func printEmptyResultAndExit() -> Never {
    print("[]")
    exit(0)
}

guard CommandLine.arguments.count > 1 else {
    FileHandle.standardError.write("usage: scoreboard-ocr <image-path>\n".data(using: .utf8)!)
    exit(1)
}

let imagePath = CommandLine.arguments[1]

guard let dataProvider = CGDataProvider(filename: imagePath),
      let cgImage = CGImage(pngDataProviderSource: dataProvider, decode: nil, shouldInterpolate: false, intent: .defaultIntent) else {
    printEmptyResultAndExit()
}

let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
request.usesLanguageCorrection = false

let handler = VNImageRequestHandler(cgImage: cgImage, options: [:])

do {
    try handler.perform([request])
} catch {
    printEmptyResultAndExit()
}

let lines: [RecognizedLine] = (request.results ?? []).compactMap { observation in
    guard let candidate = observation.topCandidates(1).first else { return nil }
    return RecognizedLine(text: candidate.string, confidence: candidate.confidence)
}

let encoder = JSONEncoder()
if let jsonData = try? encoder.encode(lines), let json = String(data: jsonData, encoding: .utf8) {
    print(json)
} else {
    print("[]")
}
