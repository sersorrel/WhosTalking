using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Dalamud.Logging;
using Websocket.Client;

namespace WhosTalking.Discord;

public class DiscordConnection {
    private const string ClientId = "207646673902501888";
    internal readonly Dictionary<string, User> AllUsers;
    private readonly Stack<Action> disposeActions = new();
    private readonly Plugin plugin;
    private readonly WebsocketClient webSocket;
    private DiscordChannel? currentChannel;
    private string? userId;

    public DiscordConnection(Plugin plugin) {
        this.plugin = plugin;
        this.AllUsers = new Dictionary<string, User>();
        this.webSocket = new WebsocketClient(
            new Uri($"ws://127.0.0.1:6463/?v=1&client_id={ClientId}"),
            () => {
                var client = new ClientWebSocket();
                client.Options.SetRequestHeader("Origin", "https://streamkit.discord.com");
                return client;
            }
        );
        this.disposeActions.Push(() => this.webSocket.Dispose());

        this.webSocket.ReconnectTimeout = TimeSpan.FromMinutes(5);
        this.webSocket.MessageReceived.Subscribe(this.OnMessage);
        this.webSocket.DisconnectionHappened.Subscribe(this.OnError);
        this.webSocket.Start();
    }

    public bool IsConnected => this.webSocket.IsRunning;

    public User? Self {
        get {
            foreach (var pair in this.AllUsers) {
                if (pair.Key == this.userId) {
                    return pair.Value;
                }
            }

            return null;
        }
    }

    internal DiscordChannel? Channel {
        get => this.currentChannel;
        set {
            PluginLog.Log(
                "channel switch {oldG} {oldC} => {newG} {newC}",
                this.currentChannel?.Guild ?? "(null)",
                this.currentChannel?.Channel ?? "(null)",
                value?.Guild ?? "(null)",
                value?.Channel ?? "(null)"
            );
            if (value?.Guild == this.currentChannel?.Guild && value?.Channel == this.currentChannel?.Channel) {
                return;
            }

            if (this.currentChannel != null) {
                this.Unsubscribe("VOICE_STATE_CREATE", new { channel_id = this.currentChannel.Channel });
                this.Unsubscribe("VOICE_STATE_UPDATE", new { channel_id = this.currentChannel.Channel });
                this.Unsubscribe("VOICE_STATE_DELETE", new { channel_id = this.currentChannel.Channel });
                this.Unsubscribe("SPEAKING_START", new { channel_id = this.currentChannel.Channel });
                this.Unsubscribe("SPEAKING_STOP", new { channel_id = this.currentChannel.Channel });
            }

            if (value != null) {
                this.Subscribe("VOICE_STATE_CREATE", new { channel_id = value.Channel });
                this.Subscribe("VOICE_STATE_UPDATE", new { channel_id = value.Channel });
                this.Subscribe("VOICE_STATE_DELETE", new { channel_id = value.Channel });
                this.Subscribe("SPEAKING_START", new { channel_id = value.Channel });
                this.Subscribe("SPEAKING_STOP", new { channel_id = value.Channel });
            }

            this.currentChannel = value;
        }
    }

    private string? AccessToken {
        get => this.plugin.Configuration.AccessToken;
        set {
            this.plugin.Configuration.AccessToken = value;
            this.plugin.Configuration.Save();
        }
    }

    public void Dispose() {
        foreach (var action in this.disposeActions) {
            action.Invoke();
        }
    }

    private void Authenticate() {
        PluginLog.Log("authenticate...");
        this.webSocket.Send(
            JsonSerializer.Serialize(
                new {
                    cmd = "AUTHENTICATE",
                    args = new {
                        access_token = this.AccessToken,
                    },
                    nonce = Guid.NewGuid().ToString(),
                }
            )
        );
    }

    private void Authorize1() {
        PluginLog.Log("authorize, stage 1");
        this.webSocket.Send(
            JsonSerializer.Serialize(
                new {
                    cmd = "AUTHORIZE",
                    args = new {
                        client_id = ClientId,
                        scopes = new[] { "rpc", "messages.read", "rpc.notifications.read" },
                        prompt = "none",
                    },
                    nonce = Guid.NewGuid().ToString(),
                }
            )
        );
    }

    private async void Authorize2(string authCode) {
        PluginLog.Log("authorize, stage 2");
        using var client = new HttpClient();
        var response = await client.PostAsync(
            new Uri("https://streamkit.discord.com/overlay/token"),
            new StringContent(
                JsonSerializer.Serialize(
                    new {
                        code = authCode,
                    }
                ),
                Encoding.UTF8,
                new MediaTypeHeaderValue("application/json")
            )
        );
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.TryGetProperty("access_token", out var accessToken);
        this.AccessToken = accessToken.GetString();
        this.Authenticate();
    }

    private void OnError(DisconnectionInfo info) {
        this.userId = null;
        this.currentChannel = null; // bypass the setter, no point sending an unsubscribe when we're disconnected
        // this is called during dispose, when I guess AllUsers is already gone??
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        this.AllUsers?.Clear();

        if (info.Type == DisconnectionType.NoMessageReceived
            || info.Exception == null
            || (info.Exception?.InnerException is HttpRequestException e
                && e.Message.StartsWith("Connection refused"))) {
            return;
        }

        PluginLog.Error(
            "disconnected; exception {exception}, type {type}, status {status}, description {description}",
            info.Exception != null ? info.Exception : "(null)",
            info.Type,
            info.CloseStatus?.ToString() ?? "(null)",
            info.CloseStatusDescription
        );
    }

    private void OnMessage(ResponseMessage message) {
        using var document = JsonDocument.Parse(message.ToString());
        var root = document.RootElement;
        var cmd = root.GetProperty("cmd");
        root.TryGetProperty("evt", out var evt);
        if (!(cmd.GetString() == "DISPATCH"
            && (evt.GetString() == "SPEAKING_START" || evt.GetString() == "SPEAKING_STOP"))) {
            var redacted = this.AccessToken != null
                ? message.ToString().Replace(this.AccessToken, "[token]")
                : message.ToString();
            PluginLog.Debug("got message: {message}", redacted);
        }

        switch (cmd.GetString()) {
            case "DISPATCH": {
                switch (evt.GetString()) {
                    case "READY": {
                        // connected, ready to do auth
                        var version = root.GetProperty("data").GetProperty("v").GetInt64();
                        if (version != 1) {
                            PluginLog.Warning("unexpected api version {version}", version);
                        }

                        this.Authenticate();
                        break;
                    }
                    case "VOICE_CHANNEL_SELECT": {
                        // the client joins or leaves a voice channel
                        // join: both guild and channel are set
                        // leave: channel is null, guild is not present
                        string? guild, channel;
                        if (root.GetProperty("data").TryGetProperty("guild_id", out var guildElement)
                            && root.GetProperty("data").TryGetProperty("channel_id", out var channelElement)
                            && (guild = guildElement.GetString()) != null
                            && (channel = channelElement.GetString()) != null
                        ) {
                            this.Channel = new DiscordChannel(guild, channel);
                        } else {
                            this.Channel = null;
                            this.AllUsers.Clear();
                        }

                        break;
                    }
                    case "VOICE_STATE_CREATE":
                    case "VOICE_STATE_UPDATE": {
                        var data = root.GetProperty("data");
                        var user = data.GetProperty("user");
                        var voiceState = data.GetProperty("voice_state");
                        var selfMuteElement = voiceState.GetProperty("self_mute");
                        var muteElement = voiceState.GetProperty("mute");
                        var suppressElement = voiceState.GetProperty("suppress");
                        var selfDeafElement = voiceState.GetProperty("self_deaf");
                        var deafElement = voiceState.GetProperty("deaf");
                        var newUser = new User(
                            user.GetProperty("id").GetString()!,
                            user.GetProperty("username").GetString(),
                            user.GetProperty("discriminator").GetString(),
                            data.GetProperty("nick").GetString(),
                            selfMuteElement.ValueKind != JsonValueKind.Null
                                ? selfMuteElement.GetBoolean()
                                : muteElement.ValueKind != JsonValueKind.Null
                                    ? muteElement.GetBoolean()
                                    : suppressElement.ValueKind != JsonValueKind.Null
                                        ? suppressElement.GetBoolean()
                                        : null,
                            selfDeafElement.ValueKind != JsonValueKind.Null
                                ? selfDeafElement.GetBoolean()
                                : deafElement.ValueKind != JsonValueKind.Null
                                    ? deafElement.GetBoolean()
                                    : null
                        );
                        if (this.AllUsers.TryGetValue(newUser.UserId, out var existingUser)) {
                            existingUser.Update(newUser);
                        } else {
                            this.AllUsers.Add(newUser.UserId, newUser);
                        }

                        if (evt.GetString() == "VOICE_STATE_CREATE" && newUser.UserId == this.userId) {
                            // we joined, and need to find out which room we're in
                            this.webSocket.Send(
                                JsonSerializer.Serialize(
                                    new {
                                        cmd = "GET_SELECTED_VOICE_CHANNEL",
                                        nonce = Guid.NewGuid().ToString(),
                                    }
                                )
                            );
                        }

                        break;
                    }
                    case "VOICE_STATE_DELETE": {
                        var userId = root.GetProperty("data").GetProperty("user").GetProperty("id").GetString()!;
                        this.AllUsers.Remove(userId);
                        if (userId == this.userId) {
                            // we left, maybe by getting moved into another room
                            this.AllUsers.Clear();
                            this.webSocket.Send(
                                JsonSerializer.Serialize(
                                    new {
                                        cmd = "GET_SELECTED_VOICE_CHANNEL",
                                        nonce = Guid.NewGuid().ToString(),
                                    }
                                )
                            );
                        }

                        break;
                    }
                    case "SPEAKING_START": {
                        var userId = root.GetProperty("data").GetProperty("user_id").GetString()!;
                        if (this.AllUsers.TryGetValue(userId, out var user)) {
                            user.Speaking = true;
                        } else {
                            PluginLog.Warning("got SPEAKING_START for unknown user {id}", userId);
                        }

                        break;
                    }
                    case "SPEAKING_STOP": {
                        var userId = root.GetProperty("data").GetProperty("user_id").GetString()!;
                        if (this.AllUsers.TryGetValue(userId, out var user)) {
                            user.Speaking = false;
                        } else {
                            PluginLog.Warning("got SPEAKING_STOP for unknown user {id}", userId);
                        }

                        break;
                    }
                }

                break;
            }
            case "AUTHENTICATE": {
                switch (evt.GetString()) {
                    case "ERROR": {
                        this.AccessToken = null;
                        this.AllUsers.Clear();
                        this.Authorize1();
                        break;
                    }
                    default: {
                        var user = root.GetProperty("data").GetProperty("user");
                        this.userId = user.GetProperty("id").GetString();
                        // TODO: store username/discriminator/display_name somewhere
                        // but! they must be kept up-to-date!
                        this.Subscribe("VOICE_CHANNEL_SELECT");
                        this.webSocket.Send(
                            JsonSerializer.Serialize(
                                new {
                                    cmd = "GET_SELECTED_VOICE_CHANNEL",
                                    nonce = Guid.NewGuid().ToString(),
                                }
                            )
                        );
                        break;
                    }
                }

                break;
            }
            case "AUTHORIZE": {
                this.Authorize2(root.GetProperty("data").GetProperty("code").GetString()!);
                break;
            }
            case "GET_SELECTED_VOICE_CHANNEL": {
                var data = root.GetProperty("data");
                if (data.ValueKind == JsonValueKind.Null) {
                    this.Channel = null;
                    this.AllUsers.Clear();
                    break;
                }

                string? guild, channel;
                if (data.TryGetProperty("guild_id", out var guildElement)
                    && data.TryGetProperty("id", out var channelElement)
                    && (guild = guildElement.GetString()) != null
                    && (channel = channelElement.GetString()) != null
                ) {
                    this.Channel = new DiscordChannel(guild, channel);
                } else {
                    this.Channel = null;
                }

                this.AllUsers.Clear();
                foreach (var record in data.GetProperty("voice_states").EnumerateArray()) {
                    var user = record.GetProperty("user");
                    var voiceState = record.GetProperty("voice_state");
                    var selfMuteElement = voiceState.GetProperty("self_mute");
                    var muteElement = voiceState.GetProperty("mute");
                    var suppressElement = voiceState.GetProperty("suppress");
                    var selfDeafElement = voiceState.GetProperty("self_deaf");
                    var deafElement = voiceState.GetProperty("deaf");
                    var newUser = new User(
                        user.GetProperty("id").GetString()!,
                        user.GetProperty("username").GetString(),
                        user.GetProperty("discriminator").GetString(),
                        record.GetProperty("nick").GetString(),
                        selfMuteElement.ValueKind != JsonValueKind.Null
                            ? selfMuteElement.GetBoolean()
                            : muteElement.ValueKind != JsonValueKind.Null
                                ? muteElement.GetBoolean()
                                : suppressElement.ValueKind != JsonValueKind.Null
                                    ? suppressElement.GetBoolean()
                                    : null,
                        selfDeafElement.ValueKind != JsonValueKind.Null
                            ? selfDeafElement.GetBoolean()
                            : deafElement.ValueKind != JsonValueKind.Null
                                ? deafElement.GetBoolean()
                                : null
                    );
                    this.AllUsers.Add(newUser.UserId, newUser);
                }

                break;
            }
        }
    }

    private void Subscribe(string evt) {
        this.Subscribe(evt, new {});
    }

    private void Subscribe(string evt, object args) {
        this.webSocket.Send(
            JsonSerializer.Serialize(
                new {
                    cmd = "SUBSCRIBE",
                    evt,
                    args,
                    nonce = Guid.NewGuid().ToString(),
                }
            )
        );
    }

    private void Unsubscribe(string evt) {
        this.Unsubscribe(evt, new {});
    }

    private void Unsubscribe(string evt, object args) {
        this.webSocket.Send(
            JsonSerializer.Serialize(
                new {
                    cmd = "UNSUBSCRIBE",
                    evt,
                    args,
                    nonce = Guid.NewGuid().ToString(),
                }
            )
        );
    }
}
