using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using DSharpPlus.EventArgs;
using DSharpPlus.Entities;
using System;
using Octokit;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.Net;
using Microsoft.Extensions.Logging;

namespace DSharpPlus.DSharpPlusHelper.Events.EventHandlers
{
    public sealed partial class GitHubRepositoryReference
    {
        private static readonly DiscordColor OpenColor = new(0x238636);
        private static readonly DiscordColor ClosedColor = new(0xda3633);
        private static readonly DiscordColor MergedColor = new(0x8957e5);
        private static readonly DiscordColor DraftColor = new(0x6e7681);

        private readonly ILogger<GitHubRepositoryReference> Logger;
        private readonly GitHubClient GitHubClient;
        private readonly string RepositoryOwner;
        private readonly string RepositoryName;

        public GitHubRepositoryReference(ILogger<GitHubRepositoryReference> logger, GitHubClient client, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));
            ArgumentNullException.ThrowIfNull(client, nameof(client));
            ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
            Logger = logger;
            GitHubClient = client;
            RepositoryOwner = configuration.GetValue<string>("github:repository_owner") ?? throw new ArgumentNullException(nameof(configuration), "Repository owner is not set.");
            RepositoryName = configuration.GetValue<string>("github:repository_name") ?? throw new ArgumentNullException(nameof(configuration), "Repository name is not set.");
        }

        [DiscordEvent(DiscordIntents.GuildMessages | DiscordIntents.MessageContents)]
        public async Task ExecuteAsync(DiscordClient client, MessageCreateEventArgs eventArgs)
        {
            List<Match> matches = GetGitHubNumberRegex().Matches(eventArgs.Message.Content).ToList();
            if (matches.Count == 0)
            {
                return;
            }

            List<int> missingIssues = new();
            DiscordMessageBuilder messageBuilder = new();
            for (int i = 0; i < Math.Min(matches.Count, 5); i++)
            {
                int issueNumber = int.Parse(matches[i].Groups[1].Value);
                Issue? issue = null;
                PullRequest? pullRequest = null;

                try
                {
                    issue = await GitHubClient.Issue.Get(RepositoryOwner, RepositoryName, issueNumber);
                    pullRequest = await GitHubClient.PullRequest.Get(RepositoryOwner, RepositoryName, issueNumber);
                }
                catch (ApiException error)
                {
                    if (error.StatusCode == HttpStatusCode.Forbidden)
                    {
                        if (error.HttpResponse.Headers.TryGetValue("X-RateLimit-Reset", out string? resetTime))
                        {
                            DateTimeOffset resetTimeOffset = DateTimeOffset.FromUnixTimeSeconds(long.Parse(resetTime));
                            Logger.LogWarning(error, "Rate limit reached. Reset in {TimeUntilReset}", resetTimeOffset - DateTimeOffset.UtcNow);
                            await eventArgs.Message.RespondAsync($"Rate limit reached. Reset {Formatter.Timestamp(DateTimeOffset.FromUnixTimeSeconds(long.Parse(resetTime)))}.");
                        }
                        else
                        {
                            Logger.LogWarning(error, "Rate limit reached. No reset time provided.");
                            await eventArgs.Message.RespondAsync("Rate limit reached. No reset time provided.");
                        }
                        return;
                    }
                    else if (issue is null)
                    {
                        if (error.StatusCode == HttpStatusCode.NotFound)
                        {
                            missingIssues.Add(issueNumber);
                            continue;
                        }

                        Logger.LogError(error, "Error while fetching issue #{IssueNumber}", issueNumber);
                        continue;
                    }

                    // if issue wasn't null, then it was the PR that 404'd, which is to be expected if the number isn't a PR.
                }

                string shortenedDescription = issue.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault("*No description provided.*").Trim();
                if (shortenedDescription.Length > 200)
                {
                    shortenedDescription = shortenedDescription[..200];
                }

                DiscordEmbedBuilder embedBuilder = new()
                {
                    Author = new()
                    {
                        Name = issue.User.Login,
                        IconUrl = issue.User.AvatarUrl,
                        Url = issue.User.AvatarUrl,
                    },
                    Description = shortenedDescription,
                    Timestamp = issue.CreatedAt,
                    Title = issue.Title,
                    Url = issue.HtmlUrl
                };

                if (pullRequest is not null)
                {
                    embedBuilder.Color = pullRequest.State.Value switch
                    {
                        ItemState.Closed when pullRequest.Merged => MergedColor,
                        ItemState.Closed => ClosedColor,
                        ItemState.Open when pullRequest.Draft => DraftColor,
                        ItemState.Open => OpenColor,
                        _ => DiscordColor.Yellow,
                    };

                    embedBuilder.Footer = new()
                    {
                        Text = $"Pull Request #{issue.Number}, {issue.State.Value switch
                        {
                            ItemState.Closed when pullRequest.Merged => "Merged",
                            ItemState.Closed => "Closed",
                            ItemState.Open when pullRequest.Draft => "Draft",
                            ItemState.Open => "Open",
                            _ => issue.State.StringValue,
                        }}, {issue.Comments} comment{(issue.Comments == 1 ? "" : "s")}, {pullRequest.Commits:N0} commit{(pullRequest.Commits == 1 ? "" : "s")}",
                    };
                }
                else
                {
                    embedBuilder.Color = issue.State.Value switch
                    {
                        ItemState.Closed when issue.StateReason?.Value == ItemStateReason.Completed => MergedColor,
                        ItemState.Closed => DraftColor,
                        ItemState.Open => OpenColor,
                        _ => DiscordColor.Yellow,
                    };

                    embedBuilder.Footer = new()
                    {
                        Text = $"Issue #{issue.Number}, {issue.State.Value switch
                        {
                            ItemState.Closed => "Closed",
                            ItemState.Open => "Open",
                            _ => issue.State.StringValue,
                        }}, {issue.Comments} comment{(issue.Comments == 1 ? "" : "s")}",
                    };
                }

                messageBuilder.AddEmbed(embedBuilder);
            }

            if (missingIssues.Count > 0)
            {
                messageBuilder.Content = missingIssues.Count switch
                {
                    1 => $"Issue `#{missingIssues[0]}` was not found.",
                    2 => $"Issues `#{missingIssues[0]}` and `#{missingIssues[1]}` were not found.",
                    3 => $"Issues `#{missingIssues[0]}`, `#{missingIssues[1]}`, and `#{missingIssues[2]}` were not found.",
                    _ => $"The following issues weren't found: `#{string.Join("`, `#", missingIssues)}`."
                };
            }

            messageBuilder.WithAllowedMentions(Mentions.None);
            await eventArgs.Message.RespondAsync(messageBuilder);
        }

        [GeneratedRegex("##([0-9]{1,10})")]
        private static partial Regex GetGitHubNumberRegex();
    }
}
