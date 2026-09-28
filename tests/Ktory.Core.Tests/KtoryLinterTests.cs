using System.Linq;
using Ktory.Core.Parser;
using Xunit;

namespace Ktory.Core.Tests
{
    public class KtoryLinterTests
    {
        [Fact]
        public void Linter_ValidScript_ReturnsNoDiagnostics()
        {
            string script = @"
@defaultLang: zh
@speaker alice: zh=""爱丽丝""
-> Start
=== Start ===
  alice:
    @zh: 你好世界
    @en: Hello World
  -> end
";
            var diagnostics = KtoryLinter.Analyze(script);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Linter_SyntaxError_ReturnsErrorDiagnostic()
        {
            string script = @"
=== Start ===
.sfx(""orphan"")
";
            var diagnostics = KtoryLinter.Analyze(script);
            var err = Assert.Single(diagnostics);
            Assert.Equal(KtoryDiagnosticSeverity.Error, err.Severity);
            Assert.Equal("KTR_E007", err.Code);
            Assert.Contains("Decorator has no preceding anchor", err.Message);
        }

        [Fact]
        public void Linter_TargetNotFound_ReturnsErrorDiagnostic()
        {
            string script = @"
=== Start ===
  -> NonExistent
";
            var diagnostics = KtoryLinter.Analyze(script);
            var err = Assert.Single(diagnostics);
            Assert.Equal(KtoryDiagnosticSeverity.Error, err.Severity);
            Assert.Equal("KTR_E004", err.Code);
            Assert.Contains("Target section '=== NonExistent ===' not found", err.Message);
        }

        [Fact]
        public void Linter_UnreferencedSection_EmitsWarning()
        {
            string script = @"
: 根节正文
=== DeadSection ===
  : 永远不会被调用的节
  -> return
";
            var diagnostics = KtoryLinter.Analyze(script);
            var warn = Assert.Single(diagnostics, d => d.Code == "KTR_W002");
            Assert.Equal(KtoryDiagnosticSeverity.Warning, warn.Severity);
            Assert.Contains("=== DeadSection ===", warn.Message);
            Assert.Contains("never referenced", warn.Message);
        }

        [Fact]
        public void Linter_MissingDefaultLanguageTranslation_EmitsWarning()
        {
            string script = @"
@defaultLang: zh
: 
  @en: English Only
";
            var diagnostics = KtoryLinter.Analyze(script);
            var warn = Assert.Single(diagnostics, d => d.Code == "KTR_W003");
            Assert.Equal(KtoryDiagnosticSeverity.Warning, warn.Severity);
            Assert.Contains("default language 'zh'", warn.Message);
        }

        [Fact]
        public void Linter_UnusedSpeaker_EmitsWarning()
        {
            string script = @"
@speaker alice: zh=""爱丽丝""
@speaker bob: zh=""鲍勃""
alice: 只有爱丽丝说话。
";
            var diagnostics = KtoryLinter.Analyze(script);
            var warn = Assert.Single(diagnostics, d => d.Code == "KTR_W004");
            Assert.Equal(KtoryDiagnosticSeverity.Warning, warn.Severity);
            Assert.Contains("bob", warn.Message);
            Assert.Contains("never used", warn.Message);
        }

        [Fact]
        public void Linter_UndeclaredSpeaker_EmitsWarning()
        {
            string script = @"
@speaker alice: zh=""爱丽丝""
alice: 正常台词。
charlie: 未经声明的角色。
";
            var diagnostics = KtoryLinter.Analyze(script);
            var warn = Assert.Single(diagnostics, d => d.Code == "KTR_W005");
            Assert.Equal(KtoryDiagnosticSeverity.Warning, warn.Severity);
            Assert.Contains("charlie", warn.Message);
            Assert.Contains("not declared in any @speaker definition", warn.Message);
        }

        [Fact]
        public void Linter_EmptySection_EmitsWarning()
        {
            string script = @"
-> EmptySection
=== EmptySection ===
";
            var diagnostics = KtoryLinter.Analyze(script);
            var warn = Assert.Single(diagnostics, d => d.Code == "KTR_W006");
            Assert.Equal(KtoryDiagnosticSeverity.Warning, warn.Severity);
            Assert.Contains("=== EmptySection ===", warn.Message);
            Assert.Contains("empty", warn.Message);
        }
    }
}
