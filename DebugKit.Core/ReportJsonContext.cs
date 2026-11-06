using System.Text.Json.Serialization;

namespace DebugKit.Core;

// Closed report graph: no reflection resolver or runtime enum-converter factory.
[JsonSourceGenerationOptions(UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(CaptureResult))]
[JsonSerializable(typeof(DiagnosticReport))]
internal partial class ReportJsonContext : JsonSerializerContext;
