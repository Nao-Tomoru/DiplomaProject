using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiplomaProject
{
    internal class Functions(SpotifyService spotifyService, MainWindow mainWindow, BotSettings settings)
    {

        private readonly SpotifyService _spotifyService = spotifyService;
        private readonly MainWindow _mainWindow = mainWindow;
        private readonly BotSettings _botSettings = settings;

        public int AddMusicToQueue(string UserInput)
        {
            try
            {
                if (UserInput.Length > _spotifyService.GetTrackLinkStart().Length)
                {
                    var z = UserInput.Substring(_spotifyService.GetTrackLinkStart().Length);
                    var x = z.Split('?');
                    //   await spotifyClient.Player.AddToQueue(new PlayerAddToQueueRequest(trackUriStart + x[0]));
                    //string track = redemption.UserInput.TrimStart("https://open.spotify.com/track/".ToCharArray());

                    var track_info = _spotifyService.GetSpotifyClient().Tracks.Get(x[0]);
                    _mainWindow.Log($"Playing track " + track_info.Result.Name + " by " + track_info.Result.Artists.First().Name);
                    return 0;
                }
                else { throw new Exception("Incorrect input"); }
            }
            catch (Exception ex)
            {
                _mainWindow.Log(ex.Message);
                return 1;
            }
        }

    }
}
