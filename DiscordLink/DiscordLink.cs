﻿using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Eco.Core;
using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Players;
using Eco.Plugins.DiscordLink.Modules;
using Eco.Plugins.DiscordLink.Utilities;
using Eco.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Eco.Plugins.DiscordLink
{
    public class DiscordLink : IModKitPlugin, IInitializablePlugin, IShutdownablePlugin, IConfigurablePlugin, IGameActionAware
    {
        public readonly Version PluginVersion = new Version(2, 1, 1);
        private const int FIRST_DISPLAY_UPDATE_DELAY_MS = 20000;

        private readonly List<Module> _modules = new List<Module>();
        private string _status = "No Connection Attempt Made";
        private CommandsNextExtension _commands = null;

        private Timer _discordDataMaybeAvailable = null;
        private Timer _tradePostingTimer = null;

        public event EventHandler OnClientStarted;
        public event EventHandler OnClientStopped;
        public event EventHandler OnDiscordMaybeReady;

        public static DiscordLink Obj { get { return PluginManager.GetPlugin<DiscordLink>(); } }
        public DiscordClient DiscordClient { get; private set; }
        public IPluginConfig PluginConfig { get { return DLConfig.Instance.PluginConfig; } }
        public ThreadSafeAction<object, string> ParamChanged { get; set; }

        public override string ToString()
        {
            return "DiscordLink";
        }

        public string GetStatus()
        {
            return _status;
        }

        public object GetEditObject()
        {
            return DLConfig.Data;
        }

        public void OnEditObjectChanged(object o, string param)
        {
            DLConfig.Instance.HandleConfigChanged();
        }

        public void Initialize(TimedTask timer)
        {
            DLConfig.Instance.Initialize();
            DLStorage.Instance.Read();
            Logger.Initialize();
            Logger.Info("Plugin version is " + PluginVersion);

            if (!SetUpClient())
            {
                return;
            }

            ConnectAsync().Wait();

            // Triggered on a timer that starts when the Discord client connects.
            // It is likely that the client object has fetched all the relevant data, but there are not guarantees.
            OnDiscordMaybeReady += (obj, args) =>
            {
                InitializeModules();
                UpdateModules(DLEventType.Startup, null);
            };

            // Set up callbacks
            UserManager.OnNewUserJoined.Add(user => UpdateModules(DLEventType.Join, user));
            UserManager.OnUserLoggedIn.Add(user => UpdateModules(DLEventType.Login, user));
            UserManager.OnUserLoggedOut.Add(user => UpdateModules(DLEventType.Logout, user));

            _ = EcoUser; // Create the Eco User on startup
        }

        public void Shutdown()
        {
            ShutdownModules();
            DLStorage.Instance.Write();
            Logger.Shutdown();
        }

        public void ActionPerformed(GameAction action)
        {
            switch (action)
            {
                case ChatSent chatSent:
                    OnMessageReceivedFromEco(chatSent);
                    break;
            
                case CurrencyTrade currencyTrade:
                    UpdateModules(DLEventType.Trade, currencyTrade);
                    break;

                case WorkOrderAction workOrderAction:
                    UpdateModules(DLEventType.WorkOrderCreated, workOrderAction);
                    break;
            
                case PostedWorkParty postedWorkParty:
                    UpdateModules(DLEventType.PostedWorkParty, postedWorkParty);
                    break;
            
                case CompletedWorkParty completedWorkParty:
                    UpdateModules(DLEventType.CompletedWorkParty, completedWorkParty);
                    break;
            
                case JoinedWorkParty joinedWorkParty:
                    UpdateModules(DLEventType.JoinedWorkParty, joinedWorkParty);
                    break;
            
                case LeftWorkParty leftWorkParty:
                    UpdateModules(DLEventType.LeftWorkParty, leftWorkParty);
                    break;
            
                case WorkedForWorkParty workedParty:
                    UpdateModules(DLEventType.WorkedWorkParty, workedParty);
                    break;

                case Vote vote:
                    UpdateModules(DLEventType.Vote, vote);
                    break;

                case StartElection startElection:
                    UpdateModules(DLEventType.StartElection, startElection);
                    break;

                case LostElection lostElection:
                    UpdateModules(DLEventType.StopElection, lostElection);
                    break;

                case WonElection wonElection:
                    UpdateModules(DLEventType.StopElection, wonElection);
                    break;
                    break;

                default:
                    break;
            }
        }

        public Result ShouldOverrideAuth(GameAction action)
        {
            return new Result(ResultType.None);
        }

        #region DiscordClient Management

        private bool SetUpClient()
        {
            _status = "Setting up client";

            bool BotTokenIsNull = string.IsNullOrWhiteSpace(DLConfig.Data.BotToken);
            if (BotTokenIsNull)
            {
                DLConfig.Instance.VerifyConfig(DLConfig.VerificationFlags.Static); // Make the user aware of the empty bot token
            }

            if (BotTokenIsNull) return false; // Do not attempt to initialize if the bot token is empty

            try
            {
                // Create the new client
                DiscordClient = new DiscordClient(new DiscordConfiguration
                {
                    AutoReconnect = true,
                    Token = DLConfig.Data.BotToken,
                    TokenType = TokenType.Bot,
                    MinimumLogLevel = DLConfig.Data.BackendLogLevel
                });

                DiscordClient.ClientErrored += async (client, args) => { Logger.Debug("A Discord client error occurred. Error messages was: " + args.EventName + " " + args.Exception.ToString()); };
                DiscordClient.SocketErrored += async (client, args) => { Logger.Debug("A socket error occurred. Error message was: " + args.Exception.ToString()); };
                DiscordClient.SocketClosed += async (client, args) => { Logger.DebugVerbose("Socket Closed: " + args.CloseMessage + " " + args.CloseCode); };
                DiscordClient.Resumed += async (client, args) => { Logger.Debug("Resumed connection"); };
                DiscordClient.Ready += async (client, args) =>
                {
                    DLConfig.Instance.EnqueueFullVerification();

                    _discordDataMaybeAvailable = new Timer(innerArgs =>
                    {
                        OnDiscordMaybeReady?.Invoke(this, EventArgs.Empty);
                        SystemUtil.StopAndDestroyTimer(ref _discordDataMaybeAvailable);
                    }, null, FIRST_DISPLAY_UPDATE_DELAY_MS, Timeout.Infinite);
                };

                DiscordClient.GuildAvailable += async (client, args) =>
                {
                    DLConfig.Instance.EnqueueGuildVerification();
                };

                DiscordClient.MessageDeleted += async (client, args) =>
                {
                    _modules.ForEach(async module => await module.OnMessageDeleted(args.Message));
                };

                // Set up the client to use CommandsNext
                _commands = DiscordClient.UseCommandsNext(new CommandsNextConfiguration
                {
                    StringPrefixes = DLConfig.Data.DiscordCommandPrefix.SingleItemAsEnumerable()
                });
                _commands.RegisterCommands<DiscordCommands>();

                OnClientStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception e)
            {
                Logger.Error("Error occurred while creating the Discord client. Error message: " + e);
            }

            return false;
        }

        void StopClient()
        {
            // Stop various timers that may have been set up so they do not trigger while the reset is ongoing
            DLConfig.Instance.DequeueAllVerification();
            SystemUtil.StopAndDestroyTimer(ref _discordDataMaybeAvailable);
            SystemUtil.StopAndDestroyTimer(ref _tradePostingTimer);

            ShutdownModules();

            if (DiscordClient != null)
            {
                StopRelaying();

                // If DisconnectAsync() is called in the GUI thread, it will cause a deadlock
                SystemUtil.SynchronousThreadExecute(() =>
                {
                    DiscordClient.DisconnectAsync().Wait();
                });
                DiscordClient.Dispose();
                DiscordClient = null;

                OnClientStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task<bool> RestartClient()
        {
            StopClient();
            bool result = SetUpClient();
            if (result)
            {
                await ConnectAsync();
            }
            return result;
        }

        public async Task<object> ConnectAsync()
        {
            try
            {
                _status = "Attempting connection...";
                await DiscordClient.ConnectAsync();
                BeginRelaying();
                Logger.Info("Connected to Discord");
                _status = "Connection successful";
            }
            catch (Exception e)
            {
                Logger.Error("Error occurred when connecting to Discord: Error message: " + e.Message);
                _status = "Connection failed";
            }

            return null;
        }

        public async Task<object> DisconnectAsync()
        {
            try
            {
                StopRelaying();
                await DiscordClient.DisconnectAsync();
            }
            catch (Exception e)
            {
                Logger.Error("An Error occurred when disconnecting from Discord: Error message: " + e.Message);
                _status = "Connection failed";
            }

            return null;
        }

        #endregion

        #region Module Management

        private void InitializeModules()
        {
            _modules.Add(new DiscordChatFeed());   // Discord -> Eco
            _modules.Add(new EcoChatFeed());       // Eco -> Discord
            _modules.Add(new ChatlogFeed());
            _modules.Add(new ServerInfoDisplay());
            _modules.Add(new TradeFeed());
            _modules.Add(new CraftingFeed());
            _modules.Add(new SnippetInput());
            _modules.Add(new WorkPartyDisplay());
            _modules.Add(new PlayerDisplay());
            _modules.Add(new ElectionDisplay());
            _modules.Add(new CurrencyDisplay());

            _modules.ForEach(async module => await module.StartIfRelevant());
        }

        private void ShutdownModules()
        {
            _modules.ForEach(async module => await module.Stop());
            _modules.Clear();
        }

        private void UpdateModules(DLEventType trigger, object data)
        {
            _modules.ForEach(async module => await module.Update(this, trigger, data));
        }

        #endregion

        #region Discord Guild Access

        public string[] GuildNames => DiscordClient.GuildNames();
        public DiscordGuild DefaultGuild => DiscordClient.DefaultGuild();

        public DiscordGuild GuildByName(string name)
        {
            return DiscordClient?.Guilds.Values.FirstOrDefault(guild => guild.Name?.ToLower() == name.ToLower());
        }

        public DiscordGuild GuildByNameOrId(string nameOrId)
        {
            var maybeGuildId = DSharpExtensions.TryParseSnowflakeId(nameOrId);
            return maybeGuildId != null ? DiscordClient.Guilds[maybeGuildId.Value] : GuildByName(nameOrId);
        }

        #endregion

        #region Message Sending

        public async Task<string> SendDiscordMessage(string message, string channelNameOrId, string guildNameOrId)
        {
            if (DiscordClient == null) return "No discord client";

            var guild = GuildByNameOrId(guildNameOrId);
            if (guild == null) return "No guild of that name found";

            var channel = guild.ChannelByNameOrId(channelNameOrId);
            await DiscordUtil.SendAsync(channel, message);
            return "Message sent";
        }

        public async Task<string> SendDiscordMessageAsUser(string message, User user, string channelNameOrId, string guildNameOrId, bool allowGlobalMentions = false)
        {
            var guild = GuildByNameOrId(guildNameOrId);
            if (guild == null) return "No guild of that name found";

            var channel = guild.ChannelByNameOrId(channelNameOrId);
            if (channel == null) return "No channel of that name or ID found in that guild";
            await DiscordUtil.SendAsync(channel, MessageUtil.FormatMessageForDiscord(message, channel, user.Name, allowGlobalMentions));
            return "Message sent";
        }

        public async Task<string> SendDiscordMessageAsUser(string message, User user, DiscordChannel channel, bool allowGlobalMentions = false)
        {
            await DiscordUtil.SendAsync(channel, MessageUtil.FormatMessageForDiscord(message, channel, user.Name, allowGlobalMentions));
            return "Message sent";
        }

        #endregion

        #region Message Relaying

        private const string EcoUserSteamId = "DiscordLinkSteam";
        private const string EcoUserSlgId = "DiscordLinkSlg";

        private User _ecoUser;
        public User EcoUser => _ecoUser ??= UserManager.GetOrCreateUser(EcoUserSteamId, EcoUserSlgId, !string.IsNullOrWhiteSpace(DLConfig.Data.EcoBotName) ? DLConfig.Data.EcoBotName : DLConfig.DefaultValues.EcoBotName);

        private void BeginRelaying()
        {
            ActionUtil.AddListener(this);
            DiscordClient.MessageCreated += OnDiscordMessageCreateEvent;
        }

        private void StopRelaying()
        {
            ActionUtil.RemoveListener(this);
            DiscordClient.MessageCreated -= OnDiscordMessageCreateEvent;
        }

        public ChatChannelLink GetLinkForEcoChannel(string discordChannelNameOrId)
        {
            return DLConfig.Data.ChatChannelLinks.FirstOrDefault(link => link.DiscordChannel == discordChannelNameOrId);
        }

        public ChatChannelLink GetLinkForDiscordChannel(string ecoChannelName)
        {
            var lowercaseEcoChannelName = ecoChannelName.ToLower();
            return DLConfig.Data.ChatChannelLinks.FirstOrDefault(link => link.EcoChannel.ToLower() == lowercaseEcoChannelName);
        }

        public void LogEcoMessage(ChatSent chatMessage)
        {
            Logger.DebugVerbose("Eco Message Processed:");
            Logger.DebugVerbose("Message: " + chatMessage.Message);
            Logger.DebugVerbose("Tag: " + chatMessage.Tag);
            Logger.DebugVerbose("Sender: " + chatMessage.Citizen);
        }

        public void LogDiscordMessage(DiscordMessage message)
        {
            Logger.DebugVerbose("Discord Message Processed");
            Logger.DebugVerbose("Message: " + message.Content);
            Logger.DebugVerbose("Channel: " + message.Channel.Name);
            Logger.DebugVerbose("Sender: " + message.Author);
        }

        public void OnMessageReceivedFromEco(ChatSent chatMessage)
        {
            LogEcoMessage(chatMessage);

            // Ignore commands and messages sent by our bot
            if (chatMessage.Citizen.Name == EcoUser.Name && !chatMessage.Message.StartsWith(DLConstants.ECHO_COMMAND_TOKEN))
                return;

            UpdateModules(DLEventType.EcoMessage, chatMessage);
        }

        public async Task OnDiscordMessageCreateEvent(DiscordClient client, MessageCreateEventArgs messageArgs)
        {
            OnMessageReceivedFromDiscord(messageArgs.Message);
        }

        public void OnMessageReceivedFromDiscord(DiscordMessage message)
        {
            LogDiscordMessage(message);

            // Ignore commands and messages sent by our bot
            if (message.Author == DiscordClient.CurrentUser) return;
            if (message.Content.StartsWith(DLConfig.Data.DiscordCommandPrefix)) return;

            UpdateModules(DLEventType.DiscordMessage, message);
        }
        #endregion
    }
}
