using Newtonsoft.Json.Linq;
using NHttp;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Windows;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using System.Diagnostics;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using System.Text.RegularExpressions;
using System.Text.Json;
using EmbedIO.Utilities;
using Swan;


namespace DiplomaProject
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        DateTime lastCommandTime;
        BotSettings BotSettings;

        private string addToQueueCommand;
        private ushort commandCooldown;

        //Spotify Auth
        private readonly string trackLinkStart = "https://open.spotify.com/track/";
        private readonly string trackUriStart = "spotify:track:";
        private readonly string SpotifyCLID = Properties.Settings.Default.SpotifyCLID;
        private readonly string SpotfyCLSCR = Properties.Settings.Default.SpotifyCLSCR;
        private SpotifyClient spotifyClient;
        EmbedIOAuthServer _server;

        // Twitch Auth
        private HttpServer WebServer;
        private readonly string RedirectUri = "http://localhost";
        private readonly string ClientId = Properties.Settings.Default.ClientId;
        private readonly string ClientSecret = Properties.Settings.Default.ClientSecret;
        private readonly List<string> TwitchScopes = new List<string> { "channel:read:redemptions", "channel:manage:redemptions", "chat:edit", "chat:read", "channel:read:subscriptions" };

        //TwitchLib
        private TwitchClient OwnerOfChannelConnection;
        private TwitchAPI twitchAPI;
        private TwitchPubSub pubSubClient;

        //Cache
        private string CachedOwnerOfChannelAccessToken = "Something went wrong";  //for API requests
        private string TwitchChannelId;       //Needed to join chat 
        private string TwitchChannelName;    //Needed for Calls like get subscribers, etc...

        public MainWindow()
        {
            lastCommandTime = DateTime.UnixEpoch;
            if (File.Exists("Settings.json"))
                BotSettings = new BotSettings("Settings.json");
            else
                BotSettings = new BotSettings();
            InitializeComponent();
            commandNameTextBox.Text = BotSettings.CommandName;
            commandCooldownTextbox.Text = Convert.ToString(BotSettings.CommandCooldown);
            commandUseCheckBox.IsChecked = BotSettings.isCommandEnabled;
            subCheckCheckBox.IsChecked = BotSettings.isSubOnly;
        }

        private async void startBotButton_Click(object sender, RoutedEventArgs e)
        {

            startBotButton.IsEnabled = false;
            startBotButton.Visibility = Visibility.Hidden;
            updateButton.IsEnabled = true;
            updateButton.Visibility = Visibility.Visible;
            updateButton_Click(sender, e);
            initializeWebServer();
            var authUrl = $"https://id.twitch.tv/oauth2/authorize?response_type=code&client_id={ClientId}&redirect_uri={RedirectUri}&scope={string.Join("+", TwitchScopes)}";
            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });


            var request = new LoginRequest(_server.BaseUri, SpotifyCLID, LoginRequest.ResponseType.Code)
            {
                Scope = new List<string> { Scopes.AppRemoteControl, Scopes.UserReadEmail, Scopes.UserModifyPlaybackState, Scopes.UserReadPlaybackState }
            };
            BrowserUtil.Open(request.ToUri());


        }
        private async void initializeWebServer()
        {
            //Local web server to genereate OAuth token


            _server = new EmbedIOAuthServer(new Uri(RedirectUri + ":5543/callback"), 5543);
            await _server.Start();
            _server.AuthorizationCodeReceived += OnAuthorizationCodeReceived;
            _server.ErrorReceived += OnErrorReceived;
            try
            {
                WebServer = new HttpServer();
                WebServer.EndPoint = new IPEndPoint(IPAddress.Loopback, 80);
            }
            catch (Exception ex) { Log(ex.Message); }

            //Callback
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
                catch (Exception ex) { Log(ex.Message); }
            };
            if (WebServer.State != HttpServerState.Started)
            {
                WebServer.Start();
                Log($"Web server started on: {WebServer.EndPoint} ");
                Console.WriteLine($"Web server started on: {WebServer.EndPoint} ");
            }

        }

        private async Task OnErrorReceived(object arg1, string error, string state)
        {
            Console.WriteLine($"Aborting authorization, error received: {error}");
            await _server.Stop();
        }

        private async Task OnAuthorizationCodeReceived(object arg1, AuthorizationCodeResponse response)
        {
            await _server.Stop();

            var config = SpotifyClientConfig.CreateDefault();
            var tokenResponse = await new OAuthClient(config).RequestToken(
                new AuthorizationCodeTokenRequest(
                    SpotifyCLID, SpotfyCLSCR, response.Code,
                    new Uri("http://localhost:5543/callback")
                    )
                );

            spotifyClient = new SpotifyClient(tokenResponse.AccessToken);
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
                {"redirect_uri", RedirectUri },
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
                Log(ex.Message);
            }

        }

        void InitializeOwnerOfChannelConnection(string username, string accessToken)
        {
            try
            {
                OwnerOfChannelConnection = new TwitchClient();
                //Event
                var creds = new ConnectionCredentials(username, accessToken);
                OwnerOfChannelConnection.Initialize(creds, TwitchChannelName);
                OwnerOfChannelConnection.OnConnected += Client_OnConnected;
                OwnerOfChannelConnection.OnDisconnected += Client_OnDisconnected;
                OwnerOfChannelConnection.OnChatCommandReceived += Client_OnChatCommandReceived;

                OwnerOfChannelConnection.Connect();
                //PubSub Events
                pubSubClient = new TwitchPubSub();
                pubSubClient.OnListenResponse += PubSubClient_OnListenResponse;
                pubSubClient.OnPubSubServiceConnected += PubSubClient_OnPubSubServiceConnected;
                ListenToRewards(TwitchChannelId);
                pubSubClient.Connect();
            }
            catch (Exception ex) { Log(ex.Message); }


        }

        private void PubSubClient_OnPubSubServiceConnected(object? sender, EventArgs e)
        {

            Log($"Connected to pubsub server");

            pubSubClient.SendTopics(CachedOwnerOfChannelAccessToken);
            var response = twitchAPI.Helix.ChannelPoints.GetCustomRewardAsync(TwitchChannelId);
            bool exists = false;
            foreach (var item in response.Result.Data)
            {
                if (item.Title == "Add to Spotify Queue")
                {
                    exists = true; break;
                }
            }
            if (!exists)
                twitchAPI.Helix.ChannelPoints.CreateCustomRewardsAsync(TwitchChannelId, new TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward.CreateCustomRewardsRequest() { Title = "Add to Spotify Queue", Cost = 1, IsUserInputRequired = true, IsEnabled = true, Prompt = "Input Spotify track link" });
        }

        private void PubSubClient_OnListenResponse(object? sender, OnListenResponseArgs e)
        {
            if (!e.Successful)
            {
                Log($"Failed to listen! Response{e.Response}");
            }
        }

        private void ListenToRewards(string channelId)
        {
            pubSubClient.ListenToRewards(channelId);
            pubSubClient.ListenToChannelPoints(channelId);
            pubSubClient.OnChannelPointsRewardRedeemed += Client_onPubSubChannelPointsRewardRedeemed;
        }

        private async void Client_onPubSubChannelPointsRewardRedeemed(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            bool isASub = false;
            var subsrcribers = twitchAPI.Helix.Subscriptions.GetBroadcasterSubscriptionsAsync(TwitchChannelId).Result;
            for (int i = 0; i < subsrcribers.Data.Length; i++)
            {
                if (subsrcribers.Data[i].UserId == e.RewardRedeemed.Redemption.User.Id)
                {
                    isASub = true; break;
                }
            }
            if (BotSettings.isSubOnly && isASub)
            {
                var redemption = e.RewardRedeemed.Redemption;
                var reward = e.RewardRedeemed.Redemption.Reward;
                if (reward.Title.ToLower() == "add to spotify queue")
                {

                    var result = AddMusicToQueue(redemption.UserInput);

                    if (result == 1)
                    {
                        await twitchAPI.Helix.ChannelPoints.UpdateRedemptionStatusAsync(e.ChannelId, reward.Id, new List<string>() { e.RewardRedeemed.Redemption.Id }, new UpdateCustomRewardRedemptionStatusRequest() { Status = CustomRewardRedemptionStatus.CANCELED }, CachedOwnerOfChannelAccessToken);
                        OwnerOfChannelConnection.SendMessage(TwitchChannelName, "Input was not a link or was incorrect");
                        Log($"Incorrect input");
                    }

                }
            }
        }

        private void Client_OnChatCommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            var timeSend = e.Command.ChatMessage.TmiSentTs;
            var timeSendInt = Convert.ToInt64(timeSend);
            if (BotSettings.isCommandEnabled)
            {

                if (e.Command.CommandText.ToLower() == addToQueueCommand.ToLower())
                {
                    var x = lastCommandTime.ToUnixEpochDate() * 1000;
                    if (timeSendInt - x < BotSettings.CommandCooldown * 1000)
                    {
                        if (BotSettings.isSubOnly)
                        {
                            Log("Cooldown remaining: " + (15 - (timeSendInt - lastCommandTime.ToUnixEpochDate() * 1000) / 1000));
                            return;
                        }
                        if (!e.Command.ChatMessage.IsSubscriber)
                        {
                            OwnerOfChannelConnection.SendMessage(TwitchChannelName, e.Command.ChatMessage.DisplayName + "is not subscriber");
                            return;
                        }
                    }
                    AddMusicToQueue(e.Command.ArgumentsAsList.First());
                    lastCommandTime = DateTime.Now;
                }
            }
        }

        void InitializeTwitchAPI(string accessToken)
        {
            twitchAPI = new TwitchAPI();
            twitchAPI.Settings.ClientId = ClientId;
            twitchAPI.Settings.AccessToken = accessToken;
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Log($"User {e.BotUsername} connected");
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            Log($"Bot  disconnected");
        }

        private void Log(string logs)
        {
            this.Dispatcher.Invoke(() =>
            {
                logTextBox.Text += (logs + "\n");
            });
            Console.WriteLine(logs);
        }

        private void MainWindowClosting(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Ending the connection
            if (OwnerOfChannelConnection != null)
            {
                OwnerOfChannelConnection.Disconnect();
            }
            //Disposing from web Server
            if (WebServer != null)
            {
                WebServer.Stop();
                WebServer.Dispose();
            }
        }

        private int AddMusicToQueue(string UserInput)
        {
            try
            {
                var z = UserInput.Substring(trackLinkStart.Length);
                var x = z.Split('?');
                //   await spotifyClient.Player.AddToQueue(new PlayerAddToQueueRequest(trackUriStart + x[0]));
                //string track = redemption.UserInput.TrimStart("https://open.spotify.com/track/".ToCharArray());

                var track_info = spotifyClient.Tracks.Get(x[0]);
                Log($"Playing track " + track_info.Result.Name + " by " + track_info.Result.Artists.First().Name);
                return 0;
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                return 1;
            }
        }

        private void commandCooldownTextbox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private async void updateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {


                addToQueueCommand = commandNameTextBox.Text;
                commandCooldown = Convert.ToUInt16(commandCooldownTextbox.Text);
                bool isSubOnly, isCommandEnabled;
                if (subCheckCheckBox.IsChecked == true) isSubOnly = true; else isSubOnly = false;
                if (commandUseCheckBox.IsChecked == true) isCommandEnabled = true; else isCommandEnabled = false;
                BotSettings.UpdateSettingsAsync(addToQueueCommand, commandCooldown, isSubOnly, isCommandEnabled);


            }
            catch (Exception ex) { Log(ex.Message); }

        }
    }
}