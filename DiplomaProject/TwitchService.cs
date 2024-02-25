using Newtonsoft.Json.Linq;
using NHttp;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using Swan;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Api.Interfaces;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;

namespace DiplomaProject
{
    internal class TwitchService
    {
        DateTime _lastCommandTime = DateTime.MinValue;
        Functions _functions;

        //TwitchLib
        private TwitchClient _ownerOfChannelConnection;
        private TwitchAPI _twitchAPI;
        private TwitchPubSub _pubSubClient;
        // Twitch Auth
        private MainWindow _mainWindow;
        private BotSettings _botSettings;
        private string redirectURI;
        private readonly string ClientId = Properties.Settings.Default.ClientId;
        private readonly string ClientSecret = Properties.Settings.Default.ClientSecret;
        private readonly List<string> TwitchScopes = new List<string> { "channel:read:redemptions", "channel:manage:redemptions", "chat:edit", "chat:read", "channel:read:subscriptions" };
        private HttpServer WebServer;
        private EmbedIOAuthServer _server;
        //Cache
        private string CachedOwnerOfChannelAccessToken = "Something went wrong";  //for API requests
        private string TwitchChannelId;       //Needed to join chat 
        private string TwitchChannelName;    //Needed for Calls like get subscribers, etc...

        public TwitchService(MainWindow mainWindow, BotSettings settings, Functions functions, string redirectURI)
        {
            _mainWindow = mainWindow;
            _botSettings = settings;
            _functions = functions;
            this.redirectURI = redirectURI;
            InitializeWebServer();

            var authUrl = $"https://id.twitch.tv/oauth2/authorize?response_type=code&client_id={ClientId}&redirect_uri={redirectURI}&scope={string.Join("+", TwitchScopes)}";
            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });

        }

        public TwitchClient GetOwnerOfChannel()
        {
            return _ownerOfChannelConnection;
        }
        public HttpServer GetServer() { return WebServer; }

        private void InitializeWebServer()
        {

            try
            {
                WebServer = new HttpServer();
                WebServer.EndPoint = new IPEndPoint(IPAddress.Loopback, 80);
            }
            catch (Exception ex) { _mainWindow.Log(ex.Message); }

            WebServer.RequestReceived += async (s, e) =>
            {
                try
                {
                    using (var writer = new StreamWriter(e.Response.OutputStream))
                    {
                        if (e.Request.QueryString.AllKeys.Any("code".Contains))
                        {
                            var code = e.Request.QueryString["code"];
                            var ownerOfChannelAccessAndRefresh = await getAccessAndRefreshTokens(code);
                            CachedOwnerOfChannelAccessToken = ownerOfChannelAccessAndRefresh.Item1;
                            SetNameAndIdByOauthedUser(CachedOwnerOfChannelAccessToken).Wait();
                            InitializeOwnerOfChannelConnection(TwitchChannelName, CachedOwnerOfChannelAccessToken);
                            InitializeTwitchAPI(CachedOwnerOfChannelAccessToken);
                        }

                    }
                }
                catch (Exception ex) { _mainWindow.Log(ex.Message); }
            };

            if (WebServer.State != HttpServerState.Started)
            {
                WebServer.Start();
                _mainWindow.Log($"Web server started on: {WebServer.EndPoint} ");
                Console.WriteLine($"Web server started on: {WebServer.EndPoint} ");
            }
        }



        async Task<Tuple<string, string>> getAccessAndRefreshTokens(string code)
        {
            HttpClient client = new HttpClient();
            var values = new Dictionary<string, string>
            {
                {"client_id", ClientId },
                {"client_secret", ClientSecret },
                {"code", code},
                {"grant_type","authorization_code" },
                {"redirect_uri", redirectURI },
            };

            var content = new FormUrlEncodedContent(values);

            var response = await client.PostAsync("https://id.twitch.tv/oauth2/token", content);

            var resposnseString = await response.Content.ReadAsStringAsync();

            var json = JObject.Parse(resposnseString);
            return new Tuple<string, string>(json["access_token"].ToString(), json["refresh_token"].ToString());
        }
        async Task SetNameAndIdByOauthedUser(string accessToken)
        {
            var api = new TwitchLib.Api.TwitchAPI();
            api.Settings.ClientId = ClientId;
            api.Settings.AccessToken = accessToken;
            try
            {
                var oauthedUser = await api.Helix.Users.GetUsersAsync();
                TwitchChannelId = oauthedUser.Users[0].Id;
                TwitchChannelName = oauthedUser.Users[0].Login;
            }
            catch (Exception ex)
            {
                _mainWindow.Log(ex.Message);
            }

        }
        void InitializeOwnerOfChannelConnection(string username, string accessToken)
        {
            try
            {
                _ownerOfChannelConnection = new TwitchClient();
                //Event
                var creds = new ConnectionCredentials(username, accessToken);
                _ownerOfChannelConnection.Initialize(creds, TwitchChannelName);
                _ownerOfChannelConnection.OnConnected += Client_OnConnected;
                _ownerOfChannelConnection.OnDisconnected += Client_OnDisconnected;
                _ownerOfChannelConnection.OnChatCommandReceived += Client_OnChatCommandReceived;

                _ownerOfChannelConnection.Connect();
                //PubSub Events
                _pubSubClient = new TwitchPubSub();
                _pubSubClient.OnListenResponse += PubSubClient_OnListenResponse;
                _pubSubClient.OnPubSubServiceConnected += PubSubClient_OnPubSubServiceConnected;
                ListenToRewards(TwitchChannelId);
                _pubSubClient.Connect();
            }
            catch (Exception ex)
            {
                _mainWindow.Log(ex.Message);

            }


        }
        void InitializeTwitchAPI(string accessToken)
        {
            _twitchAPI = new TwitchAPI();
            _twitchAPI.Settings.ClientId = ClientId;
            _twitchAPI.Settings.AccessToken = accessToken;
        }
        private void ListenToRewards(string channelId)
        {
            _pubSubClient.ListenToRewards(channelId);
            _pubSubClient.ListenToChannelPoints(channelId);
            _pubSubClient.OnChannelPointsRewardRedeemed += Client_onPubSubChannelPointsRewardRedeemed;
        }

        private void PubSubClient_OnPubSubServiceConnected(object? sender, EventArgs e)
        {

            _mainWindow.Log($"Connected to pubsub server");

            _pubSubClient.SendTopics(CachedOwnerOfChannelAccessToken);
            var response = _twitchAPI.Helix.ChannelPoints.GetCustomRewardAsync(TwitchChannelId);
            bool exists = false;
            foreach (var item in response.Result.Data)
            {
                if (item.Title == "Add to Spotify Queue")
                {
                    exists = true; break;
                }
            }
            if (!exists)
                _twitchAPI.Helix.ChannelPoints.CreateCustomRewardsAsync(TwitchChannelId, new TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward.CreateCustomRewardsRequest() { Title = "Add to Spotify Queue", Cost = 1, IsUserInputRequired = true, IsEnabled = true, Prompt = "Input Spotify track link" });
        }
        private void PubSubClient_OnListenResponse(object? sender, OnListenResponseArgs e)
        {
            if (!e.Successful)
            {
                _mainWindow.Log($"Failed to listen! Response{e.Response}");
            }
        }

        private async void Client_onPubSubChannelPointsRewardRedeemed(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            bool isASub = false;
            var subsrcribers = _twitchAPI.Helix.Subscriptions.GetBroadcasterSubscriptionsAsync(TwitchChannelId).Result;
            for (int i = 0; i < subsrcribers.Data.Length; i++)
            {
                if (subsrcribers.Data[i].UserId == e.RewardRedeemed.Redemption.User.Id)
                {
                    isASub = true; break;
                }
            }
            if (_botSettings.isSubOnly && isASub)
            {
                var redemption = e.RewardRedeemed.Redemption;
                var reward = e.RewardRedeemed.Redemption.Reward;
                if (reward.Title.ToLower() == "add to spotify queue")
                {

                    var result = _functions.AddMusicToQueue(redemption.UserInput);

                    if (result == 1)
                    {
                        await _twitchAPI.Helix.ChannelPoints.UpdateRedemptionStatusAsync(e.ChannelId, reward.Id, new List<string>() { e.RewardRedeemed.Redemption.Id }, new UpdateCustomRewardRedemptionStatusRequest() { Status = CustomRewardRedemptionStatus.CANCELED }, CachedOwnerOfChannelAccessToken);
                        _ownerOfChannelConnection.SendMessage(TwitchChannelName, "Input was not a link or was incorrect");
                        _mainWindow.Log($"Incorrect input");
                    }

                }
            }
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            _mainWindow.Log($"User {e.BotUsername} connected");
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            _mainWindow.Log($"Bot  disconnected");
        }

        private void Client_OnChatCommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            //string musicCommandName = _botSettings.CommandName;
            //switch () {
            //    case 1:
            //        break;
            //    default:
            //        break;
            //}

            if (_botSettings.isCommandEnabled)
            {
                if (e.Command.CommandText.ToLower() == _botSettings.CommandName.ToLower())
                {
                    var timeSend = e.Command.ChatMessage.TmiSentTs;
                    var timeSendInt = Convert.ToInt64(timeSend);

                    var x = _lastCommandTime.ToUnixEpochDate() * 1000;
                    if (timeSendInt - x < _botSettings.CommandCooldown * 1000)
                    {
                        if (_botSettings.isSubOnly)
                        {
                            _mainWindow.Log("Cooldown remaining: " + (15 - (timeSendInt - _lastCommandTime.ToUnixEpochDate() * 1000) / 1000));
                            return;
                        }
                        if (!e.Command.ChatMessage.IsSubscriber)
                        {
                            _ownerOfChannelConnection.SendMessage(TwitchChannelName, e.Command.ChatMessage.DisplayName + "is not subscriber");
                            return;
                        }
                    }
                    _functions.AddMusicToQueue(e.Command.ArgumentsAsList.First());
                    _lastCommandTime = DateTime.Now;
                }
            }
        }


















    }

}




