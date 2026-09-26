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
        public void Loop_Zero_EquivalentToOne_ExecutesOnce()
        {
            string script = @"
#choice.loop(0)
  + [重复选项]
    主角: 执行了一次。

主角: 离开循环。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            sequencer.SubmitChoice("重复选项");
            Assert.Equal("执行了一次。", sequencer.CurrentPayload!.Content);

            // After inline step completes, it returns to choice.loop(0).
            // Because max iterations is 1 (equivalent to .loop(1)), loop limit is reached!
            // It must break out to post-loop line!
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("离开循环。", sequencer.CurrentPayload!.Content);
        }

        [Fact]
        public void TextNode_Loop_ReEntersNodeAndReDispatchesTags()
        {
            string script = @"
: 警告！ .loop(3) .sfx(""alarm"")
: 正常继续。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            int sfxCount = 0;
            sequencer.OnTagsDispatched += tags =>
            {
                foreach (var t in tags)
                {
                    if (t.Name == "sfx") sfxCount++;
                }
            };

            sequencer.Start();
            // 1st entry
            Assert.Equal("警告！", sequencer.CurrentPayload!.Content);
            Assert.Equal(1, sfxCount);

            // 2nd entry
            sequencer.Step();
            Assert.Equal("警告！", sequencer.CurrentPayload!.Content);
            Assert.Equal(2, sfxCount);

            // 3rd entry
            sequencer.Step();
            Assert.Equal("警告！", sequencer.CurrentPayload!.Content);
            Assert.Equal(3, sfxCount);

            // Exhausted after 3 physical entries -> advances to next step
            sequencer.Step();
            Assert.Equal("正常继续。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void TextNode_LoopZero_ExecutesOnce()
        {
            string script = @"
: 只说一次。 .loop(0)
: 接着下一句。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("只说一次。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal("接着下一句。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void TextNode_InfiniteLoop_BreaksOnHostSignal()
        {
            string script = @"
: 待机台词。 .loop
: 跳出成功。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("待机台词。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal("待机台词。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal("待机台词。", sequencer.CurrentPayload!.Content);

            // Host sends Break signal
            sequencer.Break();
            Assert.Equal("跳出成功。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void DirectiveNode_Loop_ReEntersDirectiveBeat()
        {
            string script = @"
#shake .loop(2) .camera_shake(0.5)
: 震动结束。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            int shakeTagsCount = 0;
            sequencer.OnTagsDispatched += tags =>
            {
                foreach (var t in tags)
                {
                    if (t.Name == "camera_shake") shakeTagsCount++;
                }
            };

            sequencer.Start();
            Assert.Equal("shake", sequencer.CurrentPayload!.Content);
            Assert.Equal(StepType.Directive, sequencer.CurrentPayload.StepType);
            Assert.Equal(1, shakeTagsCount);

            sequencer.Step();
            Assert.Equal("shake", sequencer.CurrentPayload!.Content);
            Assert.Equal(StepType.Directive, sequencer.CurrentPayload.StepType);
            Assert.Equal(2, shakeTagsCount);

            sequencer.Step();
            Assert.Equal("震动结束。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void AutoDirective_PassesThroughWithoutPause_AndSetsAutoState()
        {
            string script = @"
: 准备。
#AUTO
主角: 自动播放台词一。
主角: 自动播放台词二。
#AUTO_END
主角: 恢复手动。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("准备。", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);

            // Step into AUTO: #AUTO passes through without pause directly to 自动播放台词一
            sequencer.Step();
            Assert.Equal("自动播放台词一。", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);
            Assert.NotNull(sequencer.ActiveAutoPolicy);
            Assert.False(sequencer.ActiveAutoPolicy!.UseEstimatedReadingTime);

            // Next beat in AUTO
            sequencer.Step();
            Assert.Equal("自动播放台词二。", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);

            // Step through #AUTO_END: passes through without extra pause directly to 恢复手动
            sequencer.Step();
            Assert.Equal("恢复手动。", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);
            Assert.Null(sequencer.ActiveAutoPolicy);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void AutoWait_EstimatesReadingTime_AndAllowsLocalOverride()
        {
            string script = @"
#AUTO.wait
: 中文短句。
: 这一句被局部覆盖为固定三秒。 .wait(3)
: 这一句被局部覆盖为立即推进。 .wait(0)
#AUTO_END
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);

            sequencer.Start();
            controller.SetupForCurrentBeat();

            // Beat 1: 中文短句 (uses estimated reading time from #AUTO.wait)
            Assert.Equal("中文短句。", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);
            controller.NotifyPrintingFinished();
            Assert.Equal(PresentationPhase.Holding, controller.Phase);
            Assert.True(controller.AutoAdvanceOnHoldEnd);
            Assert.True(controller.HoldDuration > 0); // estimated reading time

            // Simulate hold end and advance to Beat 2
            controller.Update(controller.HoldDuration);
            // Beat 2: .wait(3) overrides auto default
            Assert.Equal("这一句被局部覆盖为固定三秒。", sequencer.CurrentPayload!.Content);
            controller.NotifyPrintingFinished();
            Assert.Equal(3.0, controller.HoldDuration);

            // Simulate hold end and advance to Beat 3
            controller.Update(3.0);
            // Beat 3: .wait(0) overrides to 0 (immediate advance)
            Assert.Equal("这一句被局部覆盖为立即推进。", sequencer.CurrentPayload!.Content);
            controller.NotifyPrintingFinished();
            // With wait(0), finishes and immediately advances past #AUTO_END to completed!
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void AutoDirective_DoesNotInheritIntoExternalSubroutine_AndRestoresOnReturn()
        {
            string script = @"
: RootStart
#AUTO
: AutoLine1
=> ExternalSub
: AutoLine2
#AUTO_END
: RootEnd

=== ExternalSub ===
: SubLine
-> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("RootStart", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);

            // Step into AUTO: passes through #AUTO to AutoLine1
            sequencer.Step();
            Assert.Equal("AutoLine1", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);

            // Call => ExternalSub: ExternalSub does NOT inherit AUTO!
            sequencer.Step();
            Assert.Equal("SubLine", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);
            Assert.Null(sequencer.ActiveAutoPolicy);

            // Return from ExternalSub -> restores AUTO for AutoLine2!
            sequencer.Step();
            Assert.Equal("AutoLine2", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);
            Assert.NotNull(sequencer.ActiveAutoPolicy);

            // Leaves AUTO through #AUTO_END to RootEnd
            sequencer.Step();
            Assert.Equal("RootEnd", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);
            Assert.Null(sequencer.ActiveAutoPolicy);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
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
