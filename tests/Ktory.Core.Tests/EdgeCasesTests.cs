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
            controller.NotifyPrintingFinished();
            Assert.True(controller.AutoAdvanceOnHoldEnd);
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
    }
}
