using System;
using System.Threading.Tasks;
using PDTools.SimulatorInterface;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using System.Diagnostics;
using System.Threading;
using ClubmanSharp.TrackData;

namespace ClubmanSharp
{
    public class Bot
    {
        private IDualShock4Controller? _ds4 = null;
        private SimulatorInterfaceClient? _simInterface = null;
        private CancellationTokenSource? _simInterfaceCTS = null;
        private int _preRaceStuckCount = 0;
        private int _raceResultStuckCount = 0;

        public SimulatorPacket? currentPacket = null;
        public TrackDataBase? currentTrackData = null;

        public const int TimeOut = 10;
        public TimeSpan TimeOutSpan = new(0, 0, TimeOut);

        public int ShortDelay = 250;
        public int LongDelay = 3000;
        public byte LateRaceMaxThrottle = 240;

        public DualShock4Button confirmButton = DualShock4Button.Cross;
        public DualShock4Button cancelButton = DualShock4Button.Circle;

        public bool connected = false;
        public bool error = false;
        public string errorMsg = "";

        public int completedRaces = 0;
        public int stuckDetectionRuns = 0;

        public string buttonString = "";

        public TimeSpan fastestLap = new(0, 59, 59);

        public MenuState currentMenuState = MenuState.Unknown;

        public bool writeLocationData = false;

        public void Start(string ip)
        {
            DebugLog.Log($"Starting BotStart", LogType.Main);
            SimulatorInterfaceGameType type = SimulatorInterfaceGameType.GT7;
            try
            {
                DebugLog.Log($"Initializing SimulatorInterfaceClient", LogType.Main);
                _simInterface = new SimulatorInterfaceClient(ip, type);
                _simInterface.OnReceive += SimInterface_OnReceive;
            }
            catch (Exception ex)
            {
                DebugLog.Log($"BotError: SimulatorInterfaceClient init & OnReceive add failed.\n{ex.Source}\n{ex.Message}\n{ex.StackTrace}", LogType.Main);
                error = true;
                errorMsg = $"Failed to start Simulator Interface client.\nException details below:\n\n{ex.Message}";
                return;
            }

            try
            {
                DebugLog.Log($"Initializing ViGEmClient", LogType.Main);
                var client = new ViGEmClient();
                DebugLog.Log($"CreateDualShock4Controller", LogType.Main);
                _ds4 = client.CreateDualShock4Controller();
                DebugLog.Log($"ds4 Connect", LogType.Main);
                _ds4.Connect();
            }
            catch (Exception ex)
            {
                DebugLog.Log($"BotError: ViGEmClient init & controller connect failed.\n{ex.Source}\n{ex.Message}\n{ex.StackTrace}", LogType.Main);
                error = true;
                errorMsg = $"Failed to start ViGEm client and emulated DualShock 4 controller.\nException details below:\n\n{ex.Message}";
                return;
            }

            DebugLog.Log($"Connected, running tasks", LogType.Main);
            connected = true;
            Task.Run(() => SimInterfaceClientRunner());
            Task.Run(() => PacketUpdateLoop());
            Task.Run(() => DriverLoop());
            Task.Run(() => MenuUserLoop());
            DebugLog.Log($"Finished BotStart", LogType.Main);
        }

        public void Stop()
        {
            DebugLog.Log($"Starting BotStop", LogType.Main);
            connected = false;

            if (_simInterfaceCTS != null)
                _simInterfaceCTS.Cancel();

            if (_simInterface != null && _simInterface.Started)
                _simInterface.Dispose();
            DebugLog.Log($"Finished BotStop", LogType.Main);
        }

        private void SimInterface_OnReceive(SimulatorPacket packet)
        {
            currentPacket = packet;
        }

        private void PacketUpdateLoop()
        {
            DebugLog.Log($"Starting PacketUpdateLoop (nofin)", LogType.Main);
            var ok = true;
            while (ok)
            {
                Thread.Sleep(1000);
                if (!connected || currentPacket is null)
                {
                    // log message disabled, too much spam
                    //DebugLog.Log($"PacketUpdateLoop: not connected or no packet", LogType.Main);
                    continue;
                }

                if (currentPacket.BestLapTime.Milliseconds != -1 && fastestLap > currentPacket.BestLapTime)
                {
                    DebugLog.Log($"PacketUpdateLoop: setting new fastest lap = {fastestLap}", LogType.Main);
                    fastestLap = currentPacket.BestLapTime;
                }

                if (DateTimeOffset.Now - currentPacket.DateReceived > TimeOutSpan)
                {
                    DebugLog.Log($"PacketUpdateLoop: last packet was {currentPacket.DateReceived:s}, timeout", LogType.Main);
                    DisconnectController();
                    connected = false;
                    error = true;
                    errorMsg = $"Connection timed out:\nno data packet recieved for {TimeOut} seconds.";
                    return;
                }
            }
        }

        private async void SimInterfaceClientRunner()
        {
            DebugLog.Log($"Started SimInterfaceClientRunner", LogType.Main);
            if (_simInterface is null)
            {
                DebugLog.Log($"SimInterfaceClientRunner: no sim interface!!", LogType.Main);
                connected = false;
                error = true;
                errorMsg = "Internal object for Sim Interface Client Runner not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            _simInterfaceCTS = new CancellationTokenSource();

            DebugLog.Log($"Starting sim interface", LogType.Main);
            var task = _simInterface.Start(_simInterfaceCTS.Token);

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            { }
            catch (Exception ex)
            {
                DebugLog.Log($"Error: from sim interface\n{ex.Source}\n{ex.Message}\n{ex.StackTrace}", LogType.Main);
                error = true;
                errorMsg = $"Error during Simulator Interface client update.\nException details below:\n\n{ex.Message}";
            }
        }

        /*private void LocateCar()
        {
            var posString = $"{currentPacket.Position.X} x,{currentPacket.Position.Z} z,{(1-currentPacket.RelativeOrientationToNorth)*180} rotn,{currentPacket.MetersPerSecond * 2.23694} mph";
            Trace.WriteLine(posString);

            if (writeLocationData is true)
                File.AppendAllTextAsync("locationData.txt", $"{posString}\n");
        }*/

        private void DriverLoop()
        {
            DebugLog.Log($"Starting DriverLoop (nofin)", LogType.Driv);
            if (_ds4 is null || currentTrackData is null)
            {
                DebugLog.Log($"DriverLoop: no ds4 OR no currentTrackData!!", LogType.Driv);
                connected = false;
                error = true;
                errorMsg = "Internal object for Driver not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            // block for 3 seconds so it doesn't interfere with restarting a race
            Thread.Sleep(3000);

            bool ok = true;
            bool _buttonmashToggler = false;
            while (ok)
            {
                if (!connected)
                {
                    DebugLog.Log($"DriverLoop: not connected!!", LogType.Driv);
                    DisconnectController();
                    return;
                }

                if (currentPacket is null || currentMenuState != MenuState.Race)
                {
                    DebugLog.Log($"DriverLoop: no packet OR not in race... zzz", LogType.Driv);
                    Thread.Sleep(1000);
                    continue;
                }
                Thread.Sleep(100);

                try
                {
                    // handle accel & brake for target speed
                    var mph = currentPacket.MetersPerSecond * 2.23694;
                    var rotn = (1 - currentPacket.RelativeOrientationToNorth) * 180;
                    DebugLog.Log($"DriverLoop DRIVING: mph {mph} rotn {rotn}", LogType.Driv);

                    var targets = currentTrackData.GetTargets(currentPacket.Position.X, currentPacket.Position.Z, currentPacket.LapCount);
                    var targetMph = targets.Item1;
                    var targetOrientation = targets.Item2;
                    DebugLog.Log($"DriverLoop DRIVING: targetMph {targetMph} targetOrientation {targetOrientation}", LogType.Driv);

                    buttonString = "";

                    // default all buttons off
                    // NOS
                    _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                    // brake
                    _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                    _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                    // accel
                    _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                    _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);

                    _ds4.SetButtonState(confirmButton, false);
                    if (targetMph == -1 && targetOrientation == -1)
                    {
                        // we're in the pitbox. smash button.
                        _buttonmashToggler = !_buttonmashToggler;
                        if (_buttonmashToggler is true)
                        {
                            DebugLog.Log($"DriverLoop DRIVING: PITBOX!! LEFT", LogType.Driv);
                            _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                            _ds4.SetButtonState(confirmButton, false);
                            buttonString += "L";
                        }
                        else
                        {
                            DebugLog.Log($"DriverLoop DRIVING: PITBOX!! CONFIRM", LogType.Driv);
                            _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                            _ds4.SetButtonState(confirmButton, true);
                            buttonString += "X";
                        }
                        _ds4.SubmitReport();
                        continue;
                    }

                    targetMph += 2; // needed bc of how the acceleration decrease scales

                    // FULL BRAKE
                    if (mph > targetMph * 1.2)
                    {
                        DebugLog.Log($"DriverLoop DRIVING: full brake", LogType.Driv);
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        buttonString += "B";
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 255);
                    }
                    // PARTIAL BRAKE
                    else if (mph > targetMph)
                    {
                        // brake
                        var diff = mph - targetMph;
                        var input = Convert.ToByte(255 - (255 / (targetMph * 0.2) * diff));
                        DebugLog.Log($"DriverLoop DRIVING: partial brake {input}", LogType.Driv);
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        buttonString += "B";
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, input);
                    }
                    // PARTIAL ACCEL
                    else if (mph > targetMph * 0.9)
                    {
                        // accel
                        var diff = targetMph - mph;
                        var input = Convert.ToByte(255 / (targetMph * 0.1) * diff);
                        DebugLog.Log($"DriverLoop DRIVING: partial accel {input}", LogType.Driv);
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        buttonString += "A";
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, input);
                    }
                    // FULL ACCEL
                    else
                    {
                        DebugLog.Log($"DriverLoop DRIVING: full accel", LogType.Driv);
                        // NOS is only used below 150mph
                        if (mph < 150)
                        {
                            DebugLog.Log($"DriverLoop DRIVING: & under 150mph, boosting!", LogType.Driv);
                            // use NOS
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, true);
                            buttonString += "N";
                        }
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        buttonString += "A";

                        if (currentPacket.LapCount <= 2) // first 2 laps, full throttle
                            _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 255);
                        else // later laps, slow for traffic based on option
                            _ds4.SetSliderValue(DualShock4Slider.RightTrigger, LateRaceMaxThrottle);
                    }

                    // turn towards target line
                    if (targetOrientation == 360.0)
                    {
                        DebugLog.Log($"DriverLoop DRIVING: i'm lost!! no turning", LogType.Driv);
                        // not in a block, just maintain course
                        _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                    }
                    else if (targetOrientation < 0.0)
                    {
                        DebugLog.Log($"DriverLoop DRIVING: going west", LogType.Driv);
                        // heading west
                        // FULL RIGHT
                        if (-rotn < targetOrientation - 5.0)
                        {
                            DebugLog.Log($"DriverLoop DRIVING: full right (override no boost)", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 255);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL RIGHT
                        else if (-rotn < targetOrientation)
                        {
                            var diff = rotn - (-targetOrientation);
                            var input = Convert.ToByte(128 + (127 / 5.0 * diff));
                            DebugLog.Log($"DriverLoop DRIVING: partial right {input}", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // FULL LEFT
                        else if (-rotn > targetOrientation + 5.0)
                        {
                            DebugLog.Log($"DriverLoop DRIVING: full left (override no boost)", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 0);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL LEFT
                        else if (-rotn > targetOrientation)
                        {
                            var diff = (-targetOrientation) - rotn;
                            var input = Convert.ToByte(128 - (127 / 5.0 * diff));
                            DebugLog.Log($"DriverLoop DRIVING: partial left {input}", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 64);
                        }
                        // CENTERED
                        else
                        {
                            DebugLog.Log($"DriverLoop DRIVING: no steering", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                        }
                    }
                    else
                    {
                        DebugLog.Log($"DriverLoop DRIVING: going east", LogType.Driv);
                        // heading east
                        // FULL RIGHT
                        if (rotn < targetOrientation - 5.0)
                        {
                            DebugLog.Log($"DriverLoop DRIVING: full right (override no boost)", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 255);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL RIGHT
                        else if (rotn < targetOrientation)
                        {
                            var diff = rotn - targetOrientation;
                            var input = Convert.ToByte(128 - (127 / 5.0 * diff));
                            DebugLog.Log($"DriverLoop DRIVING: partial right {input}", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // FULL LEFT
                        else if (rotn > targetOrientation + 5.0)
                        {
                            DebugLog.Log($"DriverLoop DRIVING: full left (override no boost)", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 0);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL LEFT
                        else if (rotn > targetOrientation)
                        {
                            var diff = targetOrientation - rotn;
                            var input = Convert.ToByte(128 + (127 / 5.0 * diff));
                            DebugLog.Log($"DriverLoop DRIVING: partial left {input}", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // CENTERED
                        else
                        {
                            DebugLog.Log($"DriverLoop DRIVING: no steering", LogType.Driv);
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                        }
                    }

                    _ds4.SubmitReport();
                }
                catch (Exception ex)
                {
                    if (_ds4 is null)
                    {
                        return;
                    }

                    DisconnectController();
                    connected = false;
                    error = true;
                    DebugLog.Log($"BotError: DriverLoop DRIVING failed\n{ex.Source}\n{ex.Message}\n{ex.StackTrace}", LogType.Driv);
                    errorMsg = $"Unexpected error sending driver inputs to ViGEm.\nException details below:\n\n{ex.Message}";
                    return;
                }
            }
        }

        public enum MenuState
        {
            Race = 0,
            RacePaused,
            RaceStart,
            TokyoEvents,
            PreRace,
            RaceResult,
            Replay,
            PostRace,
            Stuck_PreOrPostRace,
            Stuck_PostRace,
            Stuck_PreRace,
            Stuck_Replay,
            NoPacket,

            Unknown = -1,
        }

        public MenuState FindBaseMenuState(bool allowReplay=false)
        {
            if (currentPacket is null)
                return MenuState.NoPacket;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                return MenuState.RacePaused;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                return MenuState.Race;
            if (currentPacket.NumCarsAtPreRace > 1)
                return MenuState.PreRace;
            if (allowReplay && currentPacket.LapsInRace > 0)
                return MenuState.Replay;
            return MenuState.Unknown;
        }

        public void PreRaceStuckDetection()
        {
            DebugLog.Log($"Incremented preRaceStuckCount", LogType.Menu);
            _preRaceStuckCount += 1;
            if (_preRaceStuckCount >= 5)
            {
                DebugLog.Log($"PreRaceStuckDetection STUCK!!", LogType.Menu);
                currentMenuState = MenuState.Stuck_PreOrPostRace;
                _preRaceStuckCount = 0;
                stuckDetectionRuns += 1;
            }
        }

        public void RaceResultStuckDetection()
        {
            DebugLog.Log($"Incremented raceResultStuckCount", LogType.Menu);
            _raceResultStuckCount += 1;
            if (_raceResultStuckCount >= 100)
            {
                DebugLog.Log($"RaceResultStuckDetection STUCK!!", LogType.Menu);
                DisconnectController();
                connected = false;
                error = true;
                errorMsg = "Stuck in Race Result. Unable to figure out how to get out of this state.";
            }
        }

        public MenuState FindNewMenuState()
        {
            DebugLog.Log($"Started FindNewMenuState (nofin)", LogType.Menu);
            // no packet recieved or no race is loaded
            if (currentPacket is null || currentPacket.LapCount == -1)
            {
                DebugLog.Log($"FindNewMenuState: packet is null or lap is -1. State is Unknown.", LogType.Menu);
                return MenuState.Unknown;
            }

            if (currentMenuState == MenuState.PreRace)
            {
                DebugLog.Log($"FindNewMenuState: state WAS PreRace.", LogType.Menu);
                if (currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                {
                    DebugLog.Log($"FindNewMenuState: car on track! State is Race.", LogType.Menu);
                    return MenuState.Race;
                }
                PreRaceStuckDetection();
            }
            else if (currentMenuState == MenuState.Race)
            {
                DebugLog.Log($"FindNewMenuState: state WAS Race.", LogType.Menu);
                if (currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                {
                    DebugLog.Log($"FindNewMenuState: paused! State is RacePaused.", LogType.Menu);
                    return MenuState.RacePaused;
                }
                if (currentPacket.LapCount > 5)
                {
                    DebugLog.Log($"FindNewMenuState: lap > 5! State is RaceResult.", LogType.Menu);
                    return MenuState.RaceResult;
                }
            }
            else if (currentMenuState == MenuState.RacePaused)
            {
                DebugLog.Log($"FindNewMenuState: state WAS RacePaused.", LogType.Menu);
                // have to just assume Exit is never clicked
                //  because it can bring up a mini-rewards screen with no way to tell
                if (!currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                {
                    DebugLog.Log($"FindNewMenuState: not paused! State is Race.", LogType.Menu);
                    return MenuState.Race;
                }
            }
            else if (currentMenuState == MenuState.RaceResult)
            {
                DebugLog.Log($"FindNewMenuState: state WAS RaceResult.", LogType.Menu);
                if (!currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                {
                    DebugLog.Log($"FindNewMenuState: car on track! State is Replay.", LogType.Menu);
                    return MenuState.Replay;
                }
                RaceResultStuckDetection();
            }
            else if (currentMenuState == MenuState.Replay)
            {
                DebugLog.Log($"FindNewMenuState: state WAS Replay.", LogType.Menu);
                if (currentPacket.NumCarsAtPreRace > 0)
                {
                    DebugLog.Log($"FindNewMenuState: prerace cars set! State is PostRace.", LogType.Menu);
                    return MenuState.PostRace;
                }
            }
            return currentMenuState;
        }

        private void PreRaceInputRunner()
        {
            // ensure we're hovered over start race

            // first, smash the hell out of the circle button
            for (int i = 0; i < 5; i++)
            {
                DebugLog.Log($"PreRaceInputRunner: cancel to exit [ON]", LogType.Menu);
                _ds4.SetButtonState(cancelButton, true);
                buttonString = "O";
                _ds4.SubmitReport();
                Thread.Sleep(50);

                DebugLog.Log($"PreRaceInputRunner: cancel to exit [OFF]", LogType.Menu);
                _ds4.SetButtonState(cancelButton, false);
                buttonString = "";
                _ds4.SubmitReport();
                Thread.Sleep(ShortDelay);
            }
            // then smash the hell out of left dpad
            for (int i = 0; i < 5; i++)
            {
                DebugLog.Log($"PreRaceInputRunner: left to weather [ON]", LogType.Menu);
                _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                buttonString = "L";
                _ds4.SubmitReport();
                Thread.Sleep(50);

                DebugLog.Log($"PreRaceInputRunner: left to weather [OFF]", LogType.Menu);
                _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                buttonString = "";
                _ds4.SubmitReport();
                Thread.Sleep(ShortDelay);
            }
            // then press down
            DebugLog.Log($"PreRaceInputRunner: down to be safe? [ON]", LogType.Menu);
            _ds4.SetDPadDirection(DualShock4DPadDirection.South);
            buttonString = "D";
            _ds4.SubmitReport();
            Thread.Sleep(50);

            DebugLog.Log($"PreRaceInputRunner: down to be safe? [OFF]", LogType.Menu);
            _ds4.SetDPadDirection(DualShock4DPadDirection.None);
            buttonString = "";
            _ds4.SubmitReport();
            Thread.Sleep(ShortDelay);

            // then press right to move to Start from Weather Radar
            DebugLog.Log($"PreRaceInputRunner: right to start [ON]", LogType.Menu);
            _ds4.SetDPadDirection(DualShock4DPadDirection.East);
            buttonString = "R";
            _ds4.SubmitReport();
            Thread.Sleep(50);

            DebugLog.Log($"PreRaceInputRunner: right to start [OFF]", LogType.Menu);
            _ds4.SetDPadDirection(DualShock4DPadDirection.None);
            buttonString = "";
            _ds4.SubmitReport();
            Thread.Sleep(ShortDelay);

            // and finally, click start race
            DebugLog.Log($"PreRaceInputRunner: click start [ON]", LogType.Menu);
            _ds4.SetButtonState(confirmButton, true);
            buttonString = "X";
            _ds4.SubmitReport();
            Thread.Sleep(50);

            DebugLog.Log($"PreRaceInputRunner: click start [OFF]", LogType.Menu);
            _ds4.SetButtonState(confirmButton, false);
            buttonString = "";
            _ds4.SubmitReport();
            Thread.Sleep(ShortDelay);
        }

        private void MenuUserLoop()
        {
            DebugLog.Log($"Starting MenuUserLoop (nofin)", LogType.Menu);
            if (_ds4 is null)
            {
                DebugLog.Log($"MenuUserLoop: no ds4!!", LogType.Menu);
                connected = false;
                error = true;
                errorMsg = "Internal object for MenuUser not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            Thread.Sleep(1000);

            currentMenuState = FindBaseMenuState();
            DebugLog.Log($"MenuUserLoop: base menu state is {currentMenuState}", LogType.Menu);

            if (currentMenuState == MenuState.Unknown)
            {
                DebugLog.Log($"BotError: can't start if menu state is Unknown", LogType.Menu);
                connected = false;
                error = true;
                errorMsg = "Couldn't determine game state. Open the pre-race menu before starting.";
                return;
            }
            else if (currentMenuState == MenuState.NoPacket)
            {
                DebugLog.Log($"BotError: can't start if menu state is NoPacket", LogType.Menu);
                connected = false;
                error = true;
                errorMsg = "No packet received.\nCheck your connection, allow ClubmanSharp access through any firewalls, verify the entered IP address for your console.\n\n" +
                           "A restricted network such as a university campus may block the connection:\n" +
                           "you can try using a mobile hotspot (be aware of your data usage) or internet connection sharing through your PC.";
                return;
            }

            try
            {
                if (currentMenuState == MenuState.PreRace)
                {
                    // check the right number of cars are in PreRace
                    if (currentPacket.NumCarsAtPreRace != 5)
                    {
                        DebugLog.Log($"BotError: incorrect prerace cars - was {currentPacket.NumCarsAtPreRace}", LogType.Menu);
                        DisconnectController();
                        connected = false;
                        error = true;
                        errorMsg = $"Incorrect number of cars in Pre-Race.\nPlease verify that you have selected the Tokyo Expressway Clubman Cup Plus event.\n\n(The Japanese Clubman Cup 550 is a different event!)";
                        return;
                    }

                    PreRaceInputRunner();
                }
                else if (currentMenuState == MenuState.Race)
                {
                    DebugLog.Log($"InitialMenuUser Race: pause [ON]", LogType.Menu);
                    _ds4.SetButtonState(DualShock4Button.Options, true);
                    buttonString = "S";
                    _ds4.SubmitReport();
                    Thread.Sleep(50);

                    DebugLog.Log($"InitialMenuUser Race: pause [OFF]", LogType.Menu);
                    _ds4.SetButtonState(DualShock4Button.Options, false);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    DebugLog.Log($"InitialMenuUser Race: set state to RacePaused", LogType.Menu);
                    currentMenuState = MenuState.RacePaused;
                }

                if (currentMenuState == MenuState.RacePaused)
                {
                    // smash the hell out of left dpad
                    for (int i = 0; i < 5; i++)
                    {
                        DebugLog.Log($"InitialMenuUser RacePaused: left to resume [ON]", LogType.Menu);
                        _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                        buttonString = "L";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"InitialMenuUser RacePaused: left to resume [OFF]", LogType.Menu);
                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);
                    }
                    // then go right once
                    DebugLog.Log($"InitialMenuUser RacePaused: right to retry [ON]", LogType.Menu);
                    _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                    buttonString = "R";
                    _ds4.SubmitReport();

                    Thread.Sleep(50);
                    DebugLog.Log($"InitialMenuUser RacePaused: right to retry [OFF]", LogType.Menu);
                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    // and click to restart race
                    DebugLog.Log($"InitialMenuUser RacePaused: click retry [ON]", LogType.Menu);
                    _ds4.SetButtonState(confirmButton, true);
                    buttonString = "X";
                    _ds4.SubmitReport();
                    Thread.Sleep(50);

                    DebugLog.Log($"InitialMenuUser RacePaused: click retry [OFF]", LogType.Menu);
                    _ds4.SetButtonState(confirmButton, false);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    Thread.Sleep(LongDelay);
                }
            }
            catch (Exception ex)
            {
                if (_ds4 is null)
                {
                    return;
                }
                else
                {
                    DisconnectController();
                    connected = false;
                    error = true;
                    DebugLog.Log($"BotError: InitialMenuUser failed\n{ex.Source}\n{ex.Message}\n{ex.StackTrace}", LogType.Menu);
                    errorMsg = $"Unexpected error sending initial menu inputs to ViGEm.\nException details below:\n\n{ex.Message}";
                    return;
                }
            }

            currentTrackData.NewRace();

            bool ok = true;
            bool registeredResult = false;
            while (ok)
            {
                if (!connected)
                {
                    DebugLog.Log($"MenuUser: not connected", LogType.Menu);
                    DisconnectController();
                    return;
                }

                if (currentMenuState == MenuState.Unknown)
                {
                    DebugLog.Log($"MenuUser: unknown state", LogType.Menu);
                    Thread.Sleep(1000);
                    currentMenuState = FindBaseMenuState();
                    continue;
                }

                currentMenuState = FindNewMenuState();

                try
                {
                    if (currentMenuState == MenuState.Unknown)
                    {
                        DebugLog.Log($"BotError: MenuUser: changed to unknown state", LogType.Menu);
                        DisconnectController();
                        connected = false;
                        error = true;
                        errorMsg = "Unexpected menu state change. Unable to determine game state.";
                        return;
                    }
                    else if (currentMenuState == MenuState.RaceResult)
                    {
                        Thread.Sleep(ShortDelay);

                        if (!registeredResult)
                        {
                            DebugLog.Log($"MenuUser RaceResult: registering new result", LogType.Menu);
                            completedRaces += 1;

                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                            _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                            _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                            _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);

                            registeredResult = true;

                            currentTrackData.NewRace();
                        }

                        DebugLog.Log($"MenuUser RaceResult: click continue [ON]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser RaceResult: click continue [OFF]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();
                    }
                    else if (currentMenuState == MenuState.Replay)
                    {
                        registeredResult = false;
                        _raceResultStuckCount = 0;

                        Thread.Sleep(LongDelay);

                        for (int i = 0; i < 2; i++)
                        {
                            DebugLog.Log($"MenuUser Replay: click show hud / exit [ON]", LogType.Menu);
                            _ds4.SetButtonState(confirmButton, true);
                            buttonString = "X";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            DebugLog.Log($"MenuUser Replay: click show hud / exit [OFF]", LogType.Menu);
                            _ds4.SetButtonState(confirmButton, false);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(ShortDelay);
                        }

                        Thread.Sleep(LongDelay);
                    }
                    else if (currentMenuState == MenuState.PostRace)
                    {
                        Thread.Sleep(LongDelay);

                        DebugLog.Log($"MenuUser PostRace: right to retry [ON]", LogType.Menu);
                        _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                        buttonString = "R";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser PostRace: right to retry [OFF]", LogType.Menu);
                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);

                        DebugLog.Log($"MenuUser PostRace: click retry [ON]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser PostRace: click retry [OFF]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        _preRaceStuckCount = 0;
                        DebugLog.Log($"MenuUser PostRace: set state to PreRace", LogType.Menu);
                        currentMenuState = MenuState.PreRace;
                    }
                    else if (currentMenuState == MenuState.PreRace)
                    {
                        Thread.Sleep(LongDelay);

                        DebugLog.Log($"MenuUser PreRace: click start [ON]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser PreRace: click start [OFF]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        Thread.Sleep(LongDelay);
                    }
                    else if (currentMenuState == MenuState.Stuck_PreOrPostRace)
                    {
                        DebugLog.Log($"MenuUser Stuck_PreOrPostRace: attempting recovery", LogType.Menu);
                        Thread.Sleep(3000);

                        PreRaceInputRunner();

                        Thread.Sleep(5000);

                        // if we're still in pre-race, it's actually post-race
                        currentMenuState = FindBaseMenuState(true);
                        if (currentMenuState == MenuState.PreRace)
                        {
                            DebugLog.Log($"MenuUser Stuck_PreOrPostRace: set state to Stuck_PostRace", LogType.Menu);
                            currentMenuState = MenuState.Stuck_PostRace;
                        }
                        if (currentMenuState == MenuState.Replay)
                        {
                            DebugLog.Log($"MenuUser Stuck_PreOrPostRace: set state to Stuck_Replay", LogType.Menu);
                            currentMenuState = MenuState.Stuck_Replay;
                        }
                    }
                    else if (currentMenuState == MenuState.Stuck_Replay)
                    {
                        DebugLog.Log($"MenuUser Stuck_Replay: attempting recovery", LogType.Menu);
                        // get to the exit button
                        for (int i = 0; i < 10; i++)
                        {
                            DebugLog.Log($"MenuUser Stuck_Replay: right to exit [ON]", LogType.Menu);
                            _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                            buttonString = "R";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            DebugLog.Log($"MenuUser Stuck_Replay: right to exit [OFF]", LogType.Menu);
                            _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(250);
                        }
                        // click exit
                        DebugLog.Log($"MenuUser Stuck_Replay: click exit [ON]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser Stuck_Replay: click exit [OFF]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(250);

                        Thread.Sleep(3000);
                        DebugLog.Log($"MenuUser Stuck_Replay: set state to PostRace", LogType.Menu);
                        currentMenuState = MenuState.PostRace;
                    }
                    else if (currentMenuState == MenuState.Stuck_PostRace)
                    {
                        DebugLog.Log($"MenuUser Stuck_PostRace: attempting recovery", LogType.Menu);
                        // first, smash the hell out of the circle button
                        for (int i = 0; i < 5; i++)
                        {
                            DebugLog.Log($"MenuUser Stuck_PostRace: cancel to exit [ON]", LogType.Menu);
                            _ds4.SetButtonState(cancelButton, true);
                            buttonString = "O";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            DebugLog.Log($"MenuUser Stuck_PostRace: cancel to exit [OFF]", LogType.Menu);
                            _ds4.SetButtonState(cancelButton, false);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(250);
                        }
                        // then press left
                        DebugLog.Log($"MenuUser Stuck_PostRace: left to retry [ON]", LogType.Menu);
                        _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                        buttonString = "L";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser Stuck_PostRace: left to retry [OFF]", LogType.Menu);
                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(250);

                        // and finally, click retry
                        DebugLog.Log($"MenuUser Stuck_PostRace: click retry [ON]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser Stuck_PostRace: click retry [OFF]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        DebugLog.Log($"MenuUser Stuck_PostRace: set state to Stuck_PreRace", LogType.Menu);
                        currentMenuState = MenuState.Stuck_PreRace;
                    }
                    else if (currentMenuState == MenuState.Stuck_PreRace)
                    {
                        DebugLog.Log($"MenuUser Stuck_PreRace: continuing recovery", LogType.Menu);
                        Thread.Sleep(8000);

                        DebugLog.Log($"MenuUser Stuck_PreRace: click start [ON]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        DebugLog.Log($"MenuUser Stuck_PreRace: click start [OFF]", LogType.Menu);
                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        Thread.Sleep(5000);

                        currentMenuState = MenuState.PreRace;
                        currentMenuState = FindNewMenuState();
                        if (currentMenuState != MenuState.Race)
                        {
                            DebugLog.Log($"BotError: MenuUser gave up. don't wanna do harm when totally lost", LogType.Menu);
                            DisconnectController();
                            connected = false;
                            error = true;
                            errorMsg = "Got stuck attempting to correct a post-race menu failure. Unable to determine game state.";
                            return;
                        }
                    }
                    else
                    {
                        DebugLog.Log($"MenuLoop: in race... zzz", LogType.Menu);
                        Thread.Sleep(1000);
                    }
                }
                catch (Exception ex)
                {
                    if (_ds4 is null)
                    {
                        return;
                    }
                    else
                    {
                        DisconnectController();
                        connected = false;
                        error = true;
                        DebugLog.Log($"BotError: MenuUser failed\n{ex.Source}\n{ex.Message}\n{ex.StackTrace}", LogType.Menu);
                        errorMsg = $"Unexpected error sending menu inputs to ViGEm.\nException details below:\n\n{ex.Message}";
                        return;
                    }
                }
            }
        }

        private void DisconnectController()
        {
            DebugLog.Log($"Starting DisconnectController", LogType.Main);
            if (_ds4 != null)
            {
                DebugLog.Log($"Sending disconnect to ds4 & nulling", LogType.Main);
                _ds4.Disconnect();
                _ds4 = null;
            }
            DebugLog.Log($"Finished DisconnectController", LogType.Main);
        }
    }
}
