namespace Ktory.Core.Ast
{
    /// <summary>A piece of display text. Literal content must never be interpreted as markup.</summary>
    public sealed class TextSegment
    {
        public string Content { get; set; } = string.Empty;
        public bool IsLiteral { get; set; }
    }
}
