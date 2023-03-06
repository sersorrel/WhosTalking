using System;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class ConfigWindow: Window, IDisposable {
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin): base(
        "Who's Talking configuration",
        ImGuiWindowFlags.AlwaysAutoResize
    ) {
        this.plugin = plugin;
    }

    public void Dispose() {}

    public override void Draw() {
        if (this.plugin.Connection.IsConnected) {
            ImGui.TextUnformatted(
                $"Authenticated as {this.plugin.Connection.Username}#{this.plugin.Connection.Discriminator}."
            );
        } else {
            ImGui.Text("Discord not connected.");
        }
    }
}
