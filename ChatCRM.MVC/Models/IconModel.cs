namespace ChatCRM.MVC.Models
{
    /// <summary>
    /// Lightweight model passed to the _Icon partial.
    /// Keep it small — adding lots of properties here defeats the "stable consistent icon" goal.
    /// </summary>
    public sealed class IconModel
    {
        public IconModel(string name, int size = 0, string? cssClass = null, string? ariaLabel = null)
        {
            Name = name;
            Size = size;
            CssClass = cssClass;
            AriaLabel = ariaLabel;
        }

        public string Name { get; }
        public int Size { get; }
        public string? CssClass { get; }
        public string? AriaLabel { get; }
    }
}
