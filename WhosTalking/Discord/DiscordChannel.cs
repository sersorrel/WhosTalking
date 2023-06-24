namespace WhosTalking.Discord;

internal class DiscordChannel {

    public DiscordChannel(string guild, string channel) {
        this.Guild = guild;
        this.Channel = channel;
    }

    public string Channel { get; }
    public string Guild { get; }
}
