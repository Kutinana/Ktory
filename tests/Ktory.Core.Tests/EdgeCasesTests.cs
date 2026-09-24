using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Xunit;

namespace Ktory.Core.Tests
{
    public class EdgeCasesTests
    {
        [Fact]
        public void Narration_ExplicitAndSyntacticSugar_BothRecognizedAsNarration()
        {
            string script = @"
: 这是显式冒号旁白。
这是语法糖脱糖旁白。
主角: 这是角色对白。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            // 1: Explicit narration
            Assert.True(sequencer.CurrentPayload!.IsNarration);
            Assert.Equal("这是显式冒号旁白。", sequencer.CurrentPayload.Content);

            // 2: Syntactic sugar narration
            sequencer.Step();
            Assert.True(sequencer.CurrentPayload!.IsNarration);
            Assert.Equal("这是语法糖脱糖旁白。", sequencer.CurrentPayload.Content);

            // 3: Speaker dialogue
            sequencer.Step();
            Assert.False(sequencer.CurrentPayload!.IsNarration);
            Assert.Equal("主角", sequencer.CurrentPayload.Speaker);
            Assert.Equal("这是角色对白。", sequencer.CurrentPayload.Content);
        }

        [Fact]
        public void Loop_Limit_ExhaustsAfterMaxIterations()
        {
            string script = @"
#choice.loop(2)
  + [重复选项]
    主角: 执行了一次。

主角: 循环结束离开。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            // Iteration 1
            sequencer.SubmitChoice("重复选项");
            Assert.Equal("执行了一次。", sequencer.CurrentPayload!.Content);

            // Iteration 2
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            sequencer.SubmitChoice("重复选项");
            Assert.Equal("执行了一次。", sequencer.CurrentPayload!.Content);

            // Iteration 3: Loop limit (2) reached, should break out to post-loop line!
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("循环结束离开。", sequencer.CurrentPayload!.Content);
        }

        [Fact]
        public void AutoDirective_RecognizedWithoutCrashing()
        {
            string script = @"
#AUTO
主角: 自动播放台词一。
主角: 自动播放台词二。
#AUTO_END
主角: 恢复手动。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            // #AUTO is a directive step or passes
            // Check steps
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
        }
    }
}
