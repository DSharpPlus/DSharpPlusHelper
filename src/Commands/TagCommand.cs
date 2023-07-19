using System.ComponentModel;
using System.Threading.Tasks;
using DSharpPlus.CommandAll.Attributes;
using DSharpPlus.CommandAll.Commands;
using DSharpPlus.DSharpPlusHelper.Database;
using DSharpPlus.DSharpPlusHelper.Entities;
using DSharpPlus.Entities;

namespace DSharpPlus.DSharpPlusHelper.Commands
{
    [Command("tag"), Description("Use or manage a predefined message.")]
    public sealed class TagCommand : BaseCommand
    {
        [Command("send"), Description("Sends a predefined wall of text.")]
        public static Task GetAsync(CommandContext context, [RemainingText, Description("The name of the tag.")] string name)
            => DatabaseTracker.GetTagContent(name) is string content
                ? context.ReplyAsync(content)
                : context.ReplyAsync($"Tag {Formatter.InlineCode(Formatter.Sanitize(name))} not found.");

        [Command("create"), Description("Creates a message to send later.")]
        public static Task CreateAsync(CommandContext context, [Description("The name of the tag.")] string name, [RemainingText, Description("The content that the tag should contain.")] string content)
        {
            if (DatabaseTracker.GetTag(name) is not null)
            {
                return context.ReplyAsync($"Tag {name} already exists.");
            }
            else if (DatabaseTracker.CreateTag(name, content))
            {
                return context.ReplyAsync($"Tag {name} created.");
            }

            return context.ReplyAsync($"Tag {name} could not be created.");
        }

        [Command("info"), Description("Retrieves information about a tag.")]
        public static Task InfoAsync(CommandContext context, [Description("The name of the tag.")] string name)
        {
            if (DatabaseTracker.GetTag(name) is not TagEntity tag)
            {
                return context.ReplyAsync($"Tag {name} not found.");
            }

            DiscordEmbedBuilder embedBuilder = new();
            embedBuilder.WithTitle($"Tag {name}");
            embedBuilder.WithDescription(tag.Content);
            embedBuilder.AddField("Aliases", $"`{string.Join("`, `", tag.Aliases)}`");
            embedBuilder.AddField("Created By", $"<@{tag.History[0].Author}> on {Formatter.Timestamp(tag.History[0].Timestamp)}");
            embedBuilder.AddField("Last Updated By", $"<@{tag.History[^1].Author}> on {Formatter.Timestamp(tag.History[^1].Timestamp)}");
            embedBuilder.AddField("Total Edits", (tag.History.Count - 1).ToString("N0"));
            return context.ReplyAsync(embedBuilder);
        }
    }
}
