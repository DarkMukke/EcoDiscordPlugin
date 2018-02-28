﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Eco.Core;
using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Shared.Services;
using Eco.Shared.Utils;

namespace Eco.Plugins.DiscordLink
{
    public class DiscordLink : IModKitPlugin, IInitializablePlugin, IConfigurablePlugin
    {
        private PluginConfig<DiscordConfig> configOptions;
        private DiscordClient _discordClient;
        private CommandsNextModule _commands;
        private string _currentToken;
        private string _status = "No Connection Attempt Made";

        protected ChatNotifier chatNotifier;

        public override string ToString()
        {
            return "DiscordLink";
        }

        public IPluginConfig PluginConfig
        {
            get { return configOptions; }
        }

        public DiscordConfig DiscordPluginConfig
        {
            get { return PluginConfig.GetConfig() as DiscordConfig; }
        }

        public string GetStatus()
        {
            return _status;
        }

        public void Initialize()
        {
            if (_discordClient == null) return;
            ConnectAsync();
            StartChatNotifier();
        }

        private void StartChatNotifier()
        {
            chatNotifier.Initialize();
            new Thread(() => { chatNotifier.Run(); })
            {
                Name = "ChatNotifierThread"
            }.Start();
        }

        public DiscordLink()
        {
            configOptions = new PluginConfig<DiscordConfig>("DiscordPluginSpoffy");
            chatNotifier = new ChatNotifier();
            SetUpClient();
        }

        private async Task<object> DisposeOfClient()
        {
            if (_discordClient != null)
            {
                await DisconnectAsync();
                _discordClient.Dispose();
            }
            return null;
        }

        private bool SetUpClient()
        {
            DisposeOfClient();
            _status = "Setting up client";
            // Loading the configuration
            _currentToken = String.IsNullOrWhiteSpace(DiscordPluginConfig.BotToken)
                ? "ThisTokenWillNeverWork" //Whitespace isn't allowed, and it should trigger an obvious authentication error rather than crashing.
                : DiscordPluginConfig.BotToken;

            try
            {
                // Create the new client
                _discordClient = new DiscordClient(new DiscordConfiguration
                {
                    Token = _currentToken,
                    TokenType = TokenType.Bot
                });

                // Set up the client to use CommandsNext
                _commands = _discordClient.UseCommandsNext(new CommandsNextConfiguration()
                {
                    StringPrefix = "?"
                });

                _commands.RegisterCommands<DiscordDiscordCommands>();

                return true;
            }
            catch (Exception e)
            {
                Log.Write("ERROR: Unable to create the discord client. Error message was: " + e.Message + "\n");
                Log.Write("Backtrace: " + e.StackTrace);
            }

            return false;
        }

        public async Task<bool> RestartClient()
        {
            var result = SetUpClient();
            await ConnectAsync();
            return result;
        }

        public string[] GuildNames
        {
            get {
                if (_discordClient != null)
                {
                    return _discordClient.Guilds.Values.Select((guild => guild.Name)).ToArray();
                }
                return null;
            }
        }

        public DiscordGuild DefaultGuild
        {
            get {
                if (_discordClient != null)
                {
                    return _discordClient.Guilds.FirstOrDefault().Value;
                }

                return null;
            }
        }

        public string[] ChannelsInGuild(DiscordGuild guild)
        {
            return guild != null ? TextChannelsInGuild(guild).Select(channel => channel.Name).ToArray() : new string[0];
        }
        
        public IReadOnlyList<DiscordChannel> TextChannelsInGuild(DiscordGuild guild)
        {
            return guild != null
                ? guild.Channels.Where(channel => channel.Type == ChannelType.Text).ToList()
                : new List<DiscordChannel>();
        }

        public string[] ChannelsByGuildName(string guildName)
        {
            if (_discordClient == null) return new string[0];
            return ChannelsInGuild(GuildByName(guildName));
        }

        public DiscordGuild GuildByName(string name)
        {
            if (_discordClient == null) return null;
            return _discordClient.Guilds.Values.FirstOrDefault(guild => guild.Name == name);

        }

        public DiscordChannel ChannelByName(string guildName, string channelName)
        {
            var guild = GuildByName(guildName);
            return ChannelByName(guild, channelName);
        }

        public DiscordChannel ChannelByName(DiscordGuild guild, string channelName)
        {
            return guild != null? TextChannelsInGuild(guild).FirstOrDefault(channel => channel.Name == channelName) : null;
        }

        public async Task<string> SendMessage(string message, string channelName, string guildName)
        {
            if (_discordClient == null) return "No discord client";
            var guild = GuildByName(guildName);
            if (guild == null) return "No guild of that name found";

            var channel = ChannelByName(guild, channelName);
            return await SendMessage(message, channel);
        }
        
        public async Task<string> SendMessage(string message, DiscordChannel channel)
        {
            if (_discordClient == null) return "No discord client";
            if (channel == null) return "No channel of that name found in that guild";

            await _discordClient.SendMessageAsync(channel, message);
            return "Message sent successfully!";
        }

        public async Task<string> SendMessageAsUser(string message, User user, string channelName, string guildName)
        {
            return await SendMessage(String.Format("*{0}*: {1}", user.Name, message), channelName, guildName);
        }
        
        public async Task<String> SendMessageAsUser(string message, User user, DiscordChannel channel)
        {
            return await SendMessage(String.Format("*{0}*: {1}", user.Name, message), channel);
        }
        
        public async Task<object> ConnectAsync()
        {
            try
            {
                _status = "Attempting connection...";
                await _discordClient.ConnectAsync();
                BeginRelaying();
                Log.Write("Connected to Discord.\n");
                _status = "Connection successful";
                
            } 
            catch (Exception e)
            {
                Log.Write("Error connecting to discord: " + e.Message + "\n");
                _status = "Connection failed";
            }

            return null;
        }
        
        public async Task<object> DisconnectAsync()
        {
            try
            {
                StopRelaying();
                await _discordClient.DisconnectAsync();           
            } 
            catch (Exception e)
            {
                Log.Write("Error Disconnecting from discord: " + e.Message + "\n");
                _status = "Connection failed";
            }

            return null;
        }

        #region MessageRelaying

        private string EcoUserSteamId = "DiscordLinkSteam";
        private string EcoUserSlgId = "DiscordLinkSlg";
        private string EcoUserName = "Discord";
        private User _ecoUser;
        private bool _relayInitialised = false;
        
        protected User EcoUser => _ecoUser ?? (_ecoUser = UserManager.GetOrCreateUser(EcoUserSteamId, EcoUserSlgId, EcoUserName));

        private void BeginRelaying()
        {
            if (!_relayInitialised)
            {
                chatNotifier.OnMessageReceived.Add(OnMessageReceivedFromEco);
                _discordClient.MessageCreated += OnDiscordMessageCreateEvent;
            }

            _relayInitialised = true;
        }

        private void StopRelaying()
        {
            if (_relayInitialised)
            {
                chatNotifier.OnMessageReceived.Remove(OnMessageReceivedFromEco);
                _discordClient.MessageCreated -= OnDiscordMessageCreateEvent;
            }

            _relayInitialised = false;
        }

        public void OnMessageReceivedFromEco(ChatMessage message)
        {
            if (message.Sender == EcoUser.Name) { return; }
            Log.Write("Message: " + message.Text + "\n");
            Log.Write("Tag: " + message.Tag + "\n");
            Log.Write("Category: " + message.Category + "\n");
            Log.Write("Temporary: " + message.Temporary + "\n");
            Log.Write("Sender: " + message.Sender + "\n");
            if (String.IsNullOrWhiteSpace(message.Sender)) { return; };
            SendMessage($"**{message.Sender}**: {message.Text}", "just-spoffy", "Full Duplex Game Testing");
        }

        public async Task OnDiscordMessageCreateEvent(MessageCreateEventArgs messageArgs)
        {
            Log.Write("Message from Discord!");
            OnMessageReceivedFromDiscord(messageArgs.Message);
        }

        public void OnMessageReceivedFromDiscord(DiscordMessage message)
        {
            if (message.Author == _discordClient.CurrentUser) { return; }
            var text = "#Discord " + message.Author.Username + ": " + message.Content;
            ChatManager.SendChat(text, EcoUser);;
        }

        #endregion

        public static DiscordLink Obj
        {
            get { return PluginManager.GetPlugin<DiscordLink>(); }
        }
        
        public object GetEditObject()
        {
            return configOptions.Config;
        }

        public void OnEditObjectChanged(object o, string param)
        {
            
            configOptions.Save();
            if (DiscordPluginConfig.BotToken != _currentToken)
            {
                //Reinitialise client.
                Log.Write("Discord Token changed, reinitialising client.\n");
                RestartClient();
            }
        }
        
        public DiscordPlayerConfig GetOrCreatePlayerConfig(string identifier)
        {
            var config = DiscordPluginConfig.PlayerConfigs.FirstOrDefault(user => user.Username == identifier);
            if (config == null)
            {
                config = new DiscordPlayerConfig
                {
                    Username = identifier
                };
                AddOrReplacePlayerConfig(config);
            }

            return config;
        }

        public bool AddOrReplacePlayerConfig(DiscordPlayerConfig config)
        {
            var removed = DiscordPluginConfig.PlayerConfigs.Remove(config);
            DiscordPluginConfig.PlayerConfigs.Add(config);
            SavePlayerConfig();
            return removed;
        }

        public void SavePlayerConfig()
        {
            configOptions.Save();
        }

        public DiscordChannel GetDefaultChannelForPlayer(string identifier)
        {
            var playerConfig = GetOrCreatePlayerConfig(identifier);
            if (playerConfig.DefaultChannel == null
                || String.IsNullOrEmpty(playerConfig.DefaultChannel.Guild)
                || String.IsNullOrEmpty(playerConfig.DefaultChannel.Channel))
            {
                return null;
            }

            return ChannelByName(playerConfig.DefaultChannel.Guild, playerConfig.DefaultChannel.Channel);
        }
        
        
        public void SetDefaultChannelForPlayer(string identifier, string guildName, string channelName)
        {
            var playerConfig = GetOrCreatePlayerConfig(identifier);
            playerConfig.DefaultChannel.Guild = guildName;
            playerConfig.DefaultChannel.Channel = channelName;
            SavePlayerConfig();
        }
    }

    public class DiscordConfig
    {
        [Description("The token provided by the Discord API to allow access to the bot"), Category("Bot Configuration")]
        public string BotToken { get; set; }
        
        [Description("The name of the Eco server, overriding the name configured within Eco."), Category("Server Details")]
        public string ServerName { get; set; }
        
        [Description("The description of the Eco server, overriding the description configured within Eco."), Category("Server Details")]
        public string ServerDescription { get; set; }
        
        [Description("The logo of the server as a URL."), Category("Server Details")]
        public string ServerLogo { get; set; }

        private List<DiscordPlayerConfig> _playerConfigs = new List<DiscordPlayerConfig>();
        
        [Description("A mapping from user to user config parameters.")]
        public List<DiscordPlayerConfig> PlayerConfigs {
            get {
                return _playerConfigs;
            }
            set
            {
                _playerConfigs = value;
            }
        }
    }

    public class DiscordPlayerConfig
    {
        [Description("ID of the user")]
        public string Username { get; set; }

        private DiscordChannelIdentifier _defaultChannel = new DiscordChannelIdentifier();
        public DiscordChannelIdentifier DefaultChannel
        {
            get { return _defaultChannel; }
            set { _defaultChannel = value; }
        }

        public class DiscordChannelIdentifier
        {
            public string Guild { get; set; }
            public string Channel { get; set; }
        }
    }
    
}