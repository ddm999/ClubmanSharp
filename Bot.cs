using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using PDTools.Crypto.SimulationInterface;
using SimulatorInterface;
using Syroot.BinaryData.Memory;
using System.Numerics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using System.Diagnostics;
using System.Threading;
using System.IO;

namespace ClubmanSharp
{
    public class Bot
    {
        private ISimulationInterfaceCryptor? _cryptor = null;
        private IPEndPoint? _endpoint = null;
        private UdpClient? _udpClient = null;
        private IDualShock4Controller? _ds4 = null;

        public SimulatorPacketGT7? currentPacket = null;

        public const int SendDelaySeconds = 10;
        public const int ReceivePort = 33739;
        public const int BindPort = 33740;

        public const int LongLoadTime = 30000;
        public const int LoadTime = 5000;

        public bool connected = false;
        public bool error = false;
        public string errorMsg = "";

        public int completedRaces = 0;

        public MenuState currentMenuState = MenuState.Unknown;

        public bool writeLocationData = false;

        public void Start(string ip)
        {
            if (!IPAddress.TryParse(ip, out IPAddress addr))
            {
                error = true;
                errorMsg = "Couldn't parse IP address.";
                return;
            }

            _endpoint = new IPEndPoint(addr, ReceivePort);
            _cryptor = new SimulatorInterfaceCryptorGT7();

            try
            {
                _udpClient = new UdpClient(BindPort);
                _udpClient.Send(new byte[1] { (byte)'A' }, _endpoint);
            }
            catch (Exception ex)
            {
                error = true;
                errorMsg = $"Failed to start UDP client.\nException details below:\n\n{ex.Message}";
                return;
            }

            try
            {
                var client = new ViGEmClient();
                _ds4 = client.CreateDualShock4Controller();
                _ds4.Connect();
            }
            catch (Exception ex)
            {
                error = true;
                errorMsg = $"Failed to start ViGEm client and emulated DualShock 4 controller.\nException details below:\n\n{ex.Message}";
                return;
            }

            connected = true;
            Task.Run(() => PacketUpdateLoop());
            Task.Run(() => DriverLoop());
            Task.Run(() => MenuUserLoop());

            return;
        }

        public void Stop()
        {
            connected = false;

            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient.Dispose();
            }
        }

        private void PacketUpdateLoop()
        {
            if (_cryptor is null || _endpoint is null || _udpClient is null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for PacketUpdate not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            DateTime lastSent = DateTime.UtcNow;
            IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);

            bool ok = true;
            while (ok)
            {
                if (!connected)
                {
                    return;
                }

                if ((DateTime.UtcNow - lastSent).TotalSeconds > SendDelaySeconds)
                {
                    _udpClient.Send(new byte[1] { (byte)'A' }, _endpoint);
                    lastSent = DateTime.UtcNow;
                }

                byte[] data;
                try
                {
                    data = _udpClient.Receive(ref RemoteIpEndPoint);
                }
                catch
                {
                    connected = false;
                    return;
                }

                if (data.Length != 0x128)
                {
                    connected = false;
                    error = true;
                    errorMsg = $"Unexpected packet size.\n\nWas {data.Length:X4} bytes.";
                    return;
                }

                _cryptor.Decrypt(data);

                SpanReader sr = new SpanReader(data);
                int magic = sr.ReadInt32();
                if (magic != 0x47375330) // 0S7G - G7S0
                {
                    connected = false;
                    error = true;
                    errorMsg = $"Unexpected packet magic.\n\nWas '{magic}'.";
                    return;
                }

                var dataPacket = new SimulatorPacketGT7();

                dataPacket.Position = new Vector3(sr.ReadSingle(), sr.ReadSingle(), sr.ReadSingle()); // Coords to track
                dataPacket.Acceleration = new Vector3(sr.ReadSingle(), sr.ReadSingle(), sr.ReadSingle());  // Accel in track pixels
                dataPacket.Rotation = new Vector3(sr.ReadSingle(), sr.ReadSingle(), sr.ReadSingle()); // Pitch/Yaw/Roll all -1 to 1
                dataPacket.RelativeOrientationToNorth = sr.ReadSingle();
                dataPacket.Unknown_0x2C = new Vector3(sr.ReadSingle(), sr.ReadSingle(), sr.ReadSingle());
                dataPacket.Unknown_0x38 = sr.ReadSingle();
                dataPacket.RPM = sr.ReadSingle();

                // Skip IV
                sr.Position += 8;

                dataPacket.Unknown_0x48 = sr.ReadSingle();
                dataPacket.MetersPerSecond = sr.ReadSingle();
                dataPacket.TurboBoost = sr.ReadSingle();
                dataPacket.Unknown_0x54 = sr.ReadSingle();
                dataPacket.Unknown_Always85_0x58 = sr.ReadSingle();
                dataPacket.Unknown_Always110_0x5C = sr.ReadSingle();
                dataPacket.TireSurfaceTemperatureFL = sr.ReadSingle();
                dataPacket.TireSurfaceTemperatureFR = sr.ReadSingle();
                dataPacket.TireSurfaceTemperatureRL = sr.ReadSingle();
                dataPacket.TireSurfaceTemperatureRR = sr.ReadSingle();
                dataPacket.TotalTimeTicks = sr.ReadInt32(); // can't be more than MAX_LAPTIME1000 - which is 1209599999, or else it's set to -1
                dataPacket.CurrentLap = sr.ReadInt16();
                var other = sr.ReadInt16();
                dataPacket.BestLapTime = TimeSpan.FromMilliseconds(sr.ReadInt32());
                dataPacket.LastLapTime = TimeSpan.FromMilliseconds(sr.ReadInt32());
                dataPacket.DayProgressionMS = sr.ReadInt32();
                dataPacket.PreRaceStartPositionOrQualiPos = sr.ReadInt16();
                dataPacket.NumCarsAtPreRace = sr.ReadInt16();
                dataPacket.MinAlertRPM = sr.ReadInt16();
                dataPacket.MaxAlertRPM = sr.ReadInt16();
                dataPacket.CalculatedMaxSpeed = sr.ReadInt16();
                dataPacket.Flags = (SimulatorFlags)sr.ReadInt16();

                int bits = sr.ReadByte();
                dataPacket.CurrentGear = (byte)(bits & 0b1111);
                dataPacket.SuggestedGear = (byte)(bits >> 4);

                dataPacket.Throttle = sr.ReadByte();
                dataPacket.Brake = sr.ReadByte();

                byte unknown = sr.ReadByte();

                dataPacket.TireFL_Unknown0x94_0 = sr.ReadSingle();
                dataPacket.TireFR_Unknown0x94_1 = sr.ReadSingle();
                dataPacket.TireRL_Unknown0x94_2 = sr.ReadSingle();
                dataPacket.TireRR_Unknown0x94_3 = sr.ReadSingle();
                dataPacket.TireFL_Accel = sr.ReadSingle();
                dataPacket.TireFR_Accel = sr.ReadSingle();
                dataPacket.TireRL_Accel = sr.ReadSingle();
                dataPacket.TireRR_Accel = sr.ReadSingle();
                dataPacket.TireFL_UnknownB4 = sr.ReadSingle();
                dataPacket.TireFR_UnknownB4 = sr.ReadSingle();
                dataPacket.TireRL_UnknownB4 = sr.ReadSingle();
                dataPacket.TireRR_UnknownB4 = sr.ReadSingle();
                dataPacket.TireFL_UnknownC4 = sr.ReadSingle();
                dataPacket.TireFR_UnknownC4 = sr.ReadSingle();
                dataPacket.TireRL_UnknownC4 = sr.ReadSingle();
                dataPacket.TireRR_UnknownC4 = sr.ReadSingle();

                sr.Position += sizeof(int) * 8; // Seems to be reserved - server does not set that

                dataPacket.Unknown_0xF4 = sr.ReadSingle();
                dataPacket.Unknown_0xF8 = sr.ReadSingle();
                dataPacket.RPMUnknown_0xFC = sr.ReadSingle();

                for (var i = 0; i < 7; i++)
                    dataPacket.GearRatios[i] = sr.ReadSingle();

                sr.Position += 8;
                dataPacket.CarCode = sr.ReadInt32();

                currentPacket = dataPacket;
            }
        }

        private void LocateCar()
        {
            var posString = $"{currentPacket.Position.X} x,{currentPacket.Position.Z} z,{(1-currentPacket.RelativeOrientationToNorth)*180} rotn,{currentPacket.MetersPerSecond * 2.23694} mph";
            Trace.WriteLine(posString);

            if (writeLocationData is true)
                File.AppendAllTextAsync("locationData.txt", $"{posString}\n");
        }

        private void DriverLoop()
        {
            if (_ds4 is null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for Driver not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            bool ok = true;
            while (ok)
            {
                if (!connected)
                {
                    DisconnectController();
                    return;
                }

                if (currentPacket is null ||
                    !currentPacket.Flags.HasFlag(SimulatorFlags.OnTrack) ||
                    currentPacket.CurrentLap > 5)
                {
                    Thread.Sleep(1000);
                    continue;
                }
                Thread.Sleep(100);

                try
                {
                    //LocateCar();

                    // always use all the NOS
                    _ds4.SetButtonState(DualShock4Button.ThumbRight, true);

                    // handle accel & brake for target speed
                    var mph = currentPacket.MetersPerSecond * 2.23694;
                    var rotn = (1 - currentPacket.RelativeOrientationToNorth) * 180;

                    var targets = TrackData.GetTargets(currentPacket.Position.X, currentPacket.Position.Z);
                    var targetMph = targets.Item1;
                    var targetOrientation = targets.Item2;

                    targetMph += 2; // needed bc of how the acceleration decrease scales

                    // FULL BRAKE
                    if (mph > targetMph * 1.2)
                    {
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 255);
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);
                    }
                    // PARTIAL BRAKE
                    else if (mph > targetMph)
                    {
                        // brake
                        var diff = mph - targetMph;
                        var input = Convert.ToByte(255 - (255 / (targetMph * 0.2) * diff));
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, input);
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);
                    }
                    // PARTIAL ACCEL
                    else if (mph > targetMph * 0.9)
                    {
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                        // accel
                        var diff = targetMph - mph;
                        var input = Convert.ToByte(255 / (targetMph * 0.1) * diff);
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, input);
                    }
                    // FULL ACCEL
                    else
                    {
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 255);
                    }

                    // turn towards target line
                    if (targetOrientation == 360.0)
                    {
                        // not in a block, just maintain course
                        _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                    }
                    else if (targetOrientation < 0.0)
                    {
                        // heading west
                        // FULL RIGHT
                        if (-rotn < targetOrientation - 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 255);
                        }
                        // PARTIAL RIGHT
                        else if (-rotn < targetOrientation)
                        {
                            var diff = rotn - (-targetOrientation);
                            var input = Convert.ToByte(128 + (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // FULL LEFT
                        else if (-rotn > targetOrientation + 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 0);
                        }
                        // PARTIAL LEFT
                        else if (-rotn > targetOrientation)
                        {
                            var diff = (-targetOrientation) - rotn;
                            var input = Convert.ToByte(128 - (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 64);
                        }
                        // CENTERED
                        else
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                        }
                    }
                    else
                    {
                        // heading east
                        // FULL RIGHT
                        if (rotn < targetOrientation - 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 255);
                        }
                        // PARTIAL RIGHT
                        else if (rotn < targetOrientation)
                        {
                            var diff = rotn - targetOrientation;
                            var input = Convert.ToByte(128 - (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // FULL LEFT
                        else if (rotn > targetOrientation + 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 0);
                        }
                        // PARTIAL LEFT
                        else if (rotn > targetOrientation)
                        {
                            var diff = targetOrientation - rotn;
                            var input = Convert.ToByte(128 + (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // CENTERED
                        else
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                        }
                    }

                    _ds4.SubmitReport();
                }
                catch (Exception ex)
                {
                    DisconnectController();
                    connected = false;
                    error = true;
                    errorMsg = $"Unexpected error sending inputs to ViGEm.\nException details below:\n\n{ex.Message}";
                    return;
                }
            }
        }

        public enum MenuState
        {
            Race = 0,
            RacePaused,
            RaceStart,
            GTMode,
            WorldCircuits,
            PreRace,
            RaceResult,
            Replay,
            PostRace,

            Unknown = -1,
        }

        public MenuState FindBaseMenuState()
        {
            if (currentPacket is null)
                return MenuState.Unknown;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                return MenuState.RacePaused;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.OnTrack))
                return MenuState.Race;
            if (currentPacket.NumCarsAtPreRace > 1)
                return MenuState.PreRace;
            return MenuState.Unknown;
        }

        private void MenuUserLoop()
        {
            if (_ds4 is null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for MenuUser not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            Thread.Sleep(1000);

            currentMenuState = FindBaseMenuState();

            if (currentMenuState == MenuState.Unknown)
            {
                connected = false;
                error = true;
                errorMsg = "Couldn't determine game state. Open the pre-race menu before starting.";
                return;
            }

            if (currentMenuState == MenuState.PreRace)
            {
                // ensure we're hovered over start race

                // first, smash the hell out of the circle button
                for (int i = 0; i < 5; i++)
                {
                    _ds4.SetButtonState(DualShock4Button.Circle, true);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetButtonState(DualShock4Button.Circle, false);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                }
                // then smash the hell out of left dpad
                for (int i = 0; i < 5; i++)
                {
                    _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                }
                // then press down
                _ds4.SetDPadDirection(DualShock4DPadDirection.South);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                // and finally, click start race
                _ds4.SetButtonState(DualShock4Button.Cross, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Cross, false);
                _ds4.SubmitReport();
                Thread.Sleep(50);
            }
            else if (currentMenuState == MenuState.Race)
            {
                _ds4.SetButtonState(DualShock4Button.Options, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Options, false);
                _ds4.SubmitReport();
                Thread.Sleep(100);

                currentMenuState = MenuState.RacePaused;
            }

            if (currentMenuState == MenuState.RacePaused)
            {
                // smash the hell out of left dpad
                for (int i = 0; i < 5; i++)
                {
                    _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                }
                // then go right once
                _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                // and click to restart race
                _ds4.SetButtonState(DualShock4Button.Cross, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Cross, false);
                _ds4.SubmitReport();
                Thread.Sleep(50);
            }

            currentMenuState = MenuState.RaceStart;
            Thread.Sleep(7000+LoadTime);
            currentMenuState = MenuState.Race;

            bool ok = true;
            while (ok)
            {
                if (!connected)
                {
                    DisconnectController();
                    return;
                }

                if (currentPacket is null ||
                    currentPacket.CurrentLap <= 5)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                // if we're past the laps in the race, the race must have ended
                completedRaces += 1;
                currentMenuState = MenuState.RaceResult;
                Thread.Sleep(LoadTime);

                // reset all driver controls
                _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);

                // RaceResult: 1st (X), Table (X),
                for (int i = 0; i < 2; i++)
                {
                    _ds4.SetButtonState(DualShock4Button.Cross, true);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetButtonState(DualShock4Button.Cross, false);
                    _ds4.SubmitReport();
                    Thread.Sleep(1000);
                }
                // Load (Short),
                Thread.Sleep(3000);
                // Fanfare (X), Rewards (X X X)
                for (int i = 0; i < 4; i++)
                {
                    _ds4.SetButtonState(DualShock4Button.Cross, true);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetButtonState(DualShock4Button.Cross, false);
                    _ds4.SubmitReport();
                    Thread.Sleep(1000);
                }
                Thread.Sleep(LoadTime);

                currentMenuState = MenuState.Replay;
                // Replay: (O, X)
                _ds4.SetButtonState(DualShock4Button.Circle, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Circle, false);
                _ds4.SubmitReport();
                Thread.Sleep(200);
                _ds4.SetButtonState(DualShock4Button.Cross, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Cross, false);
                _ds4.SubmitReport();
                Thread.Sleep(LoadTime);

                currentMenuState = MenuState.PostRace;
                // PostRace: (dpad right, X)
                _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                _ds4.SubmitReport();
                Thread.Sleep(200);
                _ds4.SetButtonState(DualShock4Button.Cross, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Cross, false);
                _ds4.SubmitReport();
                Thread.Sleep(LoadTime);

                currentMenuState = MenuState.PreRace;
                // PreRace: X
                _ds4.SetButtonState(DualShock4Button.Cross, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Cross, false);
                _ds4.SubmitReport();

                // race countdown isn't OnTrack, so wait for at least it
                currentMenuState = MenuState.RaceStart;
                Thread.Sleep(7000);
                currentMenuState = MenuState.Race;
            }
        }

        private void DisconnectController()
        {
            if (_ds4 != null)
            {
                _ds4.Disconnect();
                _ds4 = null;
            }
        }
    }
}
