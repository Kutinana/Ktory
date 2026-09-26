using System;
using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Common;

namespace Ktory.Core.Runtime
{
    public class KtorySequencer
    {
        public KtoryFile File { get; }
        public string RequestedLanguage { get; private set; } = "zh";
        public string DefaultLanguage => File.DefaultLang;

        public ExecutionStatus Status { get; private set; } = ExecutionStatus.Ready;
        public TextPayload? CurrentPayload { get; private set; }
        public ChoicePayload? CurrentChoice { get; private set; }
        public AutoPolicy? ActiveAutoPolicy { get; private set; }

        public IExpressionEvaluator Evaluator { get; set; } = new DefaultExpressionEvaluator();

        // Session state
        public HashSet<string> VisitedItemIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Stack<CallFrame> CallStack { get; } = new Stack<CallFrame>();

        // Active loop stack: (LoopContext, ResumeFrame, CallStackDepthAtEntry)
        private readonly Stack<(LoopContext Loop, CallFrame ResumeFrame, int CallStackDepth)> _activeLoops = new Stack<(LoopContext, CallFrame, int)>();
        public int ActiveLoopCount => _activeLoops.Count;

        // Current execution pointer
        private KtoryBlock _currentBlock;
        private List<StepNode> _currentSteps;
        private int _currentStepIndex;
        private StepNode? _currentStep;

        // Sequence of sections for root continuation
        private readonly List<KtoryBlock> _sectionOrder = new List<KtoryBlock>();

        public event Action<IReadOnlyList<TagData>>? OnTagsDispatched;

        public KtorySequencer(KtoryFile file)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            File.BindLexicalAutoScopes();
            _currentBlock = file.RootBlock;
            _currentSteps = _currentBlock.Steps;
            _currentStepIndex = 0;

            // Track block ordering
            _sectionOrder.Add(file.RootBlock);
            foreach (var kvp in file.Blocks)
            {
                if (kvp.Value != file.RootBlock)
                {
                    _sectionOrder.Add(kvp.Value);
                }
            }
        }

        public void Start(string? entryLabel = null, string requestedLocale = "zh")
        {
            RequestedLanguage = requestedLocale;
            VisitedItemIds.Clear();
            CallStack.Clear();
            _activeLoops.Clear();
            ActiveAutoPolicy = null;
            CurrentPayload = null;
            CurrentChoice = null;

            if (!string.IsNullOrEmpty(entryLabel))
            {
                if (!File.TryGetBlock(entryLabel, out var block))
                {
                    throw new KtoryException($"Entry section '=== {entryLabel} ===' not found in file.");
                }
                _currentBlock = block;
                _currentSteps = block.Steps;
                _currentStepIndex = 0;
            }
            else
            {
                if (File.RootBlock.Steps.Count == 0)
                {
                    var firstNamed = _sectionOrder.Find(b => !b.IsRoot);
                    if (firstNamed != null)
                    {
                        _currentBlock = firstNamed;
                        _currentSteps = _currentBlock.Steps;
                        _currentStepIndex = 0;
                    }
                    else
                    {
                        _currentBlock = File.RootBlock;
                        _currentSteps = _currentBlock.Steps;
                        _currentStepIndex = 0;
                    }
                }
                else
                {
                    _currentBlock = File.RootBlock;
                    _currentSteps = _currentBlock.Steps;
                    _currentStepIndex = 0;
                }
            }

            Status = ExecutionStatus.Ready;
            Advance();
        }

        public void Step()
        {
            if (Status == ExecutionStatus.Completed)
            {
                return;
            }

            if (Status == ExecutionStatus.AwaitingChoice)
            {
                throw new KtoryControlFlowException("Cannot advance with Step() while awaiting choice selection. SubmitChoice must be used.");
            }

            if (Status != ExecutionStatus.SuspendedAtBeat && Status != ExecutionStatus.Ready)
            {
                return;
            }

            Advance();
        }

        public void SubmitChoice(string choiceIdentifier)
        {
            if (Status != ExecutionStatus.AwaitingChoice || CurrentChoice == null)
            {
                throw new KtoryControlFlowException("Not currently awaiting a choice submission.");
            }

            // Find matching item by Id or Label
            ChoiceOption? matchedOption = null;
            ContainerItem? matchedItem = null;

            if (_currentStep is ContainerStep container)
            {
                for (int i = 0; i < container.Items.Count; i++)
                {
                    var item = container.Items[i];
                    string label = item.GetLabel(RequestedLanguage, DefaultLanguage);
                    if (string.Equals(item.Id, choiceIdentifier, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(label, choiceIdentifier, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals($"[{label}]", choiceIdentifier, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedItem = item;
                        if (i < CurrentChoice.Options.Count)
                        {
                            matchedOption = CurrentChoice.Options[i];
                        }
                        break;
                    }
                }
            }

            if (matchedItem == null)
            {
                throw new KtoryException($"Choice item '{choiceIdentifier}' not found in container.");
            }

            if (matchedOption != null && !matchedOption.CanSelect)
            {
                throw new KtoryControlFlowException($"Choice item '{choiceIdentifier}' is consumed or not selectable.");
            }

            // Mark single-use item as visited in session
            if (matchedItem.IsOneTime)
            {
                VisitedItemIds.Add(matchedItem.Id);
            }

            // Dispatch item tags
            if (matchedItem.Tags.Count > 0)
            {
                OnTagsDispatched?.Invoke(matchedItem.Tags);
            }

            CurrentChoice = null;

            // Handle branch: TargetJump vs InlineSteps
            if (matchedItem.TargetJump != null)
            {
                ExecuteControlFlow(matchedItem.TargetJump, _currentStep as ContainerStep);
                if (Status == ExecutionStatus.Completed)
                {
                    return;
                }
                Advance();
            }
            else if (matchedItem.InlineSteps.Count > 0)
            {
                // Case C: Inline steps
                var sourceContainer = _currentStep as ContainerStep;
                bool isLoop = sourceContainer?.IsLoop == true;

                // Push return frame for after inline steps finish
                var resumeFrame = new CallFrame(_currentBlock, _currentStepIndex, _currentSteps, CallFrameType.InlineBranch, _activeLoops.Count)
                {
                    SourceContainer = sourceContainer,
                    ReturnToContainer = isLoop,
                    SavedAutoPolicy = ActiveAutoPolicy
                };
                CallStack.Push(resumeFrame);

                // Switch to inline steps
                _currentSteps = matchedItem.InlineSteps;
                _currentStepIndex = 0;
                Advance();
            }
            else
            {
                // No jump and no inline steps
                var sourceContainer = _currentStep as ContainerStep;
                if (sourceContainer?.IsLoop == true)
                {
                    // Re-enter loop container
                    _currentStepIndex--; // keep at container
                    Advance();
                }
                else
                {
                    Advance();
                }
            }
        }

        public void Break()
        {
            ApplyBreak();
            Advance();
        }

        public void SetLanguage(string requestedLocale)
        {
            if (string.Equals(RequestedLanguage, requestedLocale, StringComparison.OrdinalIgnoreCase))
                return;

            RequestedLanguage = requestedLocale;

            // If currently suspended at text beat, update text content and speaker without side-effects
            if (Status == ExecutionStatus.SuspendedAtBeat && _currentStep is TextStep textStep && CurrentPayload != null)
            {
                var content = textStep.GetText(RequestedLanguage, DefaultLanguage, out var actualLang);
                CurrentPayload.Speaker = File.ResolveSpeaker(textStep.Speaker, RequestedLanguage, DefaultLanguage);
                CurrentPayload.Content = content;
                CurrentPayload.ActualLanguage = actualLang;
                CurrentPayload.RequestedLanguage = RequestedLanguage;
            }
            else if (Status == ExecutionStatus.AwaitingChoice && _currentStep is ContainerStep containerStep)
            {
                // Re-build choice payload with new language
                CurrentChoice = BuildChoicePayload(containerStep);
            }
        }

        private void Advance()
        {
            if (Status == ExecutionStatus.Completed)
            {
                return;
            }

            while (true)
            {
                // Check if currently looping a non-container step (TextStep or DirectiveStep)
                if (_activeLoops.Count > 0 && _activeLoops.Peek().Loop.TargetNode is not ContainerStep)
                {
                    var currentLoop = _activeLoops.Peek().Loop;
                    currentLoop.IterationCount++;
                    if (currentLoop.CanLoopAgain())
                    {
                        // Re-enter the node: re-emit content and re-execute modifiers
                        _currentStep = currentLoop.TargetNode;
                        ActiveAutoPolicy = _currentStep.LexicalAutoPolicy;
                        ProcessBeatStep(_currentStep);
                        return;
                    }
                    else
                    {
                        // Loop limit reached, break and unwind
                        var (_, _, loopBoundaryDepth) = _activeLoops.Pop();
                        while (CallStack.Count > loopBoundaryDepth)
                        {
                            CallStack.Pop();
                        }
                    }
                }

                // If we've reached the end of the current step list
                if (_currentStepIndex >= _currentSteps.Count)
                {
                    // Case 1: Inline branch ended naturally -> pop inline frame and resume parent steps
                    if (CallStack.Count > 0 && CallStack.Peek().FrameType == CallFrameType.InlineBranch)
                    {
                        var frame = CallStack.Pop();
                        while (_activeLoops.Count > frame.LoopStackDepth)
                        {
                            _activeLoops.Pop();
                        }
                        _currentBlock = frame.Block;
                        _currentSteps = frame.Steps;
                        _currentStepIndex = frame.StepIndex;
                        ActiveAutoPolicy = frame.SavedAutoPolicy;

                        if (frame.ReturnToContainer && frame.SourceContainer != null)
                        {
                            // Loop back to container
                            _currentStepIndex--;
                            if (_currentStepIndex < 0) _currentStepIndex = 0;
                        }
                        continue;
                    }

                    // Case 2: Named section reached natural end -> implicit end per specification:
                    // Clear call stack and active loops, then resume at the first root node following the section.
                    if (!_currentBlock.IsRoot)
                    {
                        ClearActiveLoopsForBlock(_currentBlock);
                        CallStack.Clear();
                        _activeLoops.Clear();
                        ActiveAutoPolicy = null;

                        if (ResumeRootSectionAfter(_currentBlock))
                        {
                            continue;
                        }
                        Status = ExecutionStatus.Completed;
                        return;
                    }

                    // Case 3: Root block ended
                    CallStack.Clear();
                    _activeLoops.Clear();
                    ActiveAutoPolicy = null;
                    Status = ExecutionStatus.Completed;
                    return;
                }

                _currentStep = _currentSteps[_currentStepIndex++];
                ActiveAutoPolicy = _currentStep.LexicalAutoPolicy;

                // Guard evaluation
                if (!string.IsNullOrEmpty(_currentStep.GuardCondition))
                {
                    bool guardPass = Evaluator.EvaluateCondition(_currentStep.GuardCondition!);
                    if (!guardPass)
                    {
                        continue; // Skip this node
                    }
                }

                // Handle Step types
                switch (_currentStep)
                {
                    case TextStep textStep:
                    {
                        if (textStep.IsLoop)
                        {
                            EnsureLoopContext(textStep);
                        }

                        ProcessBeatStep(textStep);
                        return;
                    }

                    case DirectiveStep directiveStep:
                    {
                        // Handle #AUTO macro directive (enters auto scope without observable pause)
                        if (string.Equals(directiveStep.Name, "AUTO", StringComparison.OrdinalIgnoreCase))
                        {
                            if (directiveStep.Tags.Count > 0)
                            {
                                OnTagsDispatched?.Invoke(directiveStep.Tags);
                            }

                            ActiveAutoPolicy = directiveStep.LexicalAutoPolicy;
                            continue;
                        }

                        // Handle #AUTO_END macro directive (leaves auto scope without extra pause)
                        if (string.Equals(directiveStep.Name, "AUTO_END", StringComparison.OrdinalIgnoreCase))
                        {
                            if (directiveStep.Tags.Count > 0)
                            {
                                OnTagsDispatched?.Invoke(directiveStep.Tags);
                            }

                            ActiveAutoPolicy = null;
                            continue;
                        }

                        if (directiveStep.IsLoop)
                        {
                            EnsureLoopContext(directiveStep);
                        }

                        ProcessBeatStep(directiveStep);
                        return;
                    }

                    case ContainerStep containerStep:
                    {
                        // Check loop context
                        if (containerStep.IsLoop)
                        {
                            EnsureLoopContext(containerStep);
                            var currentLoop = _activeLoops.Peek().Loop;
                            if (!currentLoop.CanLoopAgain())
                            {
                                // Loop limit reached, break loop
                                var (_, _, loopBoundaryDepth) = _activeLoops.Pop();
                                while (CallStack.Count > loopBoundaryDepth)
                                {
                                    CallStack.Pop();
                                }
                                continue;
                            }
                        }

                        // Dispatch container tags
                        if (containerStep.Tags.Count > 0)
                        {
                            OnTagsDispatched?.Invoke(containerStep.Tags);
                        }

                        var choicePayload = BuildChoicePayload(containerStep);

                        // If zero selectable options, smoothly skip over container!
                        if (choicePayload.SelectableCount == 0)
                        {
                            if (containerStep.IsLoop && _activeLoops.Count > 0 && _activeLoops.Peek().Loop.TargetNode == containerStep)
                            {
                                var (_, _, loopBoundaryDepth) = _activeLoops.Pop();
                                while (CallStack.Count > loopBoundaryDepth)
                                {
                                    CallStack.Pop();
                                }
                            }
                            continue;
                        }

                        CurrentChoice = choicePayload;
                        Status = ExecutionStatus.AwaitingChoice;
                        return;
                    }

                    case ControlFlowStep cfStep:
                    {
                        ExecuteControlFlow(cfStep, null);
                        if (Status == ExecutionStatus.Completed)
                        {
                            return;
                        }
                        continue;
                    }
                }
            }
        }

        private void ProcessBeatStep(StepNode step)
        {
            if (step is TextStep textStep)
            {
                if (textStep.Tags.Count > 0)
                {
                    OnTagsDispatched?.Invoke(textStep.Tags);
                }

                var content = textStep.GetText(RequestedLanguage, DefaultLanguage, out var actualLang);
                var resolvedSpeaker = File.ResolveSpeaker(textStep.Speaker, RequestedLanguage, DefaultLanguage);
                CurrentPayload = new TextPayload
                {
                    StepType = StepType.Text,
                    LineNumber = textStep.LineNumber,
                    Speaker = resolvedSpeaker,
                    Content = content,
                    ActualLanguage = actualLang,
                    RequestedLanguage = RequestedLanguage,
                    Tags = textStep.Tags,
                    AutoPolicy = ActiveAutoPolicy
                };

                Status = ExecutionStatus.SuspendedAtBeat;
            }
            else if (step is DirectiveStep directiveStep)
            {
                if (directiveStep.Tags.Count > 0)
                {
                    OnTagsDispatched?.Invoke(directiveStep.Tags);
                }

                CurrentPayload = new TextPayload
                {
                    StepType = StepType.Directive,
                    LineNumber = directiveStep.LineNumber,
                    Speaker = null,
                    Content = directiveStep.Name,
                    ActualLanguage = RequestedLanguage,
                    RequestedLanguage = RequestedLanguage,
                    Tags = directiveStep.Tags,
                    AutoPolicy = ActiveAutoPolicy
                };

                Status = ExecutionStatus.SuspendedAtBeat;
            }
        }

        private void EnsureLoopContext(StepNode node)
        {
            if (_activeLoops.Count > 0 && _activeLoops.Peek().Loop.TargetNode == node)
            {
                // Increment iteration
                _activeLoops.Peek().Loop.IterationCount++;
                return;
            }

            // Create new loop context
            var loop = new LoopContext(node);
            var resumeFrame = new CallFrame(_currentBlock, _currentStepIndex, _currentSteps, loopStackDepth: _activeLoops.Count)
            {
                SavedAutoPolicy = ActiveAutoPolicy
            };
            int callStackDepth = CallStack.Count;
            _activeLoops.Push((loop, resumeFrame, callStackDepth));
        }

        private void ApplyBreak()
        {
            if (_activeLoops.Count == 0)
            {
                throw new KtoryControlFlowException("No active loop container to break from.");
            }

            var (_, resumeFrame, loopBoundaryDepth) = _activeLoops.Pop();

            // Unwind call stack up to the loop boundary depth so that inline branch frames
            // created inside the loop are purged without disrupting outer call frames.
            while (CallStack.Count > loopBoundaryDepth)
            {
                CallStack.Pop();
            }

            _currentBlock = resumeFrame.Block;
            _currentSteps = resumeFrame.Steps;
            _currentStepIndex = resumeFrame.StepIndex;
            ActiveAutoPolicy = resumeFrame.SavedAutoPolicy;
            CurrentChoice = null;
            CurrentPayload = null;
        }

        private void ExecuteControlFlow(ControlFlowStep cf, ContainerStep? sourceContainer)
        {
            switch (cf.FlowType)
            {
                case ControlFlowType.Jump:
                {
                    CallStack.Clear();
                    _activeLoops.Clear();
                    ActiveAutoPolicy = null;
                    if (!File.TryGetBlock(cf.TargetLabel!, out var targetBlock))
                    {
                        throw new KtoryException($"Jump target block '=== {cf.TargetLabel} ===' not found.");
                    }
                    _currentBlock = targetBlock;
                    _currentSteps = targetBlock.Steps;
                    _currentStepIndex = 0;
                    break;
                }

                case ControlFlowType.Call:
                {
                    if (!File.TryGetBlock(cf.TargetLabel!, out var targetBlock))
                    {
                        throw new KtoryException($"Call target block '=== {cf.TargetLabel} ===' not found.");
                    }

                    bool returnToContainer = sourceContainer?.IsLoop == true;
                    var returnFrame = new CallFrame(_currentBlock, _currentStepIndex, _currentSteps, CallFrameType.SectionCall, _activeLoops.Count)
                    {
                        SourceContainer = sourceContainer,
                        ReturnToContainer = returnToContainer,
                        SavedAutoPolicy = ActiveAutoPolicy
                    };
                    CallStack.Push(returnFrame);

                    // AUTO policy does not inherit into external subroutines per Specification §2.7
                    ActiveAutoPolicy = null;

                    _currentBlock = targetBlock;
                    _currentSteps = targetBlock.Steps;
                    _currentStepIndex = 0;
                    break;
                }

                case ControlFlowType.Return:
                {
                    // Unwind frames until we find a SectionCall frame (pierces inline branch blocks)
                    CallFrame? callFrame = null;
                    while (CallStack.Count > 0)
                    {
                        var f = CallStack.Pop();
                        if (f.FrameType == CallFrameType.SectionCall)
                        {
                            callFrame = f;
                            break;
                        }
                    }

                    if (callFrame == null)
                    {
                        throw new KtoryControlFlowException("-> return encountered with empty call stack.");
                    }

                    // Clean up any loops created within the returned subroutine,
                    // preserving loops that existed before entering the subroutine.
                    while (_activeLoops.Count > callFrame.LoopStackDepth)
                    {
                        _activeLoops.Pop();
                    }

                    _currentBlock = callFrame.Block;
                    _currentSteps = callFrame.Steps;
                    _currentStepIndex = callFrame.StepIndex;
                    ActiveAutoPolicy = callFrame.SavedAutoPolicy;

                    if (callFrame.ReturnToContainer && callFrame.SourceContainer != null)
                    {
                        _currentStepIndex--;
                        if (_currentStepIndex < 0) _currentStepIndex = 0;
                    }
                    break;
                }

                case ControlFlowType.Break:
                {
                    ApplyBreak();
                    break;
                }

                case ControlFlowType.End:
                {
                    CallStack.Clear();
                    _activeLoops.Clear();
                    ActiveAutoPolicy = null;
                    if (!_currentBlock.IsRoot)
                    {
                        if (ResumeRootSectionAfter(_currentBlock))
                        {
                            return;
                        }
                    }
                    Status = ExecutionStatus.Completed;
                    break;
                }
            }
        }

        private ChoicePayload BuildChoicePayload(ContainerStep container)
        {
            var options = new List<ChoiceOption>();

            foreach (var item in container.Items)
            {
                bool isConsumed = VisitedItemIds.Contains(item.Id);
                bool conditionPass = string.IsNullOrEmpty(item.GuardCondition) || Evaluator.EvaluateCondition(item.GuardCondition);

                bool canSelect = conditionPass && (!item.IsOneTime || !isConsumed);

                options.Add(new ChoiceOption
                {
                    Id = item.Id,
                    Label = item.GetLabel(RequestedLanguage, DefaultLanguage),
                    Marker = item.Marker,
                    IsConsumed = isConsumed,
                    CanSelect = canSelect,
                    Tags = item.Tags
                });
            }

            return new ChoicePayload
            {
                ContainerName = container.Name,
                IsLoop = container.IsLoop,
                Tags = container.Tags,
                Options = options
            };
        }

        private bool ResumeRootSectionAfter(KtoryBlock block)
        {
            _currentBlock = File.RootBlock;
            _currentSteps = File.RootBlock.Steps;

            int maxLine = block.StartLineNumber;
            if (block.EndLineNumber > maxLine)
            {
                maxLine = block.EndLineNumber;
            }
            foreach (var s in block.Steps)
            {
                if (s.LineNumber > maxLine)
                {
                    maxLine = s.LineNumber;
                }
            }

            int resumeIndex = _currentSteps.FindIndex(s => s.LineNumber > maxLine);
            if (resumeIndex >= 0)
            {
                _currentStepIndex = resumeIndex;
                return true;
            }

            return false;
        }

        private void ClearActiveLoopsForBlock(KtoryBlock block)
        {
            while (_activeLoops.Count > 0 && _activeLoops.Peek().ResumeFrame.Block == block)
            {
                _activeLoops.Pop();
            }
        }
    }
}
