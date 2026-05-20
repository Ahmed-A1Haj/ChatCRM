namespace ChatCRM.Application.Ai.DTOs
{
    /// <summary>File reference for the agent-knowledge upload pipeline.</summary>
    public sealed class AiFileRef
    {
        public AiFileRef(string key, string name)
        {
            Key = key;
            Name = name;
        }

        /// <summary>Redis key holding the raw bytes (set via SET file:{uuid} bytes EX 3600).</summary>
        public string Key { get; init; }
        public string Name { get; init; }
    }

    /// <summary>Link reference for the agent-knowledge URL pipeline.</summary>
    public sealed class AiLinkRef
    {
        public AiLinkRef(string url, string name, string refresh)
        {
            Url = url;
            Name = name;
            Refresh = refresh;
        }

        public string Url { get; init; }
        public string Name { get; init; }
        /// <summary><c>never</c> | <c>daily</c> | <c>weekly</c> | <c>monthly</c>.</summary>
        public string Refresh { get; init; }
    }
}
