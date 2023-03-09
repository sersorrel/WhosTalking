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
            var self = this.plugin.Connection.Self;
            if (self != null) {
                ImGui.TextUnformatted($"Authenticated as {self.Username}#{self.Discriminator}.");
            } else {
                ImGui.Text("Connected, but not authenticated.");
                ImGui.Text("(Maybe you're not in a voice call?)");
            }
        } else {
            ImGui.Text("Not connected to Discord.");
        }
    }
}
