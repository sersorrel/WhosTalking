namespace WhosTalking.Discord;

internal class DiscordChannel {
    public DiscordChannel(string? guild, string channel) {
        this.Guild = guild;
        this.Channel = channel;
    }

    // snowflake for the channel. globally unique.
    public string Channel { get; }

    // snowflake for the guild. globally unique. not always present (e.g. DMs).
    public string? Guild { get; }
}
