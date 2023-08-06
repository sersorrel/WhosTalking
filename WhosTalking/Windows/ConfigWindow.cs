using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class ConfigWindow: Window, IDisposable {
    private readonly List<AssignmentEntry> individualAssignments;
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin): base(
        "Who's Talking configuration",
        ImGuiWindowFlags.AlwaysAutoResize
    ) {
        this.plugin = plugin;
        this.individualAssignments = new List<AssignmentEntry>();
        this.ResetListToConfig();
    }

    public void Dispose() {}

    public override void Draw() {
        ImGui.Text($"Thanks for {(this.plugin.PluginInterface.IsTesting ? "testing" : "using")} Who's Talking!");

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
        var nonXivUsersDisplayMode = (int)this.plugin.Configuration.NonXivUsersDisplayMode;
        var nonXivUsersDisplayModes = Enum.GetValues(typeof(NonXivUsersDisplayMode))
            .Cast<NonXivUsersDisplayMode>()
            .Select(val => val.ToString())
            .ToArray();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("List speaking Discord users not in your party:");
        ImGui.SameLine();
        if (ImGui.Combo(
                "###nonXivUsersDisplayMode",
                ref nonXivUsersDisplayMode,
                nonXivUsersDisplayModes,
                nonXivUsersDisplayModes.Length
            )) {
            this.plugin.Configuration.NonXivUsersDisplayMode = (NonXivUsersDisplayMode)nonXivUsersDisplayMode;
            this.plugin.Configuration.Save();
        }

        ImGui.Separator();
        ImGui.Text("Potential issues:");
        ImGui.BulletText(
            "“I get yellow boxes instead of voice activity indicators...”"
            + "\n Get the relevant people to put their character names (first or last name is"
            + "\n good enough) in their Discord nicknames, or click “Advanced Individual Assignments”"
            + "\n at the bottom of this window."
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
            ImGui.Text("This text deliberately looks weird in dev mode.");
            ImGui.SameLine();
            if (ImGui.Button("Open debug window")) {
                this.plugin.MainWindow.IsOpen = true;
            }
        }

        if (this.plugin.PluginInterface.IsDev || this.plugin.PluginInterface.IsTesting) {
            ImGui.Text("Hello, tester! Please ping me (sersorrel) with any issues you find :)");
        }

        if (this.plugin.PluginInterface.IsDev || !this.plugin.PluginInterface.IsTesting) {
            ImGui.Text("If you report issues on Discord, ping me (sersorrel) or I will not see your message!");
        }

        ImGui.Separator();

        if (ImGui.TreeNode("Advanced Individual Assignments")) {
            ImGui.BulletText(
                "Note: the Discord User ID is the unique User ID, it is not the Discord username."
                + Environment.NewLine
                + "To obtain the User ID you need to go to Discord Settings -> Advanced and enable Developer Mode."
                + Environment.NewLine
                + "Then right click a user and select \"Copy User ID\"."
            );
            if (ImGui.BeginTable("AssignmentTable", 3)) {
                ImGui.TableSetupColumn("Character Name");
                ImGui.TableSetupColumn("Discord User ID");
                ImGui.TableSetupColumn("Commands");
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
                for (var i = 0; i < this.individualAssignments.Count; i++) {
                    ImGui.TableNextColumn();
                    var charaName = this.individualAssignments[i].CharacterName;
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.InputText("###nameEntry" + i, ref charaName, 255)) {
                        this.individualAssignments[i].CharacterName = charaName;
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(200);
                    var discordId = this.individualAssignments[i].DiscordId;
                    if (ImGui.InputText("###discordIdEntry" + i, ref discordId, 255)) {
                        this.individualAssignments[i].DiscordId = discordId;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button("Delete###deleteEntry" + i)) {
                        this.individualAssignments.RemoveAt(i);
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button("Add new Assignment")) {
                this.individualAssignments.Add(new AssignmentEntry());
            }

            var anyChanges = false;
            if (this.individualAssignments.Count != this.plugin.Configuration.IndividualAssignments.Count) {
                anyChanges = true;
            }

            if (!anyChanges) {
                for (var i = 0; i < this.individualAssignments.Count; i++) {
                    if (!string.Equals(
                            this.individualAssignments[i].CharacterName,
                            this.plugin.Configuration.IndividualAssignments[i].CharacterName
                        )) {
                        anyChanges = true;
                        break;
                    }

                    if (!string.Equals(
                            this.individualAssignments[i].DiscordId,
                            this.plugin.Configuration.IndividualAssignments[i].DiscordId
                        )) {
                        anyChanges = true;
                        break;
                    }
                }
            }

            var configInvalid = this.individualAssignments.Any(
                p => string.IsNullOrEmpty(p.DiscordId) || string.IsNullOrEmpty(p.CharacterName)
            );
            configInvalid |= this.individualAssignments.Count
                != this.individualAssignments.Select(p => p.CharacterName).Distinct().Count();
            if (!configInvalid && anyChanges) {
                ImGui.TextColored(ImGuiColors.DalamudYellow, "Warning: you have unsaved changes.");
            }

            if (configInvalid) {
                ImGui.TextColored(
                    ImGuiColors.DalamudYellow,
                    "The configuration is invalid."
                    + Environment.NewLine
                    + "  - All entries require to have a value"
                    + Environment.NewLine
                    + "  - Duplicate character names are not allowed"
                );
            }

            if (configInvalid || !anyChanges) {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Reset")) {
                this.ResetListToConfig();
            }

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.Text("Resets the configuration to the last saved state");
                ImGui.EndTooltip();
            }

            ImGui.SameLine();

            if (ImGui.Button("Save")) {
                this.plugin.Configuration.IndividualAssignments.Clear();
                foreach (var entry in this.individualAssignments) {
                    this.plugin.Configuration.IndividualAssignments.Add(
                        new AssignmentEntry {
                            CharacterName = entry.CharacterName,
                            DiscordId = entry.DiscordId,
                        }
                    );
                }

                this.plugin.Configuration.Save();
                anyChanges = false;
            }

            if (configInvalid || !anyChanges) {
                ImGui.EndDisabled();
            }

            ImGui.TreePop();
        }
    }

    private void ResetListToConfig() {
        this.individualAssignments.Clear();
        foreach (var entry in this.plugin.Configuration.IndividualAssignments) {
            this.individualAssignments.Add(
                new AssignmentEntry {
                    CharacterName = entry.CharacterName,
                    DiscordId = entry.DiscordId,
                }
            );
        }
    }
}
