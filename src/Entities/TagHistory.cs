using System;
using System.Collections.Generic;

namespace DSharpPlus.DSharpPlusHelper.Entities
{
    public sealed class TagHistory
    {
        // TODO: Git diff?
        public string Content { get; init; }
        public IReadOnlyList<string> Aliases { get; init; }
        public ulong Author { get; init; }
        public DateTimeOffset Timestamp { get; init; }

        public TagHistory(string content, IReadOnlyList<string> aliases, ulong author, DateTimeOffset timestamp)
        {
            ArgumentException.ThrowIfNullOrEmpty(content, nameof(content));
            ArgumentNullException.ThrowIfNull(aliases, nameof(aliases));

            Content = content;
            Aliases = aliases;
            Author = author;
            Timestamp = timestamp;
        }
    }
}
