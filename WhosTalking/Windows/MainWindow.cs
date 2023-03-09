using System;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class MainWindow: Window, IDisposable {
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin): base(
        "Who's Talking debug",
        ImGuiWindowFlags.AlwaysAutoResize
    ) {
        this.plugin = plugin;
    }

    public void Dispose() {}

    public override void Draw() {
        ImGui.TextUnformatted($"Current channel: {this.plugin.Connection.Channel?.Channel}");

        ImGui.Text($"Party (size {this.plugin.PartyList.Length}):");
        foreach (var partyMember in this.plugin.PartyList) {
            ImGui.TextUnformatted($"{partyMember.Name.TextValue}");
        }

        ImGui.Text("Discord users:");
        foreach (var user in this.plugin.Connection.AllUsers.Values) {
            var muted = user.Muted.GetValueOrDefault();
            var deafened = user.Deafened.GetValueOrDefault();
            var speaking = user.Speaking.GetValueOrDefault();
            var displayName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
            ImGui.TextUnformatted(
                $"{displayName}: {(muted ? "" : "un")}muted, {(deafened ? "" : "un")}deafened{(speaking ? ", speaking" : "")}"
            );
            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip($"{user.Username}#{user.Discriminator}");
            }
        }

        ImGui.Separator();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog)) {
            this.plugin.OpenConfigUi();
        }
    }
}
