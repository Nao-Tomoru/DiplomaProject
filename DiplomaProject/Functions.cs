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

        public async Task<int> AddMusicToQueueAsync(string UserInput)
        {
            try
            {
                if (UserInput.Length > _spotifyService.GetTrackLinkStart().Length)
                {
                    var x = UserInput.Split('?');
                    var z = x[0].Substring(_spotifyService.GetTrackLinkStart().Length);
                    await _spotifyService.GetSpotifyClient().Player.AddToQueue(new PlayerAddToQueueRequest(_spotifyService.GetTracUriStart() + z));
                    var track_info = _spotifyService.GetSpotifyClient().Tracks.Get(z);
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
