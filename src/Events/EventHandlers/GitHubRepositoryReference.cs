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
using System.Text;

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

                StringBuilder footerBuilder = new();
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

                    footerBuilder.AppendFormat("Pull Request #{0}, ", issue.Number);
                    footerBuilder.AppendFormat("{0}, ", pullRequest.State.Value switch
                    {
                        ItemState.Closed when pullRequest.Merged => "Merged",
                        ItemState.Closed => "Closed",
                        ItemState.Open when pullRequest.Draft => "Draft",
                        ItemState.Open => "Open",
                        _ => issue.State.StringValue
                    });

                    footerBuilder.AppendFormat("{0} comment{1}, ", issue.Comments, issue.Comments == 1 ? "" : "s");
                    footerBuilder.AppendFormat("{0} commit{1}\n", pullRequest.Commits, pullRequest.Commits == 1 ? "" : "s");
                    footerBuilder.AppendFormat("{0} changed file{1}, ", pullRequest.ChangedFiles, pullRequest.ChangedFiles == 1 ? "" : "s");
                    footerBuilder.AppendFormat("{0} addition{1}, ", pullRequest.Additions, pullRequest.Additions == 1 ? "" : "s");
                    footerBuilder.AppendFormat("{0} deletion{1}", pullRequest.Deletions, pullRequest.Deletions == 1 ? "" : "s");
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

                    footerBuilder.AppendFormat("Issue #{0}, ", issue.Number);
                    footerBuilder.AppendFormat("{0}, ", issue.State.Value switch
                    {
                        ItemState.Closed when issue.StateReason?.Value == ItemStateReason.Completed => "Closed as Completed",
                        ItemState.Closed when issue.StateReason?.Value == ItemStateReason.NotPlanned => "Closed as Not Planned",
                        ItemState.Closed => "Closed",
                        ItemState.Open => "Open",
                        _ => issue.State.StringValue
                    });

                    footerBuilder.AppendFormat("{0} comment{1}\n", issue.Comments, issue.Comments == 1 ? "" : "s");
                    footerBuilder.AppendFormat("{0} reaction{1}", issue.Reactions.TotalCount, issue.Reactions.TotalCount == 1 ? "" : "s");

                    if (issue.State.Value == ItemState.Closed)
                    {
                        footerBuilder.AppendFormat(", Closed by @{0}", issue.ClosedBy.Login);
                    }

                    if (issue.Locked)
                    {
                        footerBuilder.AppendFormat(", Lock Reason: {0}", issue.ActiveLockReason!.Value.Value.ToString());
                    }
                }

                embedBuilder.Footer = new() { Text = footerBuilder.ToString() };
                messageBuilder.AddEmbed(embedBuilder);
            }

            if (missingIssues.Count == 1)
            {
                messageBuilder.Content = $"Issue `#{missingIssues[0]}` was not found.";
            }
            else if (missingIssues.Count > 1)
            {
                StringBuilder missingIssuesBuilder = new("Issues ");
                for (int i = 0; i < missingIssues.Count - 1; i++)
                {
                    missingIssuesBuilder.AppendFormat("`#{0}`", missingIssues[i]);
                    missingIssuesBuilder.Append(i + 2 == missingIssues.Count ? " and " : ", ");
                }
                missingIssuesBuilder.AppendFormat("`#{0}` don't seem to exist.", missingIssues[^1]);
                messageBuilder.Content = missingIssuesBuilder.ToString();
            }

            messageBuilder.WithAllowedMentions(Mentions.None);
            await eventArgs.Message.RespondAsync(messageBuilder);
        }

        [GeneratedRegex("##([0-9]{1,10})")]
        private static partial Regex GetGitHubNumberRegex();
    }
}
