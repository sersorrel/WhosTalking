using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Gui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using JetBrains.Annotations;
using WhosTalking.Windows;

namespace WhosTalking;

[PublicAPI]
public sealed class Plugin: IDalamudPlugin {
    private static Vector2 IndicatorOffset = new(-15, 0);
    internal DiscordConnection Connection;
    private Stack<Action> disposeActions = new();
    public WindowSystem WindowSystem = new("WhosTalking");

    public Plugin(
        [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
        [RequiredVersion("1.0")] GameGui gameGui,
        [RequiredVersion("1.0")] PartyList partyList
    ) {
        this.PluginInterface = pluginInterface;
        this.GameGui = gameGui;
        this.PartyList = partyList;

        this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(this.PluginInterface);

        this.ConfigWindow = new ConfigWindow(this);
        this.disposeActions.Push(() => this.ConfigWindow.Dispose());
        this.MainWindow = new MainWindow(this);
        this.disposeActions.Push(() => this.MainWindow.Dispose());

        this.WindowSystem.AddWindow(this.ConfigWindow);
        this.WindowSystem.AddWindow(this.MainWindow);
        this.disposeActions.Push(() => this.WindowSystem.RemoveAllWindows());

        this.PluginInterface.UiBuilder.Draw += this.Draw;
        this.disposeActions.Push(() => this.PluginInterface.UiBuilder.Draw -= this.Draw);
        this.PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        this.disposeActions.Push(() => this.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi);

        this.Connection = new DiscordConnection(this);
        this.disposeActions.Push(() => this.Connection.Dispose());

        this.MainWindow.IsOpen = true;
    }

    internal DalamudPluginInterface PluginInterface { get; init; }
    internal GameGui GameGui { get; init; }
    internal PartyList PartyList { get; init; }
    public Configuration Configuration { get; init; }

    internal ConfigWindow ConfigWindow { get; init; }
    internal MainWindow MainWindow { get; init; }
    public string Name => "Who's Talking";

    public void Dispose() {
        foreach (var action in this.disposeActions) {
            action.Invoke();
        }
    }

    private User? XivToDiscord(string name, string? world) {
        return null;
    }

    private void Draw() {
        this.WindowSystem.Draw();
        this.DrawOverlay();
    }

    private unsafe void DrawOverlay() {
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos);
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
        if (ImGui.Begin(
                "##WhosTalkingOverlay",
                ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoMouseInputs
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoNav
            )) {
            try {
                ImGui.PushClipRect(
                    ImGui.GetMainViewport().Pos,
                    ImGui.GetMainViewport().Pos + ImGui.GetMainViewport().Size,
                    false
                );
                var drawList = ImGui.GetWindowDrawList();
                var partyAddon = (AtkUnitBase*)this.GameGui.GetAddonByName("_PartyList");
                var shouldDrawParty = partyAddon != (AtkUnitBase*)nint.Zero && partyAddon->IsVisible;
                if (shouldDrawParty) {
                    if (this.PartyList.Length == 0) {
                        var user = this.Connection.Self;
                        DrawIndicator(drawList, partyAddon, 0, user);
                    } else {
                        for (var i = 0; i < this.PartyList.Length; i++) {
                            var partyMember = this.PartyList[i]!;
                            // TODO: work out what to do with partyMember.World
                            var user = this.XivToDiscord(partyMember.Name.TextValue, null);
                            DrawIndicator(drawList, partyAddon, i, user);
                        }
                    }
                }
            } finally {
                ImGui.PopClipRect();
                ImGui.End();
            }
        }
    }

    private static unsafe void DrawIndicator(ImDrawListPtr drawList, AtkUnitBase* partyAddon, int idx, User? user) {
        var nodePtr = (AtkComponentNode*)partyAddon->UldManager.NodeList[22 - idx];
        var colNode = nodePtr->Component->UldManager.NodeList[17]; // the image node for the job icon
        var indicatorStart = GetNodePosition(colNode) + IndicatorOffset;
        var indicatorSize = new Vector2(5, colNode->Height) * partyAddon->Scale;
        var indicatorMin = indicatorStart + ImGui.GetMainViewport().Pos;
        var indicatorMax = indicatorStart + indicatorSize + ImGui.GetMainViewport().Pos;
        drawList.AddRectFilled(indicatorMin, indicatorMax, GetColour(user));
    }

    private static unsafe Vector2 GetNodePosition(AtkResNode* node) {
        var pos = new Vector2(node->X, node->Y);
        var par = node->ParentNode;
        while (par != null) {
            pos *= new Vector2(par->ScaleX, par->ScaleY);
            pos += new Vector2(par->X, par->Y);
            par = par->ParentNode;
        }

        return pos;
    }

    private static uint GetColour(User? user) {
        // colours are ABGR
        if (user == null) {
            return 0xFF00FFFF; // yellow
        }

        if (user.Speaking.GetValueOrDefault()) {
            return 0xFF00FF00; // green
        }

        if (user.Deafened.GetValueOrDefault()) {
            return 0xFF0000FF; // red
        }

        if (user.Muted.GetValueOrDefault()) {
            return 0xFF808000; // blue
        }

        return 0;
    }

    public void OpenConfigUi() {
        this.ConfigWindow.IsOpen = true;
    }
}
