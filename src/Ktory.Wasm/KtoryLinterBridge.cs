using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Ktory.Core.Parser;

using System.Runtime.Versioning;

namespace Ktory.Wasm;

[SupportedOSPlatform("browser")]
public partial class KtoryLinterBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [JSExport]
    public static string Lint(string script)
    {
        var diagnostics = KtoryLinter.Analyze(script);
        return JsonSerializer.Serialize(diagnostics, JsonOptions);
    }
}
