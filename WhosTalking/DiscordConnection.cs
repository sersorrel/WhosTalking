using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Dalamud.Logging;
using Websocket.Client;

namespace WhosTalking;

public class DiscordConnection {
    private const string ClientId = "207646673902501888";
    private readonly Stack<Action> disposeActions = new();
    private readonly Plugin plugin;
    private readonly WebsocketClient webSocket;

    public DiscordConnection(Plugin plugin) {
        this.plugin = plugin;
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
        this.webSocket.Start();
    }

    public string? Username { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Discriminator { get; private set; }

    private string? AccessToken {
        get => this.plugin.Configuration.AccessToken;
        set {
            this.plugin.Configuration.AccessToken = value;
            this.plugin.Configuration.Save();
        }
    }

    public bool IsConnected => this.webSocket.IsRunning;

    public void Dispose() {
        foreach (var action in this.disposeActions) {
            action.Invoke();
        }
    }

    private void OnMessage(ResponseMessage message) {
        PluginLog.Log("got message: {message}", message);
        using var document = JsonDocument.Parse(message.ToString());
        var root = document.RootElement;
        var cmd = root.GetProperty("cmd");
        root.TryGetProperty("evt", out var evt);
        switch (cmd.GetString()) {
            case "DISPATCH":
                switch (evt.GetString()) {
                    case "READY":
                        this.Authenticate();
                        break;
                }

                break;

            case "AUTHENTICATE":
                switch (evt.GetString()) {
                    case "ERROR":
                        this.AccessToken = null;
                        this.Authorize1();
                        break;

                    default:
                        var user = root.GetProperty("data").GetProperty("user");
                        this.Username = user.GetProperty("username").GetString();
                        this.DisplayName = user.GetProperty("display_name").GetString();
                        this.Discriminator = user.GetProperty("discriminator").GetString();
                        break;
                }

                break;

            case "AUTHORIZE":
                this.Authorize2(root.GetProperty("data").GetProperty("code").GetString()!);
                break;
        }
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
        PluginLog.Log("authorize, stage 2; auth code is {authCode}", authCode);
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

    private void Authenticate() {
        PluginLog.Log("authenticate; token is {token}", this.AccessToken ?? "(null)");
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
}
