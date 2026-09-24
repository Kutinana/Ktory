using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Desugar;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Xunit;

namespace Ktory.Core.Tests
{
    public class CoreConformanceTests
    {
        [Fact]
        public void Desugarer_TransformsInlineMarkdownAndRuby()
        {
            // Italic, bold, strikethrough
            Assert.Equal("<i>italic</i>", TextDesugarer.Desugar("*italic*"));
            Assert.Equal("<i>italic</i>", TextDesugarer.Desugar("_italic_"));
            Assert.Equal("<b>bold</b>", TextDesugarer.Desugar("**bold**"));
            Assert.Equal("<b>bold</b>", TextDesugarer.Desugar("__bold__"));
            Assert.Equal("<s>strike</s>", TextDesugarer.Desugar("~~strike~~"));

            // Ruby furigana
            Assert.Equal("<ruby=\"desk\">书桌</ruby>", TextDesugarer.Desugar("[书桌]{desk}"));

            // Raw tags preserved
            Assert.Equal("<color=#FF0000>Red</color>", TextDesugarer.Desugar("<color=#FF0000>Red</color>"));

            // Escaping preserved
            Assert.Equal("*not italic*", TextDesugarer.Desugar(@"\*not italic\*"));
        }

        [Fact]
        public void Sequencer_RootSectionSkipsNamedSection_AndJumpWorks()
        {
            string script = @"
主角: 根节第一句。

=== SubSection ===
  艾莉丝: 命名节台词。
  -> return

主角: 根节第二句。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();

            // 1st: Root line 1
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("根节第一句。", sequencer.CurrentPayload!.Content);

            // 2nd: Root line 2 (Named section was skipped by default in linear flow!)
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("根节第二句。", sequencer.CurrentPayload!.Content);

            // Step() -> Completed
            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_SubroutineCallAndReturn()
        {
            string script = @"
主角: 开始。
=> SubRoutine
主角: 结束。

=== SubRoutine ===
  艾莉丝: 子过程中的台词。
  -> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("开始。", sequencer.CurrentPayload!.Content);

            // Calling subroutine
            sequencer.Step();
            Assert.Equal("子过程中的台词。", sequencer.CurrentPayload!.Content);

            // Returning from subroutine
            sequencer.Step();
            Assert.Equal("结束。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_ZeroSelectableChoices_SmoothlySkipsOver()
        {
            string script = @"
主角: 准备选择。
#choice
  * ? {false} [不可选项]
    主角: 不应该到这里。

主角: 顺利跳过空容器。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            sequencer.Evaluator = new DefaultExpressionEvaluator { IgnoreUnknownConditions = false };

            sequencer.Start();
            Assert.Equal("准备选择。", sequencer.CurrentPayload!.Content);

            // Advancing skips over #choice because zero items are selectable
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("顺利跳过空容器。", sequencer.CurrentPayload!.Content);
        }

        [Fact]
        public void Sequencer_LanguageSwitching_UpdatesCurrentBeatWithoutSideEffects()
        {
            string script = @"
艾莉丝:
  @zh: 你好。
  @en: Hello.
  .emotion(smile)
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            int tagDispatchCount = 0;
            sequencer.OnTagsDispatched += _ => tagDispatchCount++;

            sequencer.Start(requestedLocale: "zh");
            Assert.Equal("你好。", sequencer.CurrentPayload!.Content);
            Assert.Equal("zh", sequencer.CurrentPayload.ActualLanguage);
            Assert.Equal(1, tagDispatchCount);

            // Switch language on the fly
            sequencer.SetLanguage("en");
            Assert.Equal("Hello.", sequencer.CurrentPayload.Content);
            Assert.Equal("en", sequencer.CurrentPayload.ActualLanguage);
            // Side effects must NOT re-trigger!
            Assert.Equal(1, tagDispatchCount);
        }
    }
}
