using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class ConfigWindow: Window, IDisposable {
    private readonly List<AssignmentEntry> individualAssignments;
    private readonly Plugin plugin;
    private readonly ISharedImmediateTexture previewImage;
    private int idInCallIdx;

    private int playerInPartyIdx;

    public ConfigWindow(Plugin plugin): base("Who's Talking configuration") {
        this.plugin = plugin;
        this.individualAssignments = new List<AssignmentEntry>();
        this.ResetListToConfig();

        this.SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(600, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        // Image for preview
        var path = Path.Combine(this.plugin.PluginInterface.AssemblyLocation.Directory?.FullName!, "images");
        path = Path.Combine(path, "previewImage.png");
        // this.previewImage = this.plugin.PluginInterface.UiBuilder.LoadImage(path);
        this.previewImage = this.plugin.TextureProvider.GetFromFile(path);
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
                ImGui.Text("Connected to Discord, but not in a call.");
            }
        } else {
            ImGui.Text("Not connected to Discord. (Open the Discord app to get started!)");
        }

        ImGui.Separator();
        var showIndicators = this.plugin.Configuration.ShowIndicators;
        if (ImGui.Checkbox("Show voice activity indicators (DelvUI users, disable this!)", ref showIndicators)) {
            this.plugin.Configuration.ShowIndicators = showIndicators;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                "There's no easy way for plugins to know if you hid the party list, annoyingly."
                + "\nIf you use DelvUI (or any other plugin that completely replaces the party list),"
                + "\nyou can disable this to hide the voice activity indicators on the vanilla party list."
                + "\nThe indicators in DelvUI will continue to function."
            );
        }

        var indicatorStyle = (int)this.plugin.Configuration.IndicatorStyle;
        var indicatorStyles = Enum.GetValues(typeof(IndicatorStyle))
            .Cast<IndicatorStyle>()
            .Select(val => val.ToString())
            .ToArray();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Indicator style:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("###indicatorStyle", ref indicatorStyle, indicatorStyles, indicatorStyles.Length)) {
            this.plugin.Configuration.IndicatorStyle = (IndicatorStyle)indicatorStyle;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                "The \"Atk\" setting (using the game's own UI framework) is experimental!"
                + "\nPlease report any issues in Discord."
            );
        }

        var useRoundedCorners = this.plugin.Configuration.UseRoundedCorners;
        if (this.plugin.Configuration.IndicatorStyle == IndicatorStyle.Imgui
            && ImGui.Checkbox(
                "Use rounded corners for voice activity indicators (Material UI/Frost UI users, disable this!)",
                ref useRoundedCorners
            )) {
            this.plugin.Configuration.UseRoundedCorners = useRoundedCorners;
            this.plugin.Configuration.Save();
        }

        var nonXivUsersDisplayMode = (int)this.plugin.Configuration.NonXivUsersDisplayMode;
        var nonXivUsersDisplayModes = Enum.GetValues(typeof(NonXivUsersDisplayMode))
            .Cast<NonXivUsersDisplayMode>()
            .Select(val => val.ToString())
            .ToArray();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("List speaking Discord users not in your party:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo(
                "###nonXivUsersDisplayMode",
                ref nonXivUsersDisplayMode,
                nonXivUsersDisplayModes,
                nonXivUsersDisplayModes.Length
            )) {
            this.plugin.Configuration.NonXivUsersDisplayMode = (NonXivUsersDisplayMode)nonXivUsersDisplayMode;
            this.plugin.Configuration.Save();
        }

        if (this.plugin.Configuration.NonXivUsersDisplayMode == NonXivUsersDisplayMode.ManuallyPositioned) {
            var nonXivUsersX = this.plugin.Configuration.NonXivUsersX;
            var nonXivUsersY = this.plugin.Configuration.NonXivUsersY;
            ImGui.Text("Position:");
            ImGui.SameLine();
            if (ImGui.DragInt("###nonXivUsersX", ref nonXivUsersX)) {
                this.plugin.Configuration.NonXivUsersX = nonXivUsersX;
                this.plugin.Configuration.Save();
            }

            ImGui.SameLine();
            if (ImGui.DragInt("###nonXivUsersY", ref nonXivUsersY)) {
                this.plugin.Configuration.NonXivUsersY = nonXivUsersY;
                this.plugin.Configuration.Save();
            }
        }

        var showNonXivUsersAlways = this.plugin.Configuration.ShowNonXivUsersAlways;
        if (ImGui.Checkbox(
                "Always show all Discord users not in your party, regardless of voice activity",
                ref showNonXivUsersAlways
            )) {
            this.plugin.Configuration.ShowNonXivUsersAlways = showNonXivUsersAlways;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered() && !showIndicators) {
            ImGui.SetTooltip(
                "Because “Show voice activity indicators” is disabled,"
                + "\nthis setting has no effect."
            );
        }

        var showUnmatchedUsers = this.plugin.Configuration.ShowUnmatchedUsers;
        if (ImGui.Checkbox("Show boxes for unmatched users", ref showUnmatchedUsers)) {
            this.plugin.Configuration.ShowUnmatchedUsers = showUnmatchedUsers;
            this.plugin.Configuration.Save();
        }

        ImGui.SetNextItemWidth(150);
        var discordPort = this.plugin.Configuration.Port;
        if (ImGui.InputInt("Discord port", ref discordPort)) {
            this.plugin.Configuration.Port = discordPort;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                "Useful when running multiple clients or other software blocking the default port.\n"
                + "If a port is already in use, increment the port by one and try again.\n"
                + "Typical values will be between 6463 and 6472.\n"
                + "If in doubt, use the default value 6463."
            );
        }

        if (ImGui.Button("Reconnect")) {
            this.plugin.ReconnectDiscord();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("Try to connect to the set port.");
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(
            this.plugin.Connection.IsConnected
                ? $"Connected via {this.plugin.Connection.ApiEndpoint ?? "unknown endpoint"}"
                : "Failed to connect"
        );


        if (ImGui.IsItemHovered() && !showIndicators) {
            ImGui.SetTooltip(
                "Because “Show voice activity indicators” is disabled,"
                + "\nthis setting has no effect."
            );
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

        // Colour Assignments
        ImGui.Separator();
        if (ImGui.TreeNode("Colour Assignments")) {
            var colspk = this.plugin.Configuration.ColourSpeaking;
            if (this.ColourConfig("Speaking", colspk, ref colspk)) {
                this.plugin.Configuration.ColourSpeaking = colspk;
                this.plugin.Configuration.Save();
            }

            var colmuted = this.plugin.Configuration.ColourMuted;
            if (this.ColourConfig("Muted", colmuted, ref colmuted)) {
                this.plugin.Configuration.ColourMuted = colmuted;
                this.plugin.Configuration.Save();
            }

            var coldeafened = this.plugin.Configuration.ColourDeafened;
            if (this.ColourConfig("Deafened", coldeafened, ref coldeafened)) {
                this.plugin.Configuration.ColourDeafened = coldeafened;
                this.plugin.Configuration.Save();
            }

            if (this.plugin.Configuration.ShowUnmatchedUsers) {
                var colunm = this.plugin.Configuration.ColourUnmatched;
                if (this.ColourConfig("Unmatched", colunm, ref colunm)) {
                    this.plugin.Configuration.ColourUnmatched = colunm;
                    this.plugin.Configuration.Save();
                }
            }

            ImGui.TreePop();
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

                // Extant entries
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

                // New Entry dropdowns

                // Prep: grap extant player names and discord ids
                var extantPlayerNames = new List<string>();
                foreach (var item in this.plugin.Configuration.IndividualAssignments) {
                    extantPlayerNames.Add(item.CharacterName);
                }

                // XIV player name
                // This is kinda transcribed from the overlay code
                List<string> playersInParty;
                unsafe {
                    // Get party list and players in it
                    var partyInfoProxy = InfoProxyPartyMember.Instance();
                    var partyMemberCount = partyInfoProxy->InfoProxyCommonList.DataSize;
                    playersInParty = new List<string>();

                    if (partyInfoProxy != null) {
                        for (uint i = 0; i < partyMemberCount; i++) {
                            var entry = partyInfoProxy->InfoProxyCommonList.GetEntry(i);
                            if (entry == null) {
                                continue;
                            }

                            var name = entry->NameString;
                            // Don't add people we already know
                            if (name == null || extantPlayerNames.Contains(name)) {
                                continue;
                            }

                            playersInParty.Add(name);
                        }
                    }

                    var _playersInParty = playersInParty.ToArray();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(150);
                    ImGui.Combo(
                        "###PlayersInParty",
                        ref this.playerInPartyIdx,
                        _playersInParty,
                        _playersInParty.Length
                    );
                }

                // Discord ID
                var discordusers = this.plugin.Connection.AllUsers;
                var idsInCall = new List<string>(); // actual ids
                var idsWithName = new List<string>(); // name with id in brackets
                foreach (var user in discordusers.Values) {
                    // Don't add people we already know, don't add people who aren't real
                    if (user.Username == null) {
                        continue;
                    }

                    idsInCall.Add(user.UserId);
                    idsWithName.Add("{0} ({1})".Format(user.Username!, user.UserId));
                }

                var _idsWithName = idsWithName.ToArray();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(200);
                ImGui.Combo(
                    "###IdsInCall",
                    ref this.idInCallIdx,
                    _idsWithName,
                    _idsWithName.Length
                );

                // Add entry to list
                ImGui.TableNextColumn();
                if (ImGui.Button("Add Entry")) {
                    var _charaName = this.playerInPartyIdx < playersInParty.Count
                        ? playersInParty[this.playerInPartyIdx]
                        : "";
                    var _discId = this.playerInPartyIdx < idsInCall.Count ? idsInCall[this.idInCallIdx] : "";

                    var i = this.individualAssignments.Count;
                    this.individualAssignments.Add(new AssignmentEntry());

                    this.individualAssignments[i].CharacterName = _charaName;
                    this.individualAssignments[i].DiscordId = _discId;
                }

                // Blank entry button
                ImGui.SameLine();
                if (ImGui.Button("Add blank Entry")) {
                    var i = this.individualAssignments.Count;
                    this.individualAssignments.Add(new AssignmentEntry());

                    this.individualAssignments[i].CharacterName = "";
                    this.individualAssignments[i].DiscordId = "";
                }

                ImGui.EndTable();
            }

            if (ImGui.Button("Add manual Assignment")) {
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

    // Represents one colour for colour configuation
    private bool ColourConfig(string label, uint colour, ref uint rcolour) {
        ImGui.Text(label);

        var newcol = ImGui.ColorConvertU32ToFloat4(colour);
        var r = ImGui.ColorEdit4("###ColourEdit_{0}".Format(label), ref newcol);
        if (r) {
            rcolour = ImGui.ColorConvertFloat4ToU32(newcol);
        }

        ImGui.SameLine();
        ImGui.Text("Preview");

        // Draws a preview of what the outline will look like
        // Isn't perfect but it's good enough
        ImGui.SameLine();
        var scroll = new Vector2(ImGui.GetScrollX(), ImGui.GetScrollY());
        var preview_min = ImGui.GetWindowPos() + ImGui.GetCursorPos() - scroll;
        var preview_max = preview_min + new Vector2(25, 25);
        var img = this.previewImage.GetWrapOrDefault();
        if (img is not null) {
            ImGui.GetWindowDrawList()
                .AddImage(
                    img.ImGuiHandle,
                    preview_min,
                    preview_max
                );
        }

        ImGui.GetWindowDrawList()
            .AddRect(
                preview_min,
                preview_max,
                ImGui.ColorConvertFloat4ToU32(newcol),
                7,
                ImDrawFlags.RoundCornersAll,
                2
            );
        ImGui.Text("");

        return r;
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
