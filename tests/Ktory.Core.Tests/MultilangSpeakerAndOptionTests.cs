using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Xunit;

namespace Ktory.Core.Tests
{
    public class MultilangSpeakerAndOptionTests
    {
        [Fact]
        public void Speaker_WithExplicitId_ReverseLookupAcrossLocales()
        {
            string script = @"
@defaultLang: zh
@speaker alice: zh=""爱丽丝"" | en=""Alice"" | ja=""アリス""

alice:
  @zh: 你好！
  @en: Hello!
  @ja: こんにちは！

爱丽丝:
  @zh: 再次问好。
  @en: Greeting again.
";
            var file = KtoryParser.Parse(script);

            // Test in English
            var seqEn = new KtorySequencer(file);
            seqEn.Start(requestedLocale: "en");

            Assert.Equal("Alice", seqEn.CurrentPayload!.Speaker);
            Assert.Equal("Hello!", seqEn.CurrentPayload.Content);

            seqEn.Step();
            Assert.Equal("Alice", seqEn.CurrentPayload!.Speaker);
            Assert.Equal("Greeting again.", seqEn.CurrentPayload.Content);

            // Test in Chinese
            var seqZh = new KtorySequencer(file);
            seqZh.Start(requestedLocale: "zh");

            Assert.Equal("爱丽丝", seqZh.CurrentPayload!.Speaker);
            Assert.Equal("你好！", seqZh.CurrentPayload.Content);

            seqZh.Step();
            Assert.Equal("爱丽丝", seqZh.CurrentPayload!.Speaker);
            Assert.Equal("再次问好。", seqZh.CurrentPayload.Content);

            // Test in Japanese
            var seqJa = new KtorySequencer(file);
            seqJa.Start(requestedLocale: "ja");

            Assert.Equal("アリス", seqJa.CurrentPayload!.Speaker);
            Assert.Equal("こんにちは！", seqJa.CurrentPayload.Content);

            // Test in missing locale (fr) -> fallback to defaultLang (zh)
            var seqFr = new KtorySequencer(file);
            seqFr.Start(requestedLocale: "fr");

            Assert.Equal("爱丽丝", seqFr.CurrentPayload!.Speaker);
            Assert.Equal("你好！", seqFr.CurrentPayload.Content);
        }

        [Fact]
        public void Speaker_WithoutId_SymmetricalReverseLookup()
        {
            string script = @"
@defaultLang: zh
@speaker: zh=""夏洛克·福尔摩斯"" | en=""Sherlock Holmes""

Sherlock Holmes:
  @zh: 基本演绎法。
  @en: Elementary, my dear Watson.
";
            var file = KtoryParser.Parse(script);

            var seqZh = new KtorySequencer(file);
            seqZh.Start(requestedLocale: "zh");

            // Reverse lookup: Sherlock Holmes in script -> 夏洛克·福尔摩斯 in zh output
            Assert.Equal("夏洛克·福尔摩斯", seqZh.CurrentPayload!.Speaker);
            Assert.Equal("基本演绎法。", seqZh.CurrentPayload.Content);

            var seqEn = new KtorySequencer(file);
            seqEn.Start(requestedLocale: "en");
            Assert.Equal("Sherlock Holmes", seqEn.CurrentPayload!.Speaker);
            Assert.Equal("Elementary, my dear Watson.", seqEn.CurrentPayload.Content);
        }

        [Fact]
        public void Speaker_CaseSensitive_DifferentEntities()
        {
            string script = @"
@defaultLang: zh
@speaker alice: zh=""小写爱丽丝"" | en=""little_alice""
@speaker ALICE: zh=""大写爱丽丝"" | en=""BIG_ALICE""

alice:
  @zh: 我是小写。
  @en: I am lowercase.

ALICE:
  @zh: 我是大写。
  @en: I am uppercase.

Alice:
  @zh: 我未在别名表中声明。
  @en: I am undeclared.
";
            var file = KtoryParser.Parse(script);

            var seq = new KtorySequencer(file);
            seq.Start(requestedLocale: "zh");

            // 1. alice
            Assert.Equal("小写爱丽丝", seq.CurrentPayload!.Speaker);
            seq.Step();

            // 2. ALICE
            Assert.Equal("大写爱丽丝", seq.CurrentPayload!.Speaker);
            seq.Step();

            // 3. Alice (not declared, strictly case-sensitive, falls back verbatim)
            Assert.Equal("Alice", seq.CurrentPayload!.Speaker);
        }

        [Fact]
        public void Speaker_DuplicateOrConflictingAlias_ThrowsException()
        {
            string scriptConflicting = @"
@defaultLang: zh
@speaker: zh=""爱丽丝"" | en=""Alice""
@speaker: zh=""爱丽丝"" | en=""Alicia""
";
            Assert.Throws<KtoryException>(() => KtoryParser.Parse(scriptConflicting));

            string scriptDuplicate = @"
@defaultLang: zh
@speaker alice: zh=""爱丽丝"" | en=""Alice""
@speaker alice: zh=""爱丽丝"" | en=""Alice""
";
            Assert.Throws<KtoryException>(() => KtoryParser.Parse(scriptDuplicate));
        }

        [Fact]
        public void Option_Multilang_CanonicalStructureAndExecution()
        {
            string script = @"
@defaultLang: zh
@speaker alice: zh=""爱丽丝"" | en=""Alice""

#choice
  .menu_tag

  * [@zh: ""向爱丽丝搭话""]
    [@en: ""Talk to Alice""]
    .opt_tag

    alice:
      @zh: 我们必须尽快离开！
      @en: We have to leave!
      .emotion(urgent)

主角: 走吧。
";
            var file = KtoryParser.Parse(script);

            // Test English choice presentation and execution
            var seqEn = new KtorySequencer(file);
            seqEn.Start(requestedLocale: "en");

            Assert.Equal(ExecutionStatus.AwaitingChoice, seqEn.Status);
            Assert.NotNull(seqEn.CurrentChoice);
            Assert.Single(seqEn.CurrentChoice.Options);
            Assert.Equal("Talk to Alice", seqEn.CurrentChoice.Options[0].Label);

            // Submit option
            seqEn.SubmitChoice("Talk to Alice");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, seqEn.Status);
            Assert.Equal("Alice", seqEn.CurrentPayload!.Speaker);
            Assert.Equal("We have to leave!", seqEn.CurrentPayload.Content);

            // Test Chinese choice presentation and execution
            var seqZh = new KtorySequencer(file);
            seqZh.Start(requestedLocale: "zh");

            Assert.Equal(ExecutionStatus.AwaitingChoice, seqZh.Status);
            Assert.Equal("向爱丽丝搭话", seqZh.CurrentChoice!.Options[0].Label);

            seqZh.SubmitChoice("向爱丽丝搭话");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, seqZh.Status);
            Assert.Equal("爱丽丝", seqZh.CurrentPayload!.Speaker);
            Assert.Equal("我们必须尽快离开！", seqZh.CurrentPayload.Content);
        }

        [Fact]
        public void Option_VariantAfterBranchAnchor_ThrowsSyntaxException()
        {
            string invalidScript = @"
@defaultLang: zh

#choice
  * [@zh: ""向爱丽丝搭话""]
    alice:
      @zh: 我们走吧！
    [@en: ""Talk to Alice""]
";
            var ex = Assert.Throws<KtoryException>(() => KtoryParser.Parse(invalidScript));
            Assert.Contains("cannot appear after branch execution anchor", ex.Message);
        }

        [Fact]
        public void HotSwitchLanguage_UpdatesBothTextAndSpeakerAndOptions()
        {
            string script = @"
@defaultLang: zh
@speaker alice: zh=""爱丽丝"" | en=""Alice""

alice:
  @zh: 你好！
  @en: Hello!

#choice
  * [@zh: ""好的""]
    [@en: ""Okay""]
";
            var file = KtoryParser.Parse(script);
            var seq = new KtorySequencer(file);

            // Start in zh
            seq.Start(requestedLocale: "zh");
            Assert.Equal("爱丽丝", seq.CurrentPayload!.Speaker);
            Assert.Equal("你好！", seq.CurrentPayload.Content);

            // Hot switch to en
            seq.SetLanguage("en");
            Assert.Equal("Alice", seq.CurrentPayload!.Speaker);
            Assert.Equal("Hello!", seq.CurrentPayload.Content);

            // Advance to choice
            seq.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, seq.Status);
            Assert.Equal("Okay", seq.CurrentChoice!.Options[0].Label);

            // Hot switch back to zh
            seq.SetLanguage("zh");
            Assert.Equal("好的", seq.CurrentChoice!.Options[0].Label);
        }
    }
}
