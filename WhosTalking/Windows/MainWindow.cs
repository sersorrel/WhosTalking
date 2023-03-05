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
        if (ImGui.Button("Show Settings")) {
            this.plugin.OpenConfigUi();
        }
    }
}
