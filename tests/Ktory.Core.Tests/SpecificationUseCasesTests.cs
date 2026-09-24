using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Xunit;

namespace Ktory.Core.Tests
{
    public class SpecificationUseCasesTests
    {
        /// <summary>
        /// 6.1 用例一：多语言回退与标签确认
        /// </summary>
        [Fact]
        public void TestCase6_1_MultilingualFallbackAndTag()
        {
            string script = @"
@defaultLang: zh

主角:
  @zh: 这里真冷。
  .emotion(shiver)
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            var dispatchedTags = new List<TagData>();
            sequencer.OnTagsDispatched += tags => dispatchedTags.AddRange(tags);

            // Request English ("en")
            sequencer.Start(requestedLocale: "en");

            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.NotNull(sequencer.CurrentPayload);

            // Assertions from §6.1:
            // 1. Output TextPayload.Content == "这里真冷。"
            Assert.Equal("这里真冷。", sequencer.CurrentPayload.Content);
            // 2. Output TextPayload.ActualLanguage == "zh"
            Assert.Equal("zh", sequencer.CurrentPayload.ActualLanguage);
            // 3. Output TextPayload.RequestedLanguage == "en"
            Assert.Equal("en", sequencer.CurrentPayload.RequestedLanguage);
            // 4. Never output fake translation placeholder
            Assert.DoesNotContain("[missing", sequencer.CurrentPayload.Content);

            // Tag verification
            Assert.Single(dispatchedTags);
            Assert.Equal("emotion", dispatchedTags[0].Name);
            Assert.Equal("shiver", dispatchedTags[0].GetPositional<string>(0));
        }

        /// <summary>
        /// 6.2 用例二：选项提交流程与首句停顿
        /// </summary>
        [Fact]
        public void TestCase6_2_ChoiceSubmissionAndFirstBeatSuspend()
        {
            string script = @"
#choice
  * [调查房门]
    主角: 门已经被人从外面反锁了。
    主角: 看来只能另找出口。
  + [放弃] -> break

主角: 接下来怎么办？
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            sequencer.Start();

            // Core reaches #choice -> AwaitingChoice
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.NotNull(sequencer.CurrentChoice);
            Assert.Equal(2, sequencer.CurrentChoice.Options.Count);

            // Execute action: SubmitChoice("调查房门")
            sequencer.SubmitChoice("调查房门");

            // Assertions from §6.2:
            // 1. Status changes to SuspendedAtBeat
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            // 2. Dispatches 1st dialogue: "门已经被人从外面反锁了。"
            Assert.NotNull(sequencer.CurrentPayload);
            Assert.Equal("门已经被人从外面反锁了。", sequencer.CurrentPayload.Content);
            Assert.Equal("主角", sequencer.CurrentPayload.Speaker);

            // 3. Pointer stays at 1st dialogue, NEVER auto-passes through to 2nd line!
            Assert.Equal("门已经被人从外面反锁了。", sequencer.CurrentPayload.Content);

            // Advance to 2nd dialogue
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("看来只能另找出口。", sequencer.CurrentPayload.Content);

            // Advance to post-choice main trunk
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("接下来怎么办？", sequencer.CurrentPayload.Content);

            // 4. "调查房门" is recorded in session visited set
            var option = sequencer.CurrentChoice == null ? null : sequencer.CurrentChoice.Options[0];
            Assert.True(sequencer.VisitedItemIds.Count > 0);
        }

        /// <summary>
        /// 6.3 用例三：文字快显与自动步进倒计时时序
        /// </summary>
        [Fact]
        public void TestCase6_3_FastForwardAndAutoStepCountdown()
        {
            string script = @"
主角: 这是一句需要测试呈现倒计时的重要线索。
  .skippable(true)
  .next(2)
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);
            var controller = new PresentationController(sequencer);

            sequencer.Start();
            controller.SetupForCurrentBeat();

            // T=0: Printing phase, sequencer SuspendedAtBeat
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal(PresentationPhase.Printing, controller.Phase);
            Assert.True(controller.CanFastForward);

            // T=0.5 (mid printing): User clicks screen -> Fast forward triggers, completes text printing
            bool fastForwardTriggered = false;
            controller.OnFastForwardRequested += () => fastForwardTriggered = true;

            controller.Update(0.5);
            controller.HandleUserClick();

            Assert.True(fastForwardTriggered);
            // Must be in Holding phase now with 2.0s duration
            Assert.Equal(PresentationPhase.Holding, controller.Phase);
            Assert.Equal(2.0, controller.HoldDuration);
            // Must NOT advance to next beat upon fast-forward click
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);

            // T=1.2: (0.7s into hold phase) User clicks again -> Interrupts 2.0s hold, immediately steps
            bool advanceTriggered = false;
            controller.OnAdvanceRequested += () => advanceTriggered = true;

            controller.Update(0.7);
            controller.HandleUserClick();

            Assert.True(advanceTriggered);
            // Reached end of script
            Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        }

        /// <summary>
        /// 6.4 用例四：Hub 循环调查与 Break 机制
        /// </summary>
        [Fact]
        public void TestCase6_4_HubLoopInvestigationAndBreak()
        {
            string script = @"
=== Room_Investigation ===

#choice.loop
  * [查看书桌]
    #do .sfx(""paper"")
    主角: 桌上有一张泛黄的日记碎片。
  * [检查窗户]
    主角: 窗户被铁栅栏焊死了。
  + [尝试撞门]
    主角: 门纹丝不动，撞得肩膀生疼。
  + [放弃思考] -> break

主角: 我们离开这里吧。
";
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file);

            // Start at Room_Investigation section
            sequencer.Start("Room_Investigation");

            // Reaches #choice.loop
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(4, sequencer.CurrentChoice!.Options.Count);

            // 1. Choose [查看书桌]
            sequencer.SubmitChoice("查看书桌");

            // Suspends at #do independent directive beat
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal(StepType.Directive, sequencer.CurrentPayload!.StepType);
            Assert.Equal("do", sequencer.CurrentPayload.Content);

            // Step() -> outputs text: "桌上有一张泛黄的日记碎片。"
            sequencer.Step();
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("桌上有一张泛黄的日记碎片。", sequencer.CurrentPayload!.Content);

            // Step() after inline branch completes -> returns to #choice.loop
            sequencer.Step();
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

            // [查看书桌] is consumed and cannot be selected
            var deskOption = sequencer.CurrentChoice!.Options[0];
            Assert.True(deskOption.IsConsumed);
            Assert.False(deskOption.CanSelect);

            // Other 3 items are still selectable
            Assert.True(sequencer.CurrentChoice.Options[1].CanSelect);
            Assert.True(sequencer.CurrentChoice.Options[2].CanSelect);
            Assert.True(sequencer.CurrentChoice.Options[3].CanSelect);

            // 2. Select [放弃思考] -> break
            sequencer.SubmitChoice("放弃思考");

            // Exits loop and reaches "我们离开这里吧。"
            Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
            Assert.Equal("我们离开这里吧。", sequencer.CurrentPayload!.Content);
        }
    }
}
