using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiplomaProject
{
    internal class SpotifyService
    {
        private MainWindow _mainWindow;

        //Spotify Auth
        private string redirectURI;
        private readonly string SpotifyCLID = Properties.Settings.Default.SpotifyCLID;
        private readonly string SpotfyCLSCR = Properties.Settings.Default.SpotifyCLSCR;
        EmbedIOAuthServer _server;

        //Spotify API
        private SpotifyClient _spotifyClient;
        private readonly string trackLinkStart = "https://open.spotify.com/track/";
        private readonly string trackUriStart = "spotify:track:";

        public SpotifyService(MainWindow mainWindow, string redirectURI)
        {
            try
            {
                _mainWindow = mainWindow;
                this.redirectURI = redirectURI;
                initializeWebServer();

                var request = new LoginRequest(_server.BaseUri, SpotifyCLID, LoginRequest.ResponseType.Code)
                {
                    Scope = new List<string> { Scopes.AppRemoteControl, Scopes.UserReadEmail, Scopes.UserModifyPlaybackState, Scopes.UserReadPlaybackState }
                };
                BrowserUtil.Open(request.ToUri());
            }
            catch (Exception ex) { _mainWindow.Log(ex.Message); }
        }
        private async void initializeWebServer()
        {
            //Local web server to genereate OAuth token
            try
            {
                _server = new EmbedIOAuthServer(new Uri(redirectURI + ":5543/callback"), 5543);
                await _server.Start();
                _server.AuthorizationCodeReceived += OnAuthorizationCodeReceived;
                _server.ErrorReceived += OnErrorReceived;
            }
            catch (Exception ex) { _mainWindow.Log(ex.Message); }

        }

        private async Task OnErrorReceived(object arg1, string error, string state)
        {
            Console.WriteLine($"Aborting authorization, error received: {error}");
            await _server.Stop();
        }

        private async Task OnAuthorizationCodeReceived(object arg1, AuthorizationCodeResponse response)
        {
            try
            {
                await _server.Stop();

                var config = SpotifyClientConfig.CreateDefault();
                var tokenResponse = await new OAuthClient(config).RequestToken(
                    new AuthorizationCodeTokenRequest(
                        SpotifyCLID, SpotfyCLSCR, response.Code,
                        new Uri("http://localhost:5543/callback")
                        )
                    );

                _spotifyClient = new SpotifyClient(tokenResponse.AccessToken);
            }
            catch (Exception ex) { _mainWindow.Log(ex.Message); }
        }
        public string GetTrackLinkStart() { return trackLinkStart; }
        public string GetTracUriStart() { return trackUriStart; }
        public SpotifyClient GetSpotifyClient() { return _spotifyClient; }
    }
}
