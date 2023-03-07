using System;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class MainWindow: Window, IDisposable {
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin): base(
        "Who's Talking",
        ImGuiWindowFlags.AlwaysAutoResize
    ) {
        this.plugin = plugin;
    }

    public void Dispose() {}

    public override void Draw() {
        ImGui.TextUnformatted($"Current channel: {this.plugin.Connection.Channel?.Channel}");
        ImGui.Text("Users:");
        foreach (var user in this.plugin.Connection.AllUsers.Values) {
            ImGui.TextUnformatted(
                $"{user.DisplayName} ({user.Username}#{user.Discriminator}): {(user.Muted.GetValueOrDefault() ? "" : "un")}muted, {(user.Deafened.GetValueOrDefault() ? "" : "un")}deafened{(user.Speaking.GetValueOrDefault() ? ", speaking" : "")}"
            );
        }

        ImGui.Separator();
        if (ImGui.Button("Show Settings")) {
            this.plugin.OpenConfigUi();
        }
    }
}
