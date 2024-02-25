using System.IO;
using System.Windows;
using System.Text.RegularExpressions;


namespace DiplomaProject
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        BotSettings _botSettings;

        TwitchService _twitchService;
        SpotifyService _spotifyService;
        Functions _functions;

        private string addToQueueCommand;
        private ushort commandCooldown;

        private readonly string RedirectUri = "http://localhost";





        public MainWindow()
        {
            if (File.Exists("Settings.json"))
                _botSettings = new BotSettings("Settings.json");
            else
                _botSettings = new BotSettings();
            InitializeComponent();
            commandNameTextBox.Text = _botSettings.CommandName;
            commandCooldownTextbox.Text = Convert.ToString(_botSettings.CommandCooldown);
            commandUseCheckBox.IsChecked = _botSettings.isCommandEnabled;
            subCheckCheckBox.IsChecked = _botSettings.isSubOnly;
        }

        private void StartBotButton_Click(object sender, RoutedEventArgs e)
        {

            startBotButton.IsEnabled = false;
            startBotButton.Visibility = Visibility.Hidden;
            updateButton.Visibility = Visibility.Visible;
            UpdateButton_Click(sender, e);
            _spotifyService = new SpotifyService(this, RedirectUri);
            _functions = new Functions(_spotifyService, this, _botSettings);
            _twitchService = new TwitchService(this, _botSettings, _functions, RedirectUri);


        }

        public void Log(string logs)
        {
            this.Dispatcher.Invoke(() =>
            {
                logTextBox.Text += (logs + "\n");
            });
            Console.WriteLine(logs);
        }

        private async void MainWindowClosting(object sender, System.ComponentModel.CancelEventArgs e)
        {
            await _botSettings.UpdateSettingsAsync(commandNameTextBox.Text, Convert.ToUInt16(commandCooldownTextbox.Text));
            //Ending the connection
            if (_twitchService.GetOwnerOfChannel() != null)
            {
                _twitchService.GetOwnerOfChannel().Disconnect();
            }
            //Disposing from web Server
            if (_twitchService.GetServer() != null)
            {
                _twitchService.GetServer().Stop();
                _twitchService.GetServer().Dispose();
            }
        }
        //Need to refactor a bit

        private void CommandCooldownTextbox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _botSettings.UpdateSettingsAsync(commandNameTextBox.Text, Convert.ToUInt16(commandCooldownTextbox.Text));
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        private void CommandUseCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (commandUseCheckBox.IsChecked == true) _botSettings.isCommandEnabled = true; else _botSettings.isCommandEnabled = false;

        }

        private void SubCheckCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (subCheckCheckBox.IsChecked == true) _botSettings.isSubOnly = true; else _botSettings.isSubOnly = false;
        }

        private void CommandNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (updateButton.Visibility == Visibility.Visible)
            {
                updateButton.IsEnabled = true;
            }
        }

        private void CommandCooldownTextbox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (updateButton.Visibility == Visibility.Visible)
            {
                updateButton.IsEnabled = true;
            }
        }
    }
}
