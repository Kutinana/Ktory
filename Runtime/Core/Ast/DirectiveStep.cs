namespace Ktory.Core.Ast
{
    /// <summary>
    /// Represents an independent action beat / system directive without text dialogue (starts with #).
    /// Occupies one discrete step, triggers attached tags, and suspends at beat.
    /// </summary>
    public class DirectiveStep : StepNode
    {
        public string Name { get; set; } = string.Empty;

        public override string ToString()
        {
            var n = string.IsNullOrEmpty(Name) ? "#" : $"#{Name}";
            return $"{n} [{Tags.Count} tags]";
        }
    }
}
