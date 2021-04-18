using DSharpPlus;
using DSharpPlus.Entities;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Shared.Localization;
using Eco.Plugins.DiscordLink.Utilities;
using System;
using System.Collections.Generic;

namespace Eco.Plugins.DiscordLink
{
    public class EcoCommands : IChatCommandHandler
    {
#pragma warning disable CS4014 // Call not awaited (Shared commands are async but Eco commands can't be)

        #region Commands Base

        private delegate void EcoCommand(User callingUser, params string[] parameters);

        private static void ExecuteCommand<TRet>(EcoCommand command, User callingUser, params string[] parameters)
        {
            // Trim the arguments since they often have a space at the beginning
            for (int i = 0; i < parameters.Length; ++i)
            {
                parameters[i] = parameters[i].Trim();
            }

            string commandName = command.Method.Name;
            try
            {
                Logger.Debug($"{MessageUtils.StripTags(callingUser.Name)} invoked Eco command \"/{command.Method.Name}\"");
                command(callingUser, parameters);
            }
            catch (Exception e)
            {
                ChatManager.ServerMessageToPlayer(new LocString($"Error occurred while attempting to run that command. Error message: {e}"), callingUser);
                Logger.Error($"An exception occured while attempting to execute a command.\nCommand name: \"{commandName}\"\nCalling user: \"{MessageUtils.StripTags(callingUser.Name)}\"\nError message: {e}");
            }
        }

        [ChatCommand("Commands for the Discord integration plugin.", "DL", ChatAuthorizationLevel.User)]
#pragma warning disable IDE0060 // Remove unused parameter - callingUser parameter required
        public static void DiscordLink(User callingUser) { }
#pragma warning restore IDE0060

        #endregion

        #region User Feedback

        public static void ReportCommandError(User callingUser, string message)
        {
            callingUser.Player.Error(Localizer.NotLocalizedStr(message));
        }

        public static void ReportCommandInfo(User callingUser, string message)
        {
            callingUser.Player.InfoBox(Localizer.NotLocalizedStr(message));
        }

        public static void DisplayCommandData(User callingUser, string title, string data)
        {
            callingUser.Player.OpenInfoPanel(title, data, "DiscordLink");
        }

        #endregion

        #region Plugin Management

        [ChatSubCommand("Restart", "Restarts the plugin.", "DL-Restart", ChatAuthorizationLevel.Admin)]
        public static void Restart(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.Restart(SharedCommands.CommandSource.Eco, callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Resets world data as if a new world had been created.", "DL-ResetData", ChatAuthorizationLevel.Admin)]
        public static void ResetWorldData(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.ResetWorldData(SharedCommands.CommandSource.Eco, callingUser);
            }, callingUser);
        }

        #endregion

        #region Meta

        [ChatSubCommand("DiscordLink", "Displays information about the DiscordLink plugin.", "DL-About", ChatAuthorizationLevel.User)]
        public static void About(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                DisplayCommandData(callingUser, $"About DiscordLink {Plugins.DiscordLink.DiscordLink.Obj.PluginVersion}", MessageBuilder.Shared.GetAboutMessage());
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Shows the plugin status.", "DL-Status", ChatAuthorizationLevel.Admin)]
        public static void PluginStatus(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                DisplayCommandData(callingUser, "DiscordLink Status", MessageBuilder.Shared.GetDisplayString(verbose: false));
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Shows the plugin status including verbose debug level information.", "DL-StatusVerbose", ChatAuthorizationLevel.Admin)]
        public static void PluginStatusVerbose(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                DisplayCommandData(callingUser, "DiscordLink Status Verbose", MessageBuilder.Shared.GetDisplayString(verbose: true));
            }, callingUser);
        }

        #endregion

        #region Discord Bot Info Fetching

        [ChatSubCommand("DiscordLink", "Lists Discord servers the bot is in.", ChatAuthorizationLevel.Admin)]
        public static void ListGuilds(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;
                string joinedGuildNames = string.Join("\n", plugin.Client.DiscordClient.GuildNames());

                DisplayCommandData(callingUser, "Connected Discord Servers", joinedGuildNames);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Lists channels available to the bot in a specific server.", ChatAuthorizationLevel.Admin)]
        public static void ListChannels(User callingUser, string guildNameOrID)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;

                DiscordGuild guild = string.IsNullOrEmpty(guildNameOrID)
                    ? plugin.Client.DiscordClient.DefaultGuild()
                    : plugin.Client.GuildByNameOrID(guildNameOrID);

                // Can happen if DefaultGuild is not configured.
                if (guild == null)
                    ReportCommandError(callingUser, $"Failed to find guild with name \"{guildNameOrID}\"");

                string joinedChannelNames = string.Join("\n", guild.TextChannelNames());
                DisplayCommandData(callingUser, "Connected Discord Servers", joinedChannelNames);
            }, callingUser);
        }

        #endregion

        #region Message Relaying

        [ChatSubCommand("DiscordLink", "Sends a message to a specific server and channel.", ChatAuthorizationLevel.Admin)]
        public static void SendMessageToDiscordChannel(User callingUser, string guildNameOrID, string channelNameOrID, string message)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;

                DiscordGuild guild = plugin.Client.GuildByNameOrID(guildNameOrID);
                if(guild == null)
                {
                    ReportCommandError(callingUser, $"No guild with the name or ID \"{guildNameOrID}\" could be found.");
                    return;
                }

                DiscordChannel channel = guild.ChannelByNameOrID(channelNameOrID);
                if (channel == null)
                {
                    ReportCommandError(callingUser, $"No channel with the name or ID \"{channelNameOrID}\" could be found in the guild \"{guild.Name}\".");
                    return;
                }

                _ = plugin.Client.SendMessageAsync(channel, $"**{callingUser.Name.Replace("@", "")}**: {message}");
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco server message to a specified user.", "DL-Servermessage", ChatAuthorizationLevel.Admin)]
        public static void SendServerMessage(User callingUser, string message, string recipientUserName, string persistanceType = "temporary")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.SendServerMessage(SharedCommands.CommandSource.Eco, callingUser, message, recipientUserName, persistanceType);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco server message to all online users.", "DL-BroadcastServerMessage", ChatAuthorizationLevel.Admin)]
        public static void BroadcastServerMessage(User callingUser, string message, string persistanceType = "temporary")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.SendServerMessage(SharedCommands.CommandSource.Eco, callingUser, message, string.Empty, persistanceType);
            }, callingUser);
        }


        [ChatSubCommand("DiscordLink", "Sends an Eco popup message to a specified user.", "DL-Popup", ChatAuthorizationLevel.Admin)]
        public static void SendPopup(User callingUser, string message, string recipientUserName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.SendPopup(SharedCommands.CommandSource.Eco, callingUser, message, recipientUserName);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco popup message to all online users.", "DL-BroadcastPopup", ChatAuthorizationLevel.Admin)]
        public static void BroadcastPopup(User callingUser, string message)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.SendPopup(SharedCommands.CommandSource.Eco, callingUser, message, string.Empty);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco announcement message to a specified user.", "DL-Announcement", ChatAuthorizationLevel.Admin)]
        public static void SendAnnouncement(User callingUser, string title, string message, string recipientUserName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.SendAnnouncement(SharedCommands.CommandSource.Eco, callingUser, title, message, recipientUserName);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco announcement message to a specified user.", "DL-BroadcastAnnouncement", ChatAuthorizationLevel.Admin)]
        public static void BroadcastAnnouncement(User callingUser, string title, string message)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.SendAnnouncement(SharedCommands.CommandSource.Eco, callingUser, title, message, string.Empty);
            }, callingUser);
        }

        #endregion

        #region Invites

        [ChatSubCommand("DiscordLink", "Posts the Discord invite message to the target user. The invite will be broadcasted if no target user is specified.", "DL-Invite", ChatAuthorizationLevel.User)]
        public static void Invite(User callingUser, string targetUserName = "")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.DiscordInvite(SharedCommands.CommandSource.Eco, callingUser, targetUserName);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Posts the Discord invite message to the Eco chat.", "DL-BroadcastInvite", ChatAuthorizationLevel.User)]
        public static void BroadcastInvite(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.DiscordInvite(SharedCommands.CommandSource.Eco, callingUser, string.Empty);
            }, callingUser);
        }

        #endregion

        #region Account Linking

        [ChatSubCommand("DiscordLink", "Links the calling user account to a Discord account.", "DL-Link", ChatAuthorizationLevel.User)]
        public static void LinkDiscordAccount(User callingUser, string discordName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;

                if (!plugin.Client.BotHasIntent(DiscordIntents.GuildMembers))
                {
                    ReportCommandError(callingUser, $"This server is not configured to use account linking as the bot lacks the elevated Guild Members Intent.");
                    return;
                }

                // Find the Discord user
                DiscordMember matchingMember = null;
                foreach (DiscordGuild guild in plugin.Client.DiscordClient.Guilds.Values)
                {
                    IReadOnlyCollection<DiscordMember> guildMembers = plugin.Client.GetGuildMembersAsync(guild).Result;
                    if (guildMembers == null)
                        continue;

                    foreach (DiscordMember member in guildMembers)
                    {
                        if (member.HasNameOrID(discordName))
                        {
                            matchingMember = member;
                            break;
                        }
                    }
                }

                if (matchingMember == null)
                {
                    ReportCommandError(callingUser, $"No Discord account with the ID or name \"{discordName}\" could be found.");
                    return;
                }

                // Make sure that the accounts aren't already linked to any account
                foreach (LinkedUser linkedUser in DLStorage.PersistentData.LinkedUsers)
                {
                    if (callingUser.SlgId == linkedUser.SlgID || callingUser.SteamId == linkedUser.SteamID)
                    {
                        if (linkedUser.DiscordID == matchingMember.Id.ToString())
                            ReportCommandInfo(callingUser, "Eco account is already linked to this Discord account.\nUse /DL-Unlink to remove the existing link.");
                        else
                            ReportCommandInfo(callingUser, "Eco account is already linked to a different Discord account.\nUse /DL-Unlink to remove the existing link.");
                        return;
                    }
                    else if (linkedUser.DiscordID == matchingMember.Id.ToString())
                    {
                        ReportCommandError(callingUser, "Discord account is already linked to a different Eco account.");
                        return;
                    }
                }

                // Create a linked user from the combined Eco and Discord info
                LinkedUserManager.AddLinkedUser(callingUser, matchingMember.Id.ToString(), matchingMember.Guild.Id.ToString());

                // Notify the Discord account that a link has been made and ask for verification
                _ = plugin.Client.SendDMAsync(matchingMember, null, MessageBuilder.Discord.GetVerificationDM(callingUser));

                // Notify the Eco user that the link has been created and that verification is required
                ReportCommandInfo(callingUser, $"Your account has been linked.\nThe link requires verification before becoming active.\nInstructions have been sent to the linked Discord account.");
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Unlinks the calling user account from a linked Discord account.", "DL-Unlink", ChatAuthorizationLevel.User)]
        public static void UnlinkDiscordAccount(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                bool result = LinkedUserManager.RemoveLinkedUser(callingUser);
                if (result)
                    ReportCommandInfo(callingUser, $"Discord account unlinked.");
                else
                    ReportCommandError(callingUser, $"No linked Discord account could be found.");
            }, callingUser);
        }

        #endregion

        #region Trades

        [ChatSubCommand("DiscordLink", "Displays available trades by player, item or tag.", "DL-Trades", ChatAuthorizationLevel.User)]
        public static void Trades(User callingUser, string userOrItemName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.Trades(SharedCommands.CommandSource.Eco, callingUser, userOrItemName);
            }, callingUser);
        }

        // Wrapper for the Trades command in order to facilitate more command aliases
        [ChatSubCommand("DiscordLink", "Displays available trades by player, item or tag.", "DLT", ChatAuthorizationLevel.User)]
        public static void Trade(User user, string userOrItemName)
        {
            Trades(user, userOrItemName);
        }

        [ChatSubCommand("DiscordLink", "Creates a live updated display of available trades by player, item or tag.", "DL-TrackTrades", ChatAuthorizationLevel.User)]
        public static void TrackTrades(User callingUser, string userOrItemName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.TrackTrades(SharedCommands.CommandSource.Eco, callingUser, userOrItemName);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Removes the live updated display of available trades for the player or item.", "DL-StopTrackTrades", ChatAuthorizationLevel.User)]
        public static void StopTrackTrades(User callingUser, string userOrItemName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.StopTrackTrades(SharedCommands.CommandSource.Eco, callingUser, userOrItemName);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Lists all tracked trades for the calling user.", "DL-ListTrackedTrades", ChatAuthorizationLevel.User)]
        public static void ListTrackedTrades(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.ListTrackedTrades(SharedCommands.CommandSource.Eco, callingUser);
            }, callingUser);
        }

        #endregion

        #region Snippets

        [ChatSubCommand("DiscordLink", "Post a predefined snippet from Discord.", "DL-Snippet", ChatAuthorizationLevel.User)]
        public static void Snippet(User callingUser, string snippetKey = "")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;

                var snippets = DLStorage.Instance.Snippets;
                if (string.IsNullOrWhiteSpace(snippetKey)) // List all snippets if no key is given
                {
                    if(snippets.Count > 0)
                        DisplayCommandData(callingUser, "Snippets", string.Join("\n", snippets.Keys));
                    else
                        ReportCommandInfo(callingUser, "There are no registered snippets.");
                }
                else
                {
                    // Find and post the snippet requested by the user
                    if (snippets.TryGetValue(snippetKey, out string snippetText))
                    {
                        EcoUtils.SendServerMessage($"{callingUser.Name} invoked snippet \"{snippetKey}\"\n- - -\n{snippetText}\n- - -", permanent: true);
                    }
                    else
                    {
                        ReportCommandError(callingUser, $"No snippet with key \"{snippetKey}\" could be found.");
                    }
                }
            }, callingUser);
        }

        #endregion

#pragma warning restore CS4014 // Call not awaited
    }
}