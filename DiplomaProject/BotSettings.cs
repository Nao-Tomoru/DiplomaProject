using EmbedIO.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwitchLib.Api.Helix.Models.Moderation.AutomodSettings;

namespace DiplomaProject
{
    internal class BotSettings
    {
        public string CommandName { get; set; }
        public uint CommandCooldown { get; set; }

        public bool isSubOnly { get; set; }
        public bool isCommandEnabled { get; set; }

        public BotSettings()
        {
            SetDefault();
        }

        public BotSettings(string settingsPath)
        {
            StreamReader streamReader = new StreamReader(settingsPath);
            string jsonString = streamReader.ReadToEnd();
            streamReader.Close();

            if (jsonString.Trim().NullIfEmpty() != null)
            {
                BotSettings? botSettings = JsonSerializer.Deserialize<BotSettings>(jsonString);

                if (botSettings != null)
                {
                    CommandName = botSettings.CommandName;
                    CommandCooldown = botSettings.CommandCooldown;
                    isCommandEnabled = botSettings.isCommandEnabled;
                    isSubOnly = botSettings.isSubOnly;
                }
                else
                {
                    SetDefault();
                }
            }
            else
            {
                SetDefault();
            }
        }
        private void SetDefault()
        {
            CommandName = "addSpotify";
            CommandCooldown = 30;
            isSubOnly = false;
            isCommandEnabled = false;

        }
        public async Task UpdateSettingsAsync(string name, uint cooldown)
        {
            CommandName = name;
            CommandCooldown = cooldown;

            string jsonString = JsonSerializer.Serialize(this);
            StreamWriter streamWriter = new("Settings.json", false)
            {
                AutoFlush = true
            };
            await streamWriter.WriteAsync(jsonString);
            streamWriter.Close();
        }
    }
}
