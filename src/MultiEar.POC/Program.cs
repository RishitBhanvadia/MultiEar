using System;
using System.Linq;
using System.Threading;
using MultiEar.Engine.Audio;
using MultiEar.Engine.Bluetooth;
using MultiEar.Engine.Services;

namespace MultiEar.POC
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  MultiEar POC — Powered by MultiEar.Engine");
            Console.WriteLine("=================================================\n");

            using var engine = new AudioEngineService();

            engine.FeedbackWarning += (s, msg) =>
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[Feedback Warning] {msg}");
                Console.ResetColor();
            };

            engine.ErrorOccurred += (s, err) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[Engine Error] {err}");
                Console.ResetColor();
            };

            var defaultDevice = engine.DefaultRenderDevice;
            if (defaultDevice == null)
            {
                Console.WriteLine("ERROR: No default Windows audio output device found.");
                return;
            }

            Console.WriteLine($"Default output device (loopback source): {defaultDevice.FriendlyName}");
            Console.WriteLine("  (Do NOT select this device as a destination — that would create a feedback loop)\n");

            var devices = engine.GetAvailableRenderDevices();
            Console.WriteLine("Available Audio Render Devices:");
            for (int i = 0; i < devices.Count; i++)
            {
                string marker = (devices[i].ID == defaultDevice.ID) ? " [LOOPBACK SOURCE]" : "";
                var codec = CodecDetector.DetectCodec(devices[i]);
                Console.WriteLine($"[{i}] {devices[i].FriendlyName} ({CodecLatencyTable.GetDisplayName(codec)}){marker}");
            }

            Console.WriteLine();
            Console.Write("Enter index for the first destination device: ");
            if (!int.TryParse(Console.ReadLine(), out int index1) || index1 < 0 || index1 >= devices.Count) return;

            Console.Write("Enter index for the second destination device: ");
            if (!int.TryParse(Console.ReadLine(), out int index2) || index2 < 0 || index2 >= devices.Count) return;

            if (index1 == index2)
            {
                Console.WriteLine("ERROR: Please select two distinct devices.");
                return;
            }

            Console.WriteLine("\nInitializing audio pipelines...");
            bool added1 = engine.AddDevice(devices[index1]);
            bool added2 = engine.AddDevice(devices[index2]);

            if (!added1 || !added2)
            {
                Console.WriteLine("Failed to add one or both devices. Exiting.");
                return;
            }

            engine.StartStreaming();
            Console.WriteLine("Audio routing started! Streaming system audio to both devices.\n");

            void PrintStatus()
            {
                Console.Write("\r");
                var pipes = engine.ActivePipelines;
                for (int i = 0; i < pipes.Count; i++)
                {
                    int volPct = (int)(pipes[i].VolumeLevel * 100);
                    Console.Write($"[Dev {i + 1}: {volPct,3}% | {pipes[i].Codec} | Delay: {pipes[i].RingBuffer.CapacityMs}ms]  ");
                }
                Console.Write("   ");
            }

            Console.WriteLine("Controls:");
            Console.WriteLine("  UP / DOWN Arrows    : Adjust Device 1 Sync (±5ms)");
            Console.WriteLine("  LEFT / RIGHT Arrows : Adjust Device 2 Sync (±5ms)");
            Console.WriteLine("  W / S               : Volume Up / Down for Device 1 (±10%)");
            Console.WriteLine("  A / D               : Volume Up / Down for Device 2 (±10%)");
            Console.WriteLine("  M                   : Mute / Unmute all devices");
            Console.WriteLine("  Q                   : Quit\n");

            PrintStatus();

            bool isMuted = false;
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q) break;

                    var pipes = engine.ActivePipelines;
                    if (pipes.Count >= 2)
                    {
                        bool syncChanged = false;
                        bool volumeChanged = false;

                        if (key == ConsoleKey.UpArrow) { pipes[0].ManualOffsetMs += 5; syncChanged = true; }
                        else if (key == ConsoleKey.DownArrow) { pipes[0].ManualOffsetMs -= 5; syncChanged = true; }
                        else if (key == ConsoleKey.RightArrow) { pipes[1].ManualOffsetMs += 5; syncChanged = true; }
                        else if (key == ConsoleKey.LeftArrow) { pipes[1].ManualOffsetMs -= 5; syncChanged = true; }
                        else if (key == ConsoleKey.W) { pipes[0].VolumeUp(); volumeChanged = true; }
                        else if (key == ConsoleKey.S) { pipes[0].VolumeDown(); volumeChanged = true; }
                        else if (key == ConsoleKey.D) { pipes[1].VolumeUp(); volumeChanged = true; }
                        else if (key == ConsoleKey.A) { pipes[1].VolumeDown(); volumeChanged = true; }
                        else if (key == ConsoleKey.M)
                        {
                            isMuted = !isMuted;
                            engine.SetGlobalMute(isMuted);
                            volumeChanged = true;
                        }

                        if (syncChanged) engine.RecalculateDelays();
                        if (volumeChanged || syncChanged) PrintStatus();
                    }
                }

                Thread.Sleep(50);
            }

            Console.WriteLine("\nStopping pipelines and saving session...");
            engine.StopStreaming();
            Console.WriteLine("Done.");
        }
    }
}
