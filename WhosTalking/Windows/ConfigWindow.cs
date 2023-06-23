using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection.PortableExecutable;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class ConfigWindow: Window, IDisposable {
    private readonly List<AssignmentEntry> individualAssignments;
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base(
        "Who's Talking configuration",
        ImGuiWindowFlags.AlwaysAutoResize
    ) {
        this.plugin = plugin;
        individualAssignments = new();
        ResetListToConfig();
    }

    public void Dispose() {
    }

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

        if (this.plugin.PluginInterface.IsDev) {
            ImGui.Separator();

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

        ImGui.Separator();

        if (ImGui.TreeNode("Advanced Individual Assignments")) {
            ImGui.BulletText("Note: the Discord User ID is the unique User ID, it is not the Discord username." + Environment.NewLine +
                "To obtain the User ID you need to go to Discord Settings -> Advanced and enable Developer Mode." + Environment.NewLine +
                "Then right click a user and select \"Copy User ID\".");
            if (ImGui.BeginTable("AssignmentTable", 3)) {
                ImGui.TableSetupColumn("Character Name");
                ImGui.TableSetupColumn("Discord User ID");
                ImGui.TableSetupColumn("Commands");
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
                for (int i = 0; i < individualAssignments.Count; i++) {
                    ImGui.TableNextColumn();
                    var charaName = individualAssignments[i].CharacterName;
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.InputText("###nameEntry" + i, ref charaName, 255)) {
                        individualAssignments[i].CharacterName = charaName;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(200);
                    var discordId = individualAssignments[i].DiscordId;
                    if (ImGui.InputText("###discordIdEntry" + i, ref discordId, 255)) {
                        individualAssignments[i].DiscordId = discordId;
                    }
                    ImGui.TableNextColumn();
                    if (ImGui.Button("Delete###deleteEntry" + i)) {
                        individualAssignments.RemoveAt(i);
                    }
                }
                ImGui.EndTable();
            }
            if (ImGui.Button("Add new Assignment")) {
                individualAssignments.Add(new());
            }

            bool anyChanges = false;
            if (individualAssignments.Count != plugin.Configuration.IndividualAssignments.Count) {
                anyChanges = true;
            }

            if (!anyChanges) {
                for (int i = 0; i < individualAssignments.Count; i++) {
                    if (!string.Equals(individualAssignments[i].CharacterName, plugin.Configuration.IndividualAssignments[i].CharacterName)) {
                        anyChanges = true;
                        break;
                    }
                    if (!string.Equals(individualAssignments[i].DiscordId, plugin.Configuration.IndividualAssignments[i].DiscordId)) {
                        anyChanges = true;
                        break;
                    }
                }
            }

            if (anyChanges) {
                ImGui.TextColored(ImGuiColors.DalamudYellow, "Warning: you have unsaved changes.");
            }

            var configInvalid = individualAssignments.Any(p => string.IsNullOrEmpty(p.DiscordId) || string.IsNullOrEmpty(p.CharacterName));
            configInvalid |= individualAssignments.Count != individualAssignments.Select(p => p.CharacterName).Distinct().Count();
            if (configInvalid || !anyChanges) {
                ImGui.BeginDisabled();
                if (configInvalid) {
                    ImGui.TextColored(ImGuiColors.DalamudYellow, "The configuration is invalid." + Environment.NewLine
                        + "  - All entries require to have a value" + Environment.NewLine
                        + "  - Duplicate character names are not allowed");
                }
            }
            if (ImGui.Button("Reset")) {
                ResetListToConfig();
            }
            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.Text("Resets the configuration to the last saved state");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            if (ImGui.Button("Save")) {
                plugin.Configuration.IndividualAssignments.Clear();
                foreach (var entry in individualAssignments) {
                    plugin.Configuration.IndividualAssignments.Add(new() {
                        CharacterName = entry.CharacterName,
                        DiscordId = entry.DiscordId,
                    });
                }
                plugin.Configuration.Save();
                anyChanges = false;
            }
            if (configInvalid || !anyChanges) ImGui.EndDisabled();

            ImGui.TreePop();
        }
    }

    private void ResetListToConfig() {
        individualAssignments.Clear();
        foreach (var entry in plugin.Configuration.IndividualAssignments) {
            individualAssignments.Add(new() {
                CharacterName = entry.CharacterName,
                DiscordId = entry.DiscordId,
            });
        }
    }
}
