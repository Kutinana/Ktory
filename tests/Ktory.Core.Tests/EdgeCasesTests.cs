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

        [Fact]
        public void Sequencer_RootSectionEndTerminatesScriptImmediately()
        {
            string script = @"
: A
-> end
: B
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("A", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_RootChoiceJumpToEndTerminatesImmediately()
        {
            string script = @"
: A
#choice
  * [Quit] -> end
: B
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("A", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // enter choice
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            sequencer.SubmitChoice("Quit");
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_RootInlineBranchEndTerminatesImmediately()
        {
            string script = @"
: A
#choice
  * [Choice 1]
    : In branch
    -> end
: B
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("A", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // enter choice
            sequencer.SubmitChoice("Choice 1");
            Assert.Equal("In branch", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // executes -> end
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_NamedSectionNaturalEnd_ClearsCallStackAndResumesAfterSection()
        {
            string script = @"
: Root1
=> SubRoutine
: RootDeadCode

=== SubRoutine ===
  : SubLine1
  : SubLine2

: Root2
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("Root1", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // calls SubRoutine
            Assert.Equal("SubLine1", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal("SubLine2", sequencer.CurrentPayload!.Content);

            // SubRoutine ends naturally -> implicit end: clear call stack, resume at Root2!
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("Root2", sequencer.CurrentPayload!.Content);
            Assert.Empty(sequencer.CallStack);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_NamedSectionNaturalEnd_NoSubsequentRootNodes_Completes()
        {
            string script = @"
: Root1
=> SubRoutine
: RootDeadCode

=== SubRoutine ===
  : SubLine1
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("Root1", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // calls SubRoutine
            Assert.Equal("SubLine1", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // SubRoutine ends naturally, no root nodes after it -> completes
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
            Assert.Empty(sequencer.CallStack);
        }

        [Fact]
        public void Sequencer_InlineBranchNaturalEnd_ReturnsToContainingSection()
        {
            string script = @"
=== SubRoutine ===
  : BeforeChoice
  #choice
    * [Branch]
      : InBranch
  : AfterChoice
  -> return

: RootStart
=> SubRoutine
: RootEnd
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("RootStart", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // calls SubRoutine
            Assert.Equal("BeforeChoice", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // choice
            sequencer.SubmitChoice("Branch");
            Assert.Equal("InBranch", sequencer.CurrentPayload!.Content);

            // InBranch finishes (inline branch ends naturally) -> must return to AfterChoice inside SubRoutine!
            sequencer.Step();
            Assert.Equal("AfterChoice", sequencer.CurrentPayload!.Content);

            // SubRoutine executes -> return -> returns to RootEnd
            sequencer.Step();
            Assert.Equal("RootEnd", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_BreakFromInsideLoopBranch_DoesNotLeaveStaleFrame()
        {
            string script = @"
#choice.loop
  * [DoAction]
    : Inside Action
    -> break

: AfterLoop1
: AfterLoop2
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            sequencer.SubmitChoice("DoAction");
            Assert.Equal("Inside Action", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // executes -> break -> breaks loop, unwinds branch frame!
            Assert.Equal("AfterLoop1", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal("AfterLoop2", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // Steps exhausted -> should be Completed, NOT re-enter loop!
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_BreakInsideSubroutineLoop_PreservesOuterCallFrame()
        {
            string script = @"
: RootStart
=> SubWithLoop
: RootEnd

=== SubWithLoop ===
  #choice.loop
    * [ExitLoop]
      : Breaking now
      -> break
  : AfterLoop
  -> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("RootStart", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // calls SubWithLoop
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            sequencer.SubmitChoice("ExitLoop");
            Assert.Equal("Breaking now", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // executes -> break
            Assert.Equal("AfterLoop", sequencer.CurrentPayload!.Content);
            // Outer CallFrame for => SubWithLoop must still exist on stack!
            Assert.Single(sequencer.CallStack);

            sequencer.Step(); // -> return
            Assert.Equal("RootEnd", sequencer.CurrentPayload!.Content);
            Assert.Empty(sequencer.CallStack);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_BreakInsideNestedInlineBranchLoop_PreservesOuterBranchFrame()
        {
            string script = @"
#choice
  * [OuterChoice]
    : Outer Start
    #choice.loop
      * [ExitInner]
        : In Exit
        -> break
    : Outer End
: Root Finish
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            sequencer.SubmitChoice("OuterChoice");
            Assert.Equal("Outer Start", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // inner_loop
            sequencer.SubmitChoice("ExitInner");
            Assert.Equal("In Exit", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // -> break
            Assert.Equal("Outer End", sequencer.CurrentPayload!.Content);
            Assert.Single(sequencer.CallStack); // OuterChoice frame preserved!

            sequencer.Step(); // Outer branch finishes naturally -> returns to Root Finish!
            Assert.Equal("Root Finish", sequencer.CurrentPayload!.Content);
            Assert.Empty(sequencer.CallStack);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }
    }
}
