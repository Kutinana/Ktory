using System.Collections.Generic;
using Ktory.Core.Ast;

namespace Ktory.Core.Runtime
{
    /// <summary>A resolved value with the requested and actual language kept distinct.</summary>
    public sealed class ResolvedLocalizedValue
    {
        public string Content { get; set; } = string.Empty;
        /// <summary>When present, render literal pieces as text and other pieces as rich text. Content is their concatenation.</summary>
        public IReadOnlyList<TextSegment>? Segments { get; set; }
        public string? RequestedLanguage { get; set; }
        public string? ActualLanguage { get; set; }
    }

    /// <summary>
    /// Independent language results for one field. Layout and visibility belong to the renderer.
    /// </summary>
    public sealed class LocalizedTextOutput
    {
        public ResolvedLocalizedValue Primary { get; set; } = new ResolvedLocalizedValue();
        public ResolvedLocalizedValue? Secondary { get; set; }
    }
}
