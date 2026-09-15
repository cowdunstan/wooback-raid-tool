namespace WoobackVash.Api.Config;

/// <summary>
/// Recruitment config for the public application form. The webhook URL is a secret — anyone
/// holding it can post to the channel — so it lives in user-secrets locally and a Fly secret
/// (<c>Recruitment__DiscordWebhookUrl</c>) in production. Left empty, applications are still
/// stored; only the Discord ping is skipped.
/// </summary>
public class RecruitmentOptions
{
    public const string SectionName = "Recruitment";

    /// <summary>Discord channel webhook that is pinged for each new application.</summary>
    public string DiscordWebhookUrl { get; set; } = "";
}
