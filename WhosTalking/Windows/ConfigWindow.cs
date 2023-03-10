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
        ImGui.Text("Thanks for trying Who's Talking!");

        ImGui.Separator();
        ImGui.Text("Status:");
        ImGui.SameLine();
        if (this.plugin.Connection.IsConnected) {
            var self = this.plugin.Connection.Self;
            if (self != null) {
                ImGui.TextUnformatted($"Authenticated as {self.Username}#{self.Discriminator}.");
            } else {
                ImGui.Text("Connected, but not authenticated. (Maybe you're not in a voice call?)");
            }
        } else {
            ImGui.Text("Not connected to Discord. (Open the Discord app to get started!)");
        }

        ImGui.Separator();
        ImGui.Text("Potential issues:");
        ImGui.BulletText(
            "“My game is slow!”"
            + "\n Please contact me (“send feedback” in the plugin installer, ping me on Discord,"
            + "\n or open a GitHub issue) with details of how many people are in your Discord call"
            + "\n and how many people are in your party/alliance."
        );
        ImGui.BulletText(
            "“I get yellow bars instead of voice activity indicators...”"
            + "\n Get the relevant people to put their character names (first or last name is"
            + "\n good enough) in their Discord nicknames. Eventually I'll build a UI to let you"
            + "\n fix this yourself and/or turn off the yellow bars."
        );
        ImGui.BulletText(
            "“If I open Discord after the game starts, it takes a minute to connect.”"
            + "\n Correct! I'll try to make this faster in a future update."
        );
        ImGui.BulletText(
            "“Something else broke”/“I want a new feature”/..."
            + "\n Please do let me know, but no promises I'll be able to fix/add/... it."
        );

        ImGui.Separator();
        if (this.plugin.PluginInterface.IsDev) {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("If you closed the debug window, you can reopen it:");
            ImGui.SameLine();
            if (ImGui.Button("click here")) {
                this.plugin.MainWindow.IsOpen = true;
            }
        }

        if (this.plugin.PluginInterface.IsDev || this.plugin.PluginInterface.IsTesting) {
            ImGui.Text("Hello, tester! Please ping me (Ash#6256) with any issues you find :)");
        }

        if (this.plugin.PluginInterface.IsDev || !this.plugin.PluginInterface.IsTesting) {
            ImGui.Text("If you report issues on Discord, ping me (Ash#6256) or I will not see your message!");
        }
    }
}
