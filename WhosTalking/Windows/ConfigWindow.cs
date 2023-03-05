using System;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class ConfigWindow: Window, IDisposable {
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin): base(
        "Who's Talking configuration",
        ImGuiWindowFlags.AlwaysAutoResize
    ) {
        this.configuration = plugin.Configuration;
    }

    public void Dispose() {}

    public override void Draw() {
        // can't ref a property, so use a local copy
        var configValue = this.configuration.SomePropertyToBeSavedAndWithADefault;
        if (ImGui.Checkbox("Random Config Bool", ref configValue)) {
            this.configuration.SomePropertyToBeSavedAndWithADefault = configValue;
            // can save immediately on change, if you don't want to provide a "Save and Close" button
            this.configuration.Save();
        }
    }
}
