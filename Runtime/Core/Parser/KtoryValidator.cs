using System;
using System.Collections.Generic;
using Ktory.Core.Ast;
using Ktory.Core.Common;

namespace Ktory.Core.Parser
{
    /// <summary>Checks script-local contracts before execution; arbitrary host state remains dynamic.</summary>
    public static class KtoryValidator
    {
        public static void Validate(KtoryFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            file.ClockDeclarations.Clear();
            file.TrackDeclarations.Clear();
            var steps = new List<StepNode>();
            foreach (var block in file.Blocks.Values) Collect(block.Steps, steps);
            foreach (var speaker in file.Speakers)
            {
                ValidatePresentationTags(speaker.DefaultTags, speaker.LineNumber);
                if (ClockOperation.FromTags(speaker.DefaultTags, speaker.LineNumber).Count > 0 ||
                    TrackOperation.FromTags(speaker.DefaultTags, speaker.LineNumber).Count > 0 ||
                    ClockAnchor.FromTags(speaker.DefaultTags, speaker.LineNumber) != null)
                    throw new KtoryException("Clock and track controls cannot be speaker defaults.", speaker.LineNumber, 1);
            }
            foreach (var step in steps)
            {
                ValidatePresentationTags(step.Tags, step.LineNumber);
                var clocks = ClockOperation.FromTags(step.Tags, step.LineNumber);
                var tracks = TrackOperation.FromTags(step.Tags, step.LineNumber);
                var anchor = ClockAnchor.FromTags(step.Tags, step.LineNumber);
                bool ordinary = step is DirectiveStep directive &&
                    !string.Equals(directive.Name, "AUTO", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(directive.Name, "AUTO_END", StringComparison.OrdinalIgnoreCase);
                if ((clocks.Count > 0 || anchor != null) && !(step is TextStep) && !ordinary)
                    throw new KtoryException("Clock controls and .at are only supported on text and ordinary directive beats.", step.LineNumber, 1);
                if (tracks.Count > 0 && !ordinary)
                    throw new KtoryException("Track controls are only supported on ordinary directive beats (#).", step.LineNumber, 1);
                foreach (var clock in clocks)
                    if (clock.Kind == ClockOperationKind.Declare && clock.Name != null)
                        AddDeclaration(file.ClockDeclarations, clock.Name, step, "clock");
                foreach (var track in tracks)
                    if (track.Kind == TrackOperationKind.Create)
                    {
                        if (!file.Blocks.ContainsKey(track.Source!))
                            throw new KtoryException($"Track source section '{track.Source}' was not found. External file URL loading is not supported in this implementation.", step.LineNumber, 1);
                        if (track.Name != null) AddDeclaration(file.TrackDeclarations, track.Name, step, "track");
                    }
                if (step is ControlFlowStep flow) ValidateControlFlow(flow, file);
                if (step is ContainerStep container) ValidateContainerItems(container);
            }
            foreach (var step in steps)
            {
                var anchor = ClockAnchor.FromTags(step.Tags, step.LineNumber);
                if (anchor?.ClockName != null) RequireDeclaration(file.ClockDeclarations, anchor.ClockName, step.LineNumber, "clock");
                foreach (var clock in ClockOperation.FromTags(step.Tags, step.LineNumber))
                    if (clock.Kind != ClockOperationKind.Declare && clock.Name != null)
                        RequireDeclaration(file.ClockDeclarations, clock.Name, step.LineNumber, "clock");
                foreach (var track in TrackOperation.FromTags(step.Tags, step.LineNumber))
                    if (track.Kind == TrackOperationKind.Stop || track.Kind == TrackOperationKind.Await)
                        RequireDeclaration(file.TrackDeclarations, track.Name!, step.LineNumber, "track");
            }
            ValidateInitialRootPath(file.RootBlock.Steps);
        }

        private static void Collect(List<StepNode> source, List<StepNode> result)
        {
            foreach (var step in source)
            {
                result.Add(step);
                if (step is ContainerStep container)
                    foreach (var item in container.Items)
                    {
                        if (item.TargetJump != null) result.Add(item.TargetJump);
                        Collect(item.InlineSteps, result);
                    }
            }
        }

        private static void AddDeclaration(Dictionary<string, StepNode> declarations, string name, StepNode step, string kind)
        {
            if (declarations.TryGetValue(name, out var previous))
                throw new KtoryException($"Named {kind} '{name}' has more than one declaration (first at line {previous.LineNumber}). Re-enter the same declaration to reuse it, or use a distinct name.", step.LineNumber, 1);
            declarations.Add(name, step);
        }

        private static void RequireDeclaration(Dictionary<string, StepNode> declarations, string name, int line, string kind)
        {
            if (!declarations.ContainsKey(name))
                throw new KtoryException($"Named {kind} '{name}' has no declaration in this file.", line, 1);
        }

        // A conservative proof over the initial straight path. Calls/branches may create state,
        // so no assertion about their effects is made here; runtime still checks actual creation.
        private static void ValidateInitialRootPath(List<StepNode> steps)
        {
            var clocks = new HashSet<string>(StringComparer.Ordinal);
            var tracks = new HashSet<string>(StringComparer.Ordinal);
            bool possibleDefault = false;
            bool otherHeadsMayCreateClocks = false;
            bool hasQueuedTracks = false;
            foreach (var step in steps)
            {
                if (step is ControlFlowStep || step is ContainerStep) break;
                var anchor = ClockAnchor.FromTags(step.Tags, step.LineNumber);
                if (anchor != null)
                {
                    if (anchor.ClockName == null && !possibleDefault)
                        throw new KtoryException(".at requires a current default clock; create an anonymous clock or select .defaultClock(name=...) first.", step.LineNumber, 1);
                    if (anchor.ClockName != null && !otherHeadsMayCreateClocks && !clocks.Contains(anchor.ClockName))
                        throw NotCreated("Clock", anchor.ClockName, step.LineNumber);
                    // The entry gate runs before the tags. If it waits, queued heads may
                    // initialize state before the remaining operations of this same anchor.
                    otherHeadsMayCreateClocks |= hasQueuedTracks;
                }
                foreach (var tag in step.Tags)
                {
                    var clock = ClockOperation.FromTag(tag, step.LineNumber);
                    if (clock != null)
                    {
                        if (clock.Kind == ClockOperationKind.Declare)
                        {
                            if (clock.Name == null) possibleDefault = true;
                            else clocks.Add(clock.Name);
                        }
                        else
                        {
                            if (clock.Name == null && !possibleDefault)
                                throw new KtoryException(".resetClock requires a current default clock.", step.LineNumber, 1);
                            if (clock.Name != null && !otherHeadsMayCreateClocks && !clocks.Contains(clock.Name))
                                throw NotCreated("Clock", clock.Name, step.LineNumber);
                            if (clock.Kind == ClockOperationKind.SelectDefault) possibleDefault = true;
                        }
                    }
                    var track = TrackOperation.FromTag(tag, step.LineNumber);
                    if (track?.Kind == TrackOperationKind.Create)
                    {
                        if (track.Name != null) tracks.Add(track.Name);
                        hasQueuedTracks = true;
                    }
                    else if (track?.Kind == TrackOperationKind.Await)
                    {
                        if (!tracks.Contains(track.Name!)) throw NotCreated("Track", track.Name!, step.LineNumber);
                        otherHeadsMayCreateClocks |= hasQueuedTracks;
                    }
                    else if (track?.Kind == TrackOperationKind.AwaitCreated)
                        otherHeadsMayCreateClocks |= hasQueuedTracks;
                }
                // Creation queues a head; only a potential scheduling boundary lets it run.
                // Do not claim it has initialized while Main traverses transparent controls.
                if (hasQueuedTracks && CanYieldToAuxiliary(step)) otherHeadsMayCreateClocks = true;
            }
        }

        private static bool CanYieldToAuxiliary(StepNode step)
        {
            if (ClockAnchor.FromTags(step.Tags, step.LineNumber) != null) return true;
            if (!(step is DirectiveStep directive)) return true;
            if (directive.Name.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ||
                directive.Name.Equals("AUTO_END", StringComparison.OrdinalIgnoreCase)) return false;
            if (directive.Name.Length > 0) return true;
            bool hasControl = false;
            foreach (var tag in step.Tags)
            {
                if (ClockOperation.FromTag(tag, step.LineNumber) != null || TrackOperation.IsTrackOperationTag(tag)) hasControl = true;
                else if (!tag.Name.Equals("loop", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return !hasControl;
        }

        private static KtoryException NotCreated(string kind, string name, int line) =>
            new KtoryException($"{kind} '{name}' is referenced before its first creation on this path. Track creation only queues work; use .track(...).await or .awaitTrack(name=...) before depending on its initialization.", line, 1);

        private static void ValidatePresentationTags(IReadOnlyList<TagData> tags, int line)
        {
            foreach (var tag in tags)
            {
                switch (tag.Name.ToLowerInvariant())
                {
                    case "wait": case "next": case "duration":
                        BuiltinArguments.Check(tag, line, "seconds");
                        BuiltinArguments.Number(tag, "seconds", line, required: tag.Name.Equals("duration", StringComparison.OrdinalIgnoreCase), nonnegative: true);
                        break;
                    case "skippable":
                        BuiltinArguments.Check(tag, line, "enabled", "seconds");
                        BuiltinArguments.Number(tag, "seconds", line, nonnegative: true);
                        if (tag.NamedArgs.TryGetValue("enabled", out var enabled) && !(enabled is bool))
                            throw BuiltinArguments.Error(tag, line, "requires 'enabled' to be a boolean");
                        break;
                    case "loop":
                        BuiltinArguments.Check(tag, line, "count");
                        var count = BuiltinArguments.Number(tag, "count", line);
                        if (count.HasValue && (count.Value < int.MinValue || count.Value > int.MaxValue || count.Value != Math.Truncate(count.Value)))
                            throw BuiltinArguments.Error(tag, line, "requires 'count' to be an integer in the supported range");
                        break;
                }
            }
        }

        private static void ValidateControlFlow(ControlFlowStep flow, KtoryFile file)
        {
            if (flow.FlowType != ControlFlowType.Jump && flow.FlowType != ControlFlowType.Call) return;
            var target = flow.TargetLabel ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
                throw new KtoryException($"Control flow '{flow.FlowType.ToString().ToLowerInvariant()}' has no target specified.", flow.LineNumber, 1);
            if (flow.FlowType == ControlFlowType.Jump && (target.Equals("return", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("break", StringComparison.OrdinalIgnoreCase) || target.Equals("end", StringComparison.OrdinalIgnoreCase))) return;
            if (!file.Blocks.ContainsKey(target))
                throw new KtoryException($"Target section '=== {target} ===' not found in file (referenced by {flow.FlowType.ToString().ToLowerInvariant()} at line {flow.LineNumber}).", flow.LineNumber, 1);
        }

        private static void ValidateContainerItems(ContainerStep container)
        {
            foreach (var item in container.Items)
            {
                ValidatePresentationTags(item.Tags, item.LineNumber);
                if (ClockOperation.FromTags(item.Tags, item.LineNumber).Count > 0 ||
                    TrackOperation.FromTags(item.Tags, item.LineNumber).Count > 0 ||
                    ClockAnchor.FromTags(item.Tags, item.LineNumber) != null)
                    throw new KtoryException("Clock and track controls are not supported on choice items.", item.LineNumber, 1);
                if (item.TargetJump != null && item.InlineSteps.Count > 0)
                    throw new KtoryException($"Option line jump shorthand '{item.TargetJump}' cannot be combined with an indented branch body at line {item.LineNumber}.", item.LineNumber, 1);
            }
        }
    }
}
