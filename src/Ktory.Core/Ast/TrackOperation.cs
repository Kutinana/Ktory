using System;
using System.Collections.Generic;

namespace Ktory.Core.Ast
{
    public enum TrackOperationKind { Create, Stop, Await, AwaitCreated }

    /// <summary>Names identify session declarations, never display regions.</summary>
    public sealed class TrackOperation
    {
        public TrackOperationKind Kind { get; }
        public string? Source { get; }
        public string? Name { get; }
        private TrackOperation(TrackOperationKind kind, string? source, string? name)
        { Kind = kind; Source = source; Name = name; }

        public static bool IsTrackOperationTag(TagData tag) =>
            string.Equals(tag.Name, "track", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag.Name, "stopTrack", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag.Name, "awaitTrack", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag.Name, "await", StringComparison.OrdinalIgnoreCase);

        public static TrackOperation? FromTag(TagData tag, int line)
        {
            bool create = string.Equals(tag.Name, "track", StringComparison.OrdinalIgnoreCase);
            bool stop = string.Equals(tag.Name, "stopTrack", StringComparison.OrdinalIgnoreCase);
            bool wait = string.Equals(tag.Name, "awaitTrack", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(tag.Name, "await", StringComparison.OrdinalIgnoreCase))
            {
                BuiltinArguments.Check(tag, line);
                return new TrackOperation(TrackOperationKind.AwaitCreated, null, null);
            }
            if (!create && !stop && !wait) return null;
            if (create) BuiltinArguments.Check(tag, line, "source", "name");
            else BuiltinArguments.Check(tag, line, "name");
            return new TrackOperation(create ? TrackOperationKind.Create : stop ? TrackOperationKind.Stop : TrackOperationKind.Await,
                create ? BuiltinArguments.String(tag, "source", line, required: true) : null,
                BuiltinArguments.String(tag, "name", line, required: !create));
        }

        public static IReadOnlyList<TrackOperation> FromTags(IReadOnlyList<TagData> tags, int line)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            var result = new List<TrackOperation>();
            for (int index = 0; index < tags.Count; index++)
            {
                var tag = tags[index];
                var operation = FromTag(tag, line);
                if (operation?.Kind == TrackOperationKind.AwaitCreated &&
                    (index == 0 || !string.Equals(tags[index - 1].Name, "track", StringComparison.OrdinalIgnoreCase)))
                    throw BuiltinArguments.Error(tag, line, "must immediately follow .track(...) on the same directive");
                if (operation != null) result.Add(operation);
            }
            return result.AsReadOnly();
        }
    }
}
