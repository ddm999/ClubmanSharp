using SimulatorInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ClubmanSharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isStarted = false;
        private string ip = "192.168.1.30";
        private Bot bot;
        private DateTime nextUpdate = DateTime.UtcNow;

        public MainWindow()
        {
            InitializeComponent();

            TxtIP.Text = ip;

            bot = new Bot();

            CompositionTarget.Rendering += VisualLoop;

            TxtHelp.Text = "This script recommends use of the R33 GT-R '97 with a specific tune for 1:20 avg laps.\n" +
                           "It may work with other vehicles and tunes but performance cannot be guaranteed.\n" +
                           "You must set Pedal Controls to R2/L2, Steering to Left Stick, and Nitrous to R3 button.\n" +
                           "Automatic gearing assist is required. Turning off other assists (but keep ABS default) may improve performance. Sensitivity 10 may also help.\n\n" +
                           "This script does not use pixel checks, so the script cannot inform you when it doesn't achieve a Clean Race Bonus.\n" +
                           "Latency or other issues with your Remote Play setup may cause issues with the script.\n\n" +
                           "Turn on a password requirement for PlayStation purchases before using any script.\n" +
                           "You must start Remote Play with no controller connected to your PC. It cannot be minimised, but you can move other programs above it.\n" +
                           "Enter your PS4/PS5's local IP address and hit Start while on the Tokyo Clubman+ pre-race menu.";
        }

        public void VisualLoop(object? sender, EventArgs? e)
        {
            if (bot.error is true)
            {
                MessageBox.Show(bot.errorMsg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                bot.error = false;
                BtnStartStop.Content = "Start";
                TxtIP.IsEnabled = true;
                BtnStartStop.IsEnabled = true;
            }

            // don't update text constantly because it actually uses an unnecessary chunk of GPU lol
            if (nextUpdate > DateTime.UtcNow)
            {
                return;
            }
            nextUpdate = DateTime.UtcNow + new TimeSpan(1000000);

            TxtState.Text = "Current State: ";
            if (bot.currentMenuState == Bot.MenuState.Unknown)
                TxtState.Text += "Unknown";
            else if (bot.currentMenuState == Bot.MenuState.Race)
                TxtState.Text += "Race";
            else if (bot.currentMenuState == Bot.MenuState.RacePaused)
                TxtState.Text += "Race Paused";
            else if (bot.currentMenuState == Bot.MenuState.RaceStart)
                TxtState.Text += "Race Start";
            else if (bot.currentMenuState == Bot.MenuState.PreRace)
                TxtState.Text += "Pre Race";
            else if (bot.currentMenuState == Bot.MenuState.RaceResult)
                TxtState.Text += "Race Result";
            else if (bot.currentMenuState == Bot.MenuState.Replay)
                TxtState.Text += "Replay";
            else if (bot.currentMenuState == Bot.MenuState.PostRace)
                TxtState.Text += "Post Race";

            TxtLap.Text = $"Fastest Lap: {bot.fastestLap.Minutes:d1}:{bot.fastestLap.Seconds:d2}.{bot.fastestLap.Milliseconds:d3}";
            TxtRaces.Text = $"Completed Races: {bot.completedRaces}";
            TxtCredits.Text = $"Estimated Credits: {bot.completedRaces * 105000 * 0.98:n0}";
        }

        private void StartStop_Click(object sender, RoutedEventArgs e)
        {
            TxtIP.IsEnabled = false;
            BtnStartStop.IsEnabled = false;

            ip = TxtIP.Text;
            isStarted = !isStarted;

            if (isStarted)
            {
                BtnStartStop.Content = "Starting...";
                bot.Start(ip);
                if (bot.error is true)
                {
                    MessageBox.Show(bot.errorMsg, "Error on starting bot", MessageBoxButton.OK, MessageBoxImage.Error);
                    bot.error = false;
                    BtnStartStop.Content = "Start";
                    TxtIP.IsEnabled = true;
                    BtnStartStop.IsEnabled = true;
                    return;
                }
                BtnStartStop.Content = "Stop";
                BtnStartStop.IsEnabled = true;
            }
            else
            {
                BtnStartStop.Content = "Stopping...";
                bot.Stop();

                BtnStartStop.Content = "Start";
                TxtIP.IsEnabled = true;
                BtnStartStop.IsEnabled = true;
            }
        }
    }
}
