using Ktory.Core.Ast;
using Ktory.Core.Common;
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

        [Fact]
        public void Sequencer_SubroutineWithLoop_CalledTwice_LoopContextResetsEachTime()
        {
            string script = @"
=> Sub
: 两次调用之间。
=> Sub
: 完成。
-> end

=== Sub ===
#choice.loop(1)
  + [返回主线] -> return
: 不应该因为上次调用而跳过菜单。
-> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            // First call to Sub: should await choice on Sub's #choice.loop(1)
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(1, sequencer.ActiveLoopCount);
            Assert.Contains(sequencer.CurrentChoice!.Options, o => o.Label == "返回主线");

            // Select option returning to main line
            sequencer.SubmitChoice("返回主线");
            // After return, Sub's active loop should be popped and caller resumes
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("两次调用之间。", sequencer.CurrentPayload!.Content);
            Assert.Equal(0, sequencer.ActiveLoopCount);

            // Step to second => Sub invocation
            sequencer.Step();
            // Second call to Sub: must NOT skip the menu! Loop context should have reset.
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(1, sequencer.ActiveLoopCount);
            Assert.Contains(sequencer.CurrentChoice!.Options, o => o.Label == "返回主线");

            // Select option again
            sequencer.SubmitChoice("返回主线");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("完成。", sequencer.CurrentPayload!.Content);
            Assert.Equal(0, sequencer.ActiveLoopCount);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_OuterLoopCallsSubroutineWithInnerLoop_InnerReturnPreservesOuterLoop()
        {
            string script = @"
#choice.loop(2)
  + [调用子过程] => Sub
  + [退出] -> break
: 外部循环结束。
-> end

=== Sub ===
#choice.loop(1)
  + [子过程返回] -> return
: 子过程结束。
-> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            // Initially in outer loop
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(1, sequencer.ActiveLoopCount);

            // 1st iteration: call Sub
            sequencer.SubmitChoice("调用子过程");
            // Inside Sub: inner loop is active
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(2, sequencer.ActiveLoopCount);

            // Sub returns
            sequencer.SubmitChoice("子过程返回");
            // Returned to outer loop: inner loop released, outer loop still active!
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(1, sequencer.ActiveLoopCount);

            // Break from outer loop to verify caller's break targets outer loop correctly
            sequencer.SubmitChoice("退出");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("外部循环结束。", sequencer.CurrentPayload!.Content);
            Assert.Equal(0, sequencer.ActiveLoopCount);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void AutoDirective_BreakFromSubroutine_RestoresCallerAutoScope()
        {
            string script = @"
#AUTO
#choice.loop
  + [进入子过程] => Sub
: 这里仍在 AUTO 区域内。
#AUTO_END
-> end

=== Sub ===
  -> break
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            // Subroutine performs -> break to exit outer choice.loop
            sequencer.SubmitChoice("进入子过程");

            // Must land on '这里仍在 AUTO 区域内。' and remain in AUTO mode!
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("这里仍在 AUTO 区域内。", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);
            Assert.NotNull(sequencer.ActiveAutoPolicy);

            // Verify PresentationController automatically advances in AUTO
            controller.SetupForCurrentBeat();
            Assert.True(controller.AutoAdvanceOnHoldEnd);
            controller.NotifyPrintingFinished();
            // Zero-delay AUTO completes synchronously; diagnostics must now describe the ended session.
            Assert.False(controller.AutoAdvanceOnHoldEnd);
            controller.Update(controller.HoldDuration);

            // Automatically advances past #AUTO_END to completed
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
            Assert.Null(sequencer.ActiveAutoPolicy);
        }

        [Fact]
        public void AutoDirective_SubroutineAuto_DoesNotLeakToCallerOnBreak()
        {
            string script = @"
: 外部常规。
#choice.loop
  + [进入子过程] => Sub
: 外部仍常规。
-> end

=== Sub ===
  #AUTO
  : 子过程自动。
  -> break
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("外部常规。", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);

            sequencer.Step(); // enter choice
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            sequencer.SubmitChoice("进入子过程");
            // Inside Sub: AUTO is active
            Assert.Equal("子过程自动。", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);
            Assert.NotNull(sequencer.ActiveAutoPolicy);

            // Sub breaks back to caller
            sequencer.Step();
            // Lands on '外部仍常规。': caller's lexical scope is manual mode, NO leak!
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("外部仍常规。", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);
            Assert.Null(sequencer.ActiveAutoPolicy);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void AutoDirective_SubroutineAuto_DoesNotLeakToCallerOnReturn()
        {
            string script = @"
: 外部常规1。
=> Sub
: 外部常规2。
-> end

=== Sub ===
  #AUTO
  : 子过程自动。
  -> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal("外部常规1。", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);

            sequencer.Step(); // calls Sub
            Assert.Equal("子过程自动。", sequencer.CurrentPayload!.Content);
            Assert.True(sequencer.CurrentPayload.IsAuto);
            Assert.NotNull(sequencer.ActiveAutoPolicy);

            sequencer.Step(); // Sub returns to caller
            Assert.Equal("外部常规2。", sequencer.CurrentPayload!.Content);
            Assert.False(sequencer.CurrentPayload.IsAuto);
            Assert.Null(sequencer.ActiveAutoPolicy);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_DefaultStart_StartsAtRootSection_EvenWithSubroutineSections()
        {
            string script = @"
: RootOpening
=> Sub
: RootEnding
-> end

=== Sub ===
: InsideSub
-> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            // Default start without entryLabel MUST start at RootOpening, NOT Sub!
            sequencer.Start();
            Assert.Equal("RootOpening", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // calls Sub
            Assert.Equal("InsideSub", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // returns to RootEnding
            Assert.Equal("RootEnding", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Sequencer_DefaultStart_WhenNoRootSection_StartsAtFirstNamedSection()
        {
            string script = @"
=== SectionA ===
: In Section A
-> end

=== SectionB ===
: In Section B
-> end
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            // When script has no root nodes, starts at first named section
            sequencer.Start();
            Assert.Equal("In Section A", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);

            // Explicitly choosing SectionB starts at SectionB
            var sequencer2 = new KtorySequencer(file);
            sequencer2.Start("SectionB");
            Assert.Equal("In Section B", sequencer2.CurrentPayload!.Content);

            sequencer2.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer2.Status);
        }

        [Fact]
        public void WasmBridge_Start_WithAutoDirective_SerializesWithoutCircularReference()
        {
            string script = @"
#AUTO
: 自动播放第一句。
#AUTO_END
";
            var bridge = new Ktory.Web.KtoryWasmBridge();
            string json = bridge.Start(script, "zh", null);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("SuspendedAtBeat", root.GetProperty("status").GetString());

            var payload = root.GetProperty("payload");
            Assert.Equal("自动播放第一句。", payload.GetProperty("content").GetString());

            // payload.autoPolicy
            Assert.True(payload.TryGetProperty("autoPolicy", out var payloadAutoPolicy));
            Assert.True(payloadAutoPolicy.GetProperty("enabled").GetBoolean());
            Assert.False(payloadAutoPolicy.GetProperty("useEstimatedReadingTime").GetBoolean());
            Assert.Equal(0, payloadAutoPolicy.GetProperty("defaultWaitSeconds").GetDouble());
            Assert.False(payloadAutoPolicy.TryGetProperty("scopeBlock", out _));

            // top-level autoPolicy
            Assert.True(root.TryGetProperty("autoPolicy", out var rootAutoPolicy));
            Assert.True(rootAutoPolicy.GetProperty("enabled").GetBoolean());
            Assert.False(rootAutoPolicy.GetProperty("useEstimatedReadingTime").GetBoolean());
            Assert.Equal(0, rootAutoPolicy.GetProperty("defaultWaitSeconds").GetDouble());
            Assert.False(rootAutoPolicy.TryGetProperty("scopeBlock", out _));

            // Step through AUTO_END to completion
            string stepJson = bridge.Step();
            using var stepDoc = System.Text.Json.JsonDocument.Parse(stepJson);
            Assert.Equal("Completed", stepDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, stepDoc.RootElement.GetProperty("autoPolicy").ValueKind);
        }

        [Fact]
        public void WasmBridge_AutoWait_SerializesPolicySettingsCorrectly()
        {
            string script = @"
#AUTO.wait
: 估算阅读时间。
#AUTO.wait(3.5)
: 固定3.5秒。
#AUTO_END
";
            var bridge = new Ktory.Web.KtoryWasmBridge();
            string json1 = bridge.Start(script, "zh", null);
            using (var doc1 = System.Text.Json.JsonDocument.Parse(json1))
            {
                var payload1 = doc1.RootElement.GetProperty("payload");
                Assert.Equal("估算阅读时间。", payload1.GetProperty("content").GetString());

                var pPolicy1 = payload1.GetProperty("autoPolicy");
                Assert.True(pPolicy1.GetProperty("enabled").GetBoolean());
                Assert.True(pPolicy1.GetProperty("useEstimatedReadingTime").GetBoolean());
                Assert.Equal(0, pPolicy1.GetProperty("defaultWaitSeconds").GetDouble());

                var rPolicy1 = doc1.RootElement.GetProperty("autoPolicy");
                Assert.True(rPolicy1.GetProperty("useEstimatedReadingTime").GetBoolean());
            }

            string json2 = bridge.Step();
            using (var doc2 = System.Text.Json.JsonDocument.Parse(json2))
            {
                var payload2 = doc2.RootElement.GetProperty("payload");
                Assert.Equal("固定3.5秒。", payload2.GetProperty("content").GetString());

                var pPolicy2 = payload2.GetProperty("autoPolicy");
                Assert.True(pPolicy2.GetProperty("enabled").GetBoolean());
                Assert.False(pPolicy2.GetProperty("useEstimatedReadingTime").GetBoolean());
                Assert.Equal(3.5, pPolicy2.GetProperty("defaultWaitSeconds").GetDouble());

                var rPolicy2 = doc2.RootElement.GetProperty("autoPolicy");
                Assert.False(rPolicy2.GetProperty("useEstimatedReadingTime").GetBoolean());
                Assert.Equal(3.5, rPolicy2.GetProperty("defaultWaitSeconds").GetDouble());
            }
        }

        [Fact]
        public void OptionBranch_StartingWithSubroutineCall_ExecutesSubroutineThenRemainderOfBranch()
        {
            string script = @"
#choice
  * [查看线索]
    => Detail
    : 查看之后，我有了新的想法。
: 继续主线。

=== Detail ===
  : 这里是一条线索。
  -> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            // Select option -> enters Detail subroutine first
            sequencer.SubmitChoice("查看线索");
            Assert.Equal("这里是一条线索。", sequencer.CurrentPayload!.Content);

            // Step in Detail executes -> return, resumes inline branch to step 2
            sequencer.Step();
            Assert.Equal("查看之后，我有了新的想法。", sequencer.CurrentPayload!.Content);

            // Step finishes inline branch, resumes root main line
            sequencer.Step();
            Assert.Equal("继续主线。", sequencer.CurrentPayload!.Content);

            // Step finishes root block
            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void OptionBranch_StartingWithSubroutineCall_InsideLoopContainer_ReturnsToLoopAfterBranch()
        {
            string script = @"
#choice.loop
  * [查看线索]
    => Detail
    : 查看之后，我有了新的想法。
  + [离开] -> break
: 继续主线。

=== Detail ===
  : 这里是一条线索。
  -> return
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            // 1. First iteration: choose [查看线索]
            sequencer.SubmitChoice("查看线索");
            Assert.Equal("这里是一条线索。", sequencer.CurrentPayload!.Content);

            sequencer.Step(); // -> return -> remainder of branch
            Assert.Equal("查看之后，我有了新的想法。", sequencer.CurrentPayload!.Content);

            // Finishes branch -> loops back to choice menu
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.False(sequencer.CurrentChoice!.Options[0].CanSelect); // * consumed
            Assert.True(sequencer.CurrentChoice.Options[1].CanSelect); // + available

            // 2. Select [离开] -> break out of loop
            sequencer.SubmitChoice("离开");
            Assert.Equal("继续主线。", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void Option_JumpShorthandCombinedWithIndentedBody_ThrowsParseException()
        {
            string script = @"
#choice
  * [离开] -> break
    : 这行文字不应该存在
";
            var ex = Assert.Throws<KtoryException>(() => KtoryParser.Parse(script));
            Assert.Contains("cannot be combined with an indented branch body", ex.Message);
        }

        [Fact]
        public void Sequencer_EndlessControlFlowLoop_ThrowsInstructionBudgetExceededException()
        {
            string script = @"
-> Again

=== Again ===
  -> Again
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            sequencer.MaxInstructionBudgetPerAdvance = 50; // Use small budget for test speed

            var ex = Assert.Throws<KtoryInstructionBudgetExceededException>(() => sequencer.Start());

            Assert.Equal(ExecutionStatus.Error, sequencer.Status);
            Assert.Contains("Instruction budget of 50 exceeded", ex.Message);
            Assert.Contains("Again", ex.Message);
            Assert.Contains("Recent execution path", ex.Message);
        }

        [Fact]
        public void Sequencer_NormalLoopWithDialogueAndChoices_ExecutesWithoutHittingBudget()
        {
            string script = @"
#choice.loop
  * [选项一]
    : 对白一
  + [退出]
    -> break
: 主线结束
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            sequencer.MaxInstructionBudgetPerAdvance = 20; // Budget reset per beat

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            sequencer.SubmitChoice("选项一");
            Assert.Equal("对白一", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            sequencer.SubmitChoice("退出");
            Assert.Equal("主线结束", sequencer.CurrentPayload!.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void PresentationController_ZeroSecondDirectiveLoop_DoesNotCrashStackAndLimitsPerBatch()
        {
            string script = @"
#do.loop.next
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);
            controller.MaxAutoAdvancesPerBatch = 32;

            int limitFiredCount = 0;
            int limitAdvancesReported = 0;
            controller.OnBatchAdvanceLimitReached += count =>
            {
                limitFiredCount++;
                limitAdvancesReported = count;
            };

            sequencer.Start();
            // In previous implementation, SetupForCurrentBeat() would recursively call itself and crash with StackOverflowException
            controller.SetupForCurrentBeat();

            Assert.Equal(1, limitFiredCount);
            Assert.Equal(32, limitAdvancesReported);
            Assert.True(controller.HasDeferredAdvance);
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);

            // Simulating next frame in Unity Update(): processes next batch
            controller.Update(0.016);
            Assert.Equal(2, limitFiredCount);
            Assert.True(controller.HasDeferredAdvance);
        }

        [Fact]
        public void PresentationController_ChainedZeroSecondDirectives_ExecutesBatchThenStopsAtDialogue()
        {
            string script = @"
#bg(office).next
#se(rain).next
#chara(alice).next
: Alice ""你好！""
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);

            int advanceCount = 0;
            controller.OnAdvanceRequested += () => advanceCount++;

            sequencer.Start();
            controller.SetupForCurrentBeat();

            // 3 directives auto-advanced in batch
            Assert.Equal(3, advanceCount);
            Assert.False(controller.HasDeferredAdvance);
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal(PresentationPhase.Printing, controller.Phase);
            Assert.Equal("Alice \"你好！\"", sequencer.CurrentPayload!.Content);

            // Dialogue finishes printing and enters Holding
            controller.NotifyPrintingFinished();
            Assert.Equal(PresentationPhase.Holding, controller.Phase);

            // User click advances to Completed
            controller.HandleUserClick();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        [Fact]
        public void LexicalScanner_DotInMiddleOfText_IsNotMistakenForTrailingTag()
        {
            // Case 1: Plain text with extension in middle
            string script1 = @": 请打开 .ktr 文件。";
            var file1 = KtoryParser.Parse(script1);
            var step1 = Assert.IsType<TextStep>(file1.RootBlock.Steps[0]);
            Assert.Equal("请打开 .ktr 文件。", step1.TextVariants["zh"]);
            Assert.Empty(step1.Tags);

            // Case 2: Extension in middle followed by genuine trailing tag
            string script2 = @": 请打开 .ktr 文件。 .wait(2)";
            var file2 = KtoryParser.Parse(script2);
            var step2 = Assert.IsType<TextStep>(file2.RootBlock.Steps[0]);
            Assert.Equal("请打开 .ktr 文件。", step2.TextVariants["zh"]);
            Assert.Single(step2.Tags);
            Assert.Equal("wait", step2.Tags[0].Name);
            Assert.Equal(2L, step2.Tags[0].PositionalArgs[0]);

            // Case 3: Dot without whitespace
            string script3 = @": 请查看config.ktr文件";
            var file3 = KtoryParser.Parse(script3);
            var step3 = Assert.IsType<TextStep>(file3.RootBlock.Steps[0]);
            Assert.Equal("请查看config.ktr文件", step3.TextVariants["zh"]);
            Assert.Empty(step3.Tags);
        }

        [Fact]
        public void LexicalScanner_UrlSchemeDoubleSlash_IsNotTreatedAsComment()
        {
            // URL in unquoted dialogue
            string script = @": 文档地址 https://example.com/guide // 真实注释";
            var file = KtoryParser.Parse(script);
            var step = Assert.IsType<TextStep>(file.RootBlock.Steps[0]);
            Assert.Equal("文档地址 https://example.com/guide", step.TextVariants["zh"]);

            // http and file URLs
            string script2 = @": 下载地址 http://ktory.org 以及 file:///root/doc";
            var file2 = KtoryParser.Parse(script2);
            var step2 = Assert.IsType<TextStep>(file2.RootBlock.Steps[0]);
            Assert.Equal("下载地址 http://ktory.org 以及 file:///root/doc", step2.TextVariants["zh"]);
        }

        [Fact]
        public void LexicalScanner_ApostropheInContractions_DoesNotBlockTrailingTagsOrComments()
        {
            // Case 1: English dialogue with apostrophe and trailing decorator
            string script1 = @"Alice: I'm ready. .emotion(smile)";
            var file1 = KtoryParser.Parse(script1);
            var step1 = Assert.IsType<TextStep>(file1.RootBlock.Steps[0]);
            Assert.Equal("Alice", step1.Speaker);
            Assert.Equal("I'm ready.", step1.TextVariants["zh"]);
            Assert.Single(step1.Tags);
            Assert.Equal("emotion", step1.Tags[0].Name);
            Assert.Equal("smile", step1.Tags[0].PositionalArgs[0]);

            // Case 2: Multiple contractions and multiple tags
            string script2 = @": It's Bob's turn, don't rush. .wait(1) .sfx('chime')";
            var file2 = KtoryParser.Parse(script2);
            var step2 = Assert.IsType<TextStep>(file2.RootBlock.Steps[0]);
            Assert.Equal("It's Bob's turn, don't rush.", step2.TextVariants["zh"]);
            Assert.Equal(2, step2.Tags.Count);
            Assert.Equal("wait", step2.Tags[0].Name);
            Assert.Equal("sfx", step2.Tags[1].Name);
            Assert.Equal("chime", step2.Tags[1].PositionalArgs[0]);

            // Case 3: Apostrophe followed by comment
            string script3 = @"Alice: I'm ready. // ready comment";
            var file3 = KtoryParser.Parse(script3);
            var step3 = Assert.IsType<TextStep>(file3.RootBlock.Steps[0]);
            Assert.Equal("I'm ready.", step3.TextVariants["zh"]);
        }

        [Fact]
        public void StaticValidator_DuplicateSections_ThrowsParseException()
        {
            string script = @"
=== SectionA ===
: 台词1

=== SectionA ===
: 台词2
";
            var ex = Assert.Throws<KtoryException>(() => KtoryParser.Parse(script));
            Assert.Contains("Duplicate section '=== SectionA ==='", ex.Message);

            string scriptRoot = @"
=== root ===
: 试图重写root
";
            var exRoot = Assert.Throws<KtoryException>(() => KtoryParser.Parse(scriptRoot));
            Assert.Contains("Section label 'root' is reserved", exRoot.Message);
        }

        [Fact]
        public void StaticValidator_UnresolvedJumpOrCallTarget_ThrowsParseException()
        {
            // Jump to nonexistent section
            string scriptJump = @"
: 开始
-> MissingSection
";
            var exJump = Assert.Throws<KtoryException>(() => KtoryParser.Parse(scriptJump));
            Assert.Contains("Target section '=== MissingSection ===' not found in file", exJump.Message);

            // Call to nonexistent section
            string scriptCall = @"
: 开始
=> NonExistentSub
";
            var exCall = Assert.Throws<KtoryException>(() => KtoryParser.Parse(scriptCall));
            Assert.Contains("Target section '=== NonExistentSub ===' not found in file", exCall.Message);

            // Choice option pointing to nonexistent section
            string scriptChoice = @"
#choice
  * [去不存在的地方] -> Nowhere
";
            var exChoice = Assert.Throws<KtoryException>(() => KtoryParser.Parse(scriptChoice));
            Assert.Contains("Target section '=== Nowhere ===' not found in file", exChoice.Message);
        }

        [Fact]
        public void StaticValidator_UnindentedSection_ConflictingLinesAfterTerminal_ThrowsParseException()
        {
            // SubSection is unindented, capturing lines after -> return
            string script = @"
: 根节第一句。

=== SubSection ===
艾莉丝: 命名节台词。
-> return

主角: 根节第二句。
";
            var ex = Assert.Throws<KtoryException>(() => KtoryParser.Parse(script));
            Assert.Contains("Unreachable code or conflicting section boundary", ex.Message);
            Assert.Contains("Please indent the section body", ex.Message);
        }

        [Fact]
        public void PresentationId_IncrementsMonotonically_AcrossBeatsAndChoices()
        {
            string script = @"
: 第一句
: 第二句
#choice
  * [选A]
    : A的结果
  * [选B]
    : B的结果
: 结束前最后一句
-> end
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(1, sequencer.CurrentPresentationId);
            Assert.Equal(1, sequencer.CurrentPayload!.PresentationId);

            sequencer.Step();
            Assert.Equal(2, sequencer.CurrentPresentationId);
            Assert.Equal(2, sequencer.CurrentPayload!.PresentationId);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(3, sequencer.CurrentPresentationId);
            Assert.Equal(3, sequencer.CurrentChoice!.PresentationId);

            sequencer.SubmitChoice("选A");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal(4, sequencer.CurrentPresentationId);
            Assert.Equal(4, sequencer.CurrentPayload!.PresentationId);
            Assert.Equal("A的结果", sequencer.CurrentPayload.Content);

            sequencer.Step();
            Assert.Equal(5, sequencer.CurrentPresentationId);
            Assert.Equal(5, sequencer.CurrentPayload!.PresentationId);
            Assert.Equal("结束前最后一句", sequencer.CurrentPayload.Content);

            sequencer.Step();
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
            Assert.Equal(0, sequencer.CurrentPresentationId);
            Assert.Null(sequencer.CurrentPayload);
            Assert.Null(sequencer.CurrentChoice);
        }

        [Fact]
        public void PresentationId_IncrementsOnLoopReplay_SameSourceLine()
        {
            string script = @"
#choice.loop(2)
  + [再次倾听]
    : 重播台词。
  + [离开]
    -> end
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            long menuId1 = sequencer.CurrentPresentationId;
            Assert.Equal(1, menuId1);

            // Iteration 1 of choice
            sequencer.SubmitChoice("再次倾听");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            long textId1 = sequencer.CurrentPresentationId;
            Assert.Equal(2, textId1);
            Assert.Equal(2, sequencer.CurrentPayload!.PresentationId);
            int lineNumber1 = sequencer.CurrentPayload.LineNumber;

            // Step back to loop
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            long menuId2 = sequencer.CurrentPresentationId;
            Assert.True(menuId2 > menuId1);
            Assert.Equal(3, menuId2);

            // Iteration 2 of choice: replays the exact same source line!
            sequencer.SubmitChoice("再次倾听");
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            long textId2 = sequencer.CurrentPresentationId;
            Assert.Equal(4, textId2);
            Assert.Equal(4, sequencer.CurrentPayload!.PresentationId);
            int lineNumber2 = sequencer.CurrentPayload.LineNumber;

            // Source line number is the exact same, but PresentationId is strictly newer
            Assert.Equal(lineNumber1, lineNumber2);
            Assert.NotEqual(textId1, textId2);
            Assert.True(textId2 > textId1);
        }

        [Fact]
        public void PresentationId_StaleStepAndChoice_IgnoredOrRejected()
        {
            string script = @"
: 第一句
#choice
  * [选项1]
    : 选项1正文
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();
            long pres1 = sequencer.CurrentPresentationId;
            Assert.Equal(1, pres1);

            // Advance with stale expected presentation id -> ignored, status unchanged
            sequencer.Step(expectedPresentationId: 999);
            Assert.Equal(1, sequencer.CurrentPresentationId);
            Assert.Equal("第一句", sequencer.CurrentPayload!.Content);

            // Advance with correct expected presentation id -> succeeds
            sequencer.Step(expectedPresentationId: pres1);
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            long pres2 = sequencer.CurrentPresentationId;
            Assert.Equal(2, pres2);

            // Submit choice with stale presentation id -> throws KtoryControlFlowException
            Assert.Throws<KtoryControlFlowException>(() => sequencer.SubmitChoice("选项1", expectedPresentationId: pres1));

            // Submit choice with correct presentation id -> succeeds
            sequencer.SubmitChoice("选项1", expectedPresentationId: pres2);
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal(3, sequencer.CurrentPresentationId);
            Assert.Equal("选项1正文", sequencer.CurrentPayload!.Content);
        }

        [Fact]
        public void PresentationId_PreservedDuringLanguageSwitch()
        {
            string script = @"
@defaultLang: zh
@speaker alice: zh=""爱丽丝"" | en=""Alice""

alice:
  @zh: 中文台词
  @en: English line

#choice
  * [@zh: ""选项一""] [@en: ""Option 1""]
    : 结束
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start(requestedLocale: "zh");
            Assert.Equal(1, sequencer.CurrentPresentationId);
            Assert.Equal("爱丽丝", sequencer.CurrentPayload!.Speaker);
            Assert.Equal("中文台词", sequencer.CurrentPayload.Content);

            // Switch language while suspended at text beat
            sequencer.SetLanguage("en");
            Assert.Equal(1, sequencer.CurrentPresentationId);
            Assert.Equal(1, sequencer.CurrentPayload!.PresentationId);
            Assert.Equal("Alice", sequencer.CurrentPayload.Speaker);
            Assert.Equal("English line", sequencer.CurrentPayload.Content);

            // Advance to choice
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(2, sequencer.CurrentPresentationId);
            Assert.Equal(2, sequencer.CurrentChoice!.PresentationId);
            Assert.Equal("Option 1", sequencer.CurrentChoice.Options[0].Label);

            // Switch language while awaiting choice
            sequencer.SetLanguage("zh");
            Assert.Equal(2, sequencer.CurrentPresentationId);
            Assert.Equal(2, sequencer.CurrentChoice!.PresentationId);
            Assert.Equal("选项一", sequencer.CurrentChoice.Options[0].Label);
        }

        [Fact]
        public void PresentationController_SubscriberCallingStep_DoesNotDoubleStep()
        {
            string script = @"
: 第一句
: 第二句
: 第三句
-> end
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);

            // Natural subscriber pattern that previously triggered double stepping
            controller.OnAdvanceRequested += () => sequencer.Step();

            sequencer.Start();
            controller.SetupForCurrentBeat();
            Assert.Equal("第一句", sequencer.CurrentPayload!.Content);

            // User finishes printing and clicks to advance
            controller.NotifyPrintingFinished();
            controller.HandleUserClick();

            // Must advance to Beat 2, NOT skip to Beat 3!
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("第二句", sequencer.CurrentPayload!.Content);

            // Next advance to Beat 3
            controller.NotifyPrintingFinished();
            controller.HandleUserClick();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("第三句", sequencer.CurrentPayload!.Content);
        }

        [Fact]
        public void PresentationController_HostManagedAdvance_AutoStepSequencerFalse()
        {
            string script = @"
: 第一句
: 第二句
-> end
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer)
            {
                AutoStepSequencer = false
            };

            bool advanceRequested = false;
            controller.OnAdvanceRequested += () => advanceRequested = true;

            sequencer.Start();
            controller.SetupForCurrentBeat();
            Assert.Equal("第一句", sequencer.CurrentPayload!.Content);

            // User clicks to advance
            controller.NotifyPrintingFinished();
            controller.HandleUserClick();

            // Intent emitted, but sequencer NOT stepped yet
            Assert.True(advanceRequested);
            Assert.Equal("第一句", sequencer.CurrentPayload!.Content);

            // Host executes step and refreshes controller
            sequencer.Step();
            controller.SetupForCurrentBeat();
            Assert.Equal("第二句", sequencer.CurrentPayload!.Content);
        }

        [Fact]
        public void PresentationController_FastForward_NotifyPrintingFinishedInCallback_DoesNotDoubleAdvanceZeroSecondAuto()
        {
            string script = @"
: 第一句 .wait(0).next
: 第二句 .wait(0).next
: 第三句
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);

            // UI callback naturally calling NotifyPrintingFinished when fast-forward completes text printing
            controller.OnFastForwardRequested += () => controller.NotifyPrintingFinished();

            sequencer.Start();
            controller.SetupForCurrentBeat();
            Assert.Equal("第一句", sequencer.CurrentPayload!.Content);

            // Trigger fast forward during printing
            controller.HandleUserClick();

            // Must advance to 第二句 and NOT skip over 第二句 to 第三句!
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("第二句", sequencer.CurrentPayload!.Content);
        }

        [Fact]
        public void PresentationController_RefreshLanguage_PreservesProgressAndRecalculatesReadingTime()
        {
            string script = @"
@defaultLang: zh
@speaker alice: zh=""爱丽丝"" | en=""Alice""

alice:
  @zh: 中文长句子用于估算阅读时间测试。
  @en: A much longer English sentence that will take more time to read when calculating reading time.
  .wait
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);

            sequencer.Start(requestedLocale: "zh");
            controller.SetupForCurrentBeat();
            Assert.Equal(PresentationPhase.Printing, controller.Phase);

            // 1. Refreshing while printing: completes printing and enters holding phase
            sequencer.SetLanguage("en");
            controller.RefreshLanguage(completePrintingOnLanguageSwitch: true);
            Assert.Equal(PresentationPhase.Holding, controller.Phase);
            double enHoldDuration = controller.HoldDuration;
            Assert.True(enHoldDuration > 0);

            // 2. Refreshing while holding: preserves elapsed progress ratio
            controller.Update(1.0);
            double elapsedBefore = controller.ElapsedInPhase;
            Assert.Equal(1.0, elapsedBefore);

            sequencer.SetLanguage("zh");
            controller.RefreshLanguage();
            Assert.Equal(PresentationPhase.Holding, controller.Phase);
            // Elapsed is scaled proportionally with the new Chinese estimated reading time, not reset to 0
            Assert.True(controller.ElapsedInPhase > 0);
            Assert.True(controller.ElapsedInPhase < controller.HoldDuration);
        }
    }
}
