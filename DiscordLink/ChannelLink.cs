﻿using Eco.Plugins.DiscordLink.Utilities;
using System;
using System.ComponentModel;

namespace Eco.Plugins.DiscordLink
{
    public enum GlobalMentionPermission
    {
        AnyUser,        // Any user may use mentions
        Admin,          // Only admins may use mentions
        Forbidden       // All use of mentions is forbidden
    };

    public enum ChatSyncDirection
    {
        DiscordToEco,   // Send Discord messages to Eco
        EcoToDiscord,   // Send Eco messages to Discord
        Duplex,         // Send Discord messages to Eco and Eco messages to Discord
    }

    #region ChannelLink

    public abstract class ChannelLink : ICloneable
    {
        [Description("Discord Guild (Server) by name or ID.")]
        public string DiscordGuild { get; set; } = string.Empty;

        [Description("Discord Channel by name or ID.")]
        public string DiscordChannel { get; set; } = string.Empty;

        public virtual bool IsVoiceChannel { get; protected set; } = false;

        public override string ToString()
        {
            return DiscordGuild + " - " + DiscordChannel;
        }

        public object Clone()
        {
            return MemberwiseClone();
        }

        virtual public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(DiscordGuild) && !string.IsNullOrWhiteSpace(DiscordChannel);
        }

        virtual public bool Verify()
        {
            if (string.IsNullOrWhiteSpace(DiscordGuild) || string.IsNullOrWhiteSpace(DiscordChannel)) return false;

            var guild = DiscordLink.Obj.GuildByNameOrId(DiscordGuild);
            if (guild == null)
            {
                return false; // The channel will always fail if the guild fails
            }
            var channel = guild.ChannelByNameOrId(DiscordChannel);
            if (channel == null)
            {
                return false;
            }

            return true;
        }

        virtual public bool MakeCorrections()
        {
            return false;
        }
    }

    #endregion

    #region EcoChannelLink

    public class EcoChannelLink : ChannelLink
    {
        [Description("Eco Channel (with # omitted) to use.")]
        public string EcoChannel { get; set; } = string.Empty;

        public override string ToString()
        {
            return DiscordGuild + " - " + DiscordChannel + " <--> " + EcoChannel;
        }

        public override bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(DiscordGuild) && !string.IsNullOrWhiteSpace(DiscordChannel) && !string.IsNullOrWhiteSpace(EcoChannel);
        }

        public override bool MakeCorrections()
        {
            bool correctionMade = base.MakeCorrections();
            string original = EcoChannel;
            EcoChannel = EcoChannel.Trim('#');
            if (EcoChannel != original)
            {
                correctionMade = true;
                Logger.Info("Corrected Eco channel name with Guild name/ID \"" + DiscordGuild + "\" and Discord Channel name/ID \"" + DiscordChannel + "\" from \"" + original + "\" to \"" + EcoChannel + "\"");
            }
            return correctionMade;
        }
    }

    public class ChatChannelLink : EcoChannelLink
    {
        [Description("Allow mentions of usernames to be forwarded from Eco to the Discord channel.")]
        public bool AllowUserMentions { get; set; } = true;

        [Description("Allow mentions of roles to be forwarded from Eco to the Discord channel.")]
        public bool AllowRoleMentions { get; set; } = true;

        [Description("Allow mentions of channels to be forwarded from Eco to the Discord channel.")]
        public bool AllowChannelMentions { get; set; } = true;

        [Description("Sets which direction chat should synchronize in.")]
        public ChatSyncDirection Direction { get; set; } = ChatSyncDirection.Duplex;

        [Description("Permissions for who is allowed to forward mentions of @here or @everyone from Eco to the Discord channel.")]
        public GlobalMentionPermission HereAndEveryoneMentionPermission { get; set; } = GlobalMentionPermission.Forbidden;
    }

    #endregion

    #region TextChannelLink

    public class TextChannelLink : ChannelLink
    {
        public override bool MakeCorrections()
        {
            if (string.IsNullOrWhiteSpace(DiscordChannel)) return false;

            bool correctionMade = false;
            string original = DiscordChannel;
            if (DiscordChannel != DiscordChannel.ToLower()) // Discord channels are always lowercase
            {
                DiscordChannel = DiscordChannel.ToLower();
            }

            if (DiscordChannel.Contains(" "))
            {
                DiscordChannel = DiscordChannel.Replace(' ', '-'); // Discord channels always replace spaces with dashes
            }

            if (DiscordChannel != original)
            {
                correctionMade = true;
                Logger.Info("Corrected Discord channel name with Guild name/ID \"" + DiscordGuild + "\" from \"" + original + "\" to \"" + DiscordChannel + "\"");
            }

            return correctionMade;
        }
    }

    public class PlayerListChannelLink : TextChannelLink
    {
        [Description("Display the number of online players in the message.")]
        public bool UsePlayerCount { get; set; } = true;

        [Description("Display how long the player has been logged in for.")]
        public bool UseLoggedInTime { get; set; } = false;
    }

    public class ServerInfoChannel : TextChannelLink
    {
        [Description("Display the server name in the message.")]
        public bool UseName { get; set; } = true;

        [Description("Display the server description in the message.")]
        public bool UseDescription { get; set; } = false;

        [Description("Display the server logo in the message.")]
        public bool UseLogo { get; set; } = true;

        [Description("Display the server IP address in the message.")]
        public bool UseAddress { get; set; } = true;

        [Description("Display the number of online players in the message.")]
        public bool UsePlayerCount { get; set; } = true;

        [Description("Display a list of online players in the message.")]
        public bool UsePlayerList { get; set; } = true;

        [Description("Display the time since the world was created in the message.")]
        public bool UseTimeSinceStart { get; set; } = true;

        [Description("Display the time remaining until meteor impact in the message.")]
        public bool UseTimeRemaining { get; set; } = true;

        [Description("Display a boolean for if the meteor has hit yet or not, in the message.")]
        public bool UseMeteorHasHit { get; set; } = false;

        [Description("Display the number of active elections in the message.")]
        public bool UseElectionCount { get; set; } = false;

        [Description("Display a list of all active elections in the message.")]
        public bool UseElectionList { get; set; } = true;

        [Description("Display the number of active laws in the message.")]
        public bool UseLawCount { get; set; } = false;

        [Description("Display a list of all active laws in the message.")]
        public bool UseLawList { get; set; } = true;
    }

    #endregion

    #region VoiceChannelLink

    public class VoiceChannelLink : ChannelLink
    {
        public override bool IsVoiceChannel { get; protected set; } = true;
    }

    #endregion
}
