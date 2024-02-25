using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiplomaProject
{
    internal class Functions
    {

        private SpotifyService _spotifyService;
        private MainWindow _mainWindow;
        private BotSettings _botSettings;

        public Functions(SpotifyService spotifyService, MainWindow mainWindow, BotSettings settings)
        {
            _spotifyService = spotifyService;
            _mainWindow = mainWindow;
            _botSettings = settings;
        }

        public int AddMusicToQueue(string UserInput)
        {
            try
            {
                var z = UserInput.Substring(_spotifyService.GetTrackLinkStart().Length);
                var x = z.Split('?');
                //   await spotifyClient.Player.AddToQueue(new PlayerAddToQueueRequest(trackUriStart + x[0]));
                //string track = redemption.UserInput.TrimStart("https://open.spotify.com/track/".ToCharArray());

                var track_info = _spotifyService.GetSpotifyClient().Tracks.Get(x[0]);
                _mainWindow.Log($"Playing track " + track_info.Result.Name + " by " + track_info.Result.Artists.First().Name);
                return 0;
            }
            catch (Exception ex)
            {
                _mainWindow.Log(ex.Message);
                return 1;
            }
        }

    }
}
