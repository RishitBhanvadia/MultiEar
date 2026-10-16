# MultiEar — Complete Application Build Prompt
> Send this entire document to any AI coding assistant or developer to build the application from scratch.

---

## 1. Project mission

Build a Windows desktop application called **MultiEar** that allows a user to simultaneously stream audio from their laptop to **two or more Bluetooth earphones or headphones at the same time**, with zero per-app configuration, automatic latency synchronisation across devices, and a simple system-tray UI. The app must work on any Windows 10 or Windows 11 PC — it must not require special hardware such as a Copilot+ PC or Bluetooth LE Audio modems.

---

## 2. Background — why this is hard and why existing solutions fail

### 2.1 The core problem

Windows, by default, allows only one active audio output device at a time per application. Even though you can pair multiple Bluetooth devices, the OS audio engine routes PCM audio to a single endpoint. Trying to play sound through two Bluetooth headphones simultaneously is not natively supported on most hardware.

Microsoft introduced a feature called **Shared Audio** in Windows 11 Insider builds (first shipped via update KB5089573 in May 2026) that natively solves this problem — but only for Copilot+ PCs with Bluetooth LE Audio modems. The vast majority of Windows PCs will not have this feature available for years. MultiEar solves the same problem for every Windows PC manufactured in the last decade.

---

### 2.2 Existing Method 1 — Windows 11 Shared Audio (LE Audio)

**What it does:** Uses Bluetooth LE Audio broadcast technology (specifically the LC3 codec and Isochronous Channels standard) to transmit a single audio stream to two LE Audio-compatible Bluetooth devices simultaneously. After pairing two compatible devices, a "Shared audio (preview)" tile appears in the Quick Settings panel.

**Strengths:**
- Zero-configuration — appears natively in Quick Settings
- LC3 codec achieves very low latency (~20 ms) with excellent audio quality
- OS-level integration means all apps work automatically

**Critical flaws we must overcome:**
- Restricted to Copilot+ PCs with Qualcomm Snapdragon X processors initially; Intel Core Ultra 200 support added later but still very limited coverage
- Hard ceiling of exactly 2 devices — cannot extend to 3 or more
- Both devices must support Bluetooth LE Audio — classic Bluetooth earphones (the vast majority on the market) are incompatible
- Requires updated OEM Bluetooth drivers and firmware from the device manufacturer
- Windows 10 users are completely excluded

---

### 2.3 Existing Method 2 — Windows Stereo Mix loopback

**What it does:** Stereo Mix is a hidden Windows recording input that captures the system's own speaker output as a recordable source. By enabling it, you can route audio to a second playback device by setting Stereo Mix as the recording input and assigning the second device as the monitoring output in the Sound control panel.

**Strengths:**
- Completely free and already built into Windows
- No third-party software required
- Works on all Windows 10 and 11 PCs

**Critical flaws we must overcome:**
- Cannot simultaneously route to two pure Bluetooth devices — works best with at least one wired device
- Attempting to use two Bluetooth endpoints results in either the second device receiving no audio or the first device cutting out entirely
- No latency compensation whatsoever
- The loopback path introduces additional audio processing delay on top of Bluetooth codec delay
- Disabled by default on many systems and hidden from the device list; users must right-click > Show Disabled Devices in the Recording tab to find it

---

### 2.4 Existing Method 3 — VoiceMeeter Banana

**What it does:** VoiceMeeter Banana is a donationware virtual audio mixer application by VB-Audio. It installs a virtual audio device that appears to Windows apps as a speaker, then allows the user to route audio from that virtual device to up to three physical output buses (A1, A2, A3), each of which can be assigned to a Bluetooth endpoint.

**Strengths:**
- Can route to multiple physical Bluetooth outputs simultaneously
- Completely free (donationware)
- Powerful enough to be used by professional streamers and content creators
- Large community with many tutorials

**Critical flaws we must overcome:**
- Severe latency desync between Bluetooth devices: users have reported differences of up to 500 ms between two devices connected through VoiceMeeter (e.g., one earphone plays at ~100 ms offset, another at ~500 ms) depending on the active codec
- The desync occurs because VoiceMeeter applies a fixed WDM buffer to all outputs but each Bluetooth device introduces its own codec-dependent hardware delay on top of that, and VoiceMeeter has no mechanism to detect or compensate for per-device codec latency
- Setup requires configuring at least 10–30 settings correctly (WDM buffer size, engine mode, sample rate, driver type, input/output bus routing) — overwhelming for non-technical users
- Stability issues: VoiceMeeter is known to crash or produce audio glitches when the Bluetooth codec of a connected device changes (e.g., when a second device connects and negotiates SBC instead of aptX)
- Resource intensive: runs a persistent Windows service plus a driver that increases CPU usage even at idle
- Manual driver selection (WDM vs MME vs KS) affects latency unpredictably
- No auto-reconnect: if a Bluetooth device disconnects, VoiceMeeter silently fails and routing is lost until the user manually reconfigures

---

### 2.5 Existing Method 4 — VB-Cable virtual audio cable

**What it does:** VB-Cable is a free virtual audio cable by VB-Audio — it creates a virtual input/output pair so audio can be piped from one application to another. It is typically used together with VoiceMeeter to route audio.

**Strengths:**
- Very lightweight — minimal resource usage
- Works on all Windows versions
- Free to use

**Critical flaws we must overcome:**
- Not a standalone solution — requires VoiceMeeter or another audio mixer to route to Bluetooth devices
- Provides no latency correction at all
- No user interface of its own
- Installing VB-Cable changes the Windows audio graph in ways that can confuse other apps

---

### 2.6 Existing Method 5 — Raw WASAPI (Windows Audio Session API)

**What it does:** WASAPI is Microsoft's low-level audio API, part of the Core Audio API suite introduced in Windows Vista. It allows a developer to enumerate audio endpoints, open audio render clients, and submit PCM audio buffers directly to hardware with minimal overhead. In shared mode, the Windows Audio Engine mixes multiple clients together; in exclusive mode, the application takes direct control of the endpoint.

**Strengths:**
- Works on any Windows 10 or 11 PC regardless of Bluetooth modem generation
- Lowest achievable latency in managed code
- Exposes every audio endpoint the OS knows about, including all Bluetooth devices
- Full control over buffer sizing, sample rate, and channel layout
- Shared mode allows multiple applications to use the same device concurrently
- No ceiling on the number of endpoints that can be rendered to simultaneously

**Critical flaws we must overcome in the raw form:**
- Provides no graphical user interface whatsoever
- Has no built-in mechanism for latency synchronisation across endpoints — if you naively open two WASAPI render clients and push the same audio to both, each device plays at a different time offset depending on its codec
- Requires non-trivial C++ or C# development to implement correctly
- Does not automatically detect the active Bluetooth codec for each device
- No connection monitoring or auto-reconnect logic

**This is the API our application will be built on.** MultiEar's core job is to wrap WASAPI with the missing features: a virtual device, sync, codec awareness, a UI, and resilience.

---

## 3. Bluetooth audio codec latency reference

This table is critical for the latency compensation engine. The values below are the typical one-way encoding + transmission + decoding latency introduced by each codec. The compensator uses these as the baseline delay for each device; then the user can fine-tune with a manual offset slider.

| Codec | Typical latency | Notes |
|---|---|---|
| SBC (mandatory baseline) | 150–250 ms | Most common fallback; all Bluetooth earphones support SBC |
| AAC | 120–200 ms | Common on Apple devices and AirPods |
| aptX | 120–170 ms | Qualcomm codec; widely supported on mid-range Android earphones |
| aptX HD | 130–180 ms | Higher-quality aptX variant; similar latency |
| aptX LL (Low Latency) | 32–45 ms | Gaming-focused; extremely low latency |
| aptX Adaptive | 50–80 ms | Newer Qualcomm; varies by quality tier |
| LDAC | 80–130 ms | Sony's high-resolution codec; typically 3 quality tiers |
| LC3 (Bluetooth LE Audio) | 10–30 ms | The newest standard; very low latency |

**Sync rule:** The compensator finds the device with the highest codec latency in the active session, then delays all other devices by `(max_latency - device_latency)` milliseconds using per-device ring buffers. This ensures all physical speakers emit the same audio sample at the same moment.

---

## 4. The 8 problems MultiEar must solve

### Problem 1 — Hardware lock-in
**Flaw:** Microsoft's native Shared Audio requires Copilot+ PCs, locking out hundreds of millions of existing Windows users.
**Solution:** MultiEar is built entirely on WASAPI shared mode, which has been available since Windows Vista and works on any PC with a Bluetooth adapter, regardless of modem generation or OEM certification.

### Problem 2 — Device ceiling
**Flaw:** LE Audio broadcast supports exactly 2 devices simultaneously.
**Solution:** MultiEar's stream duplicator copies the PCM audio buffer into N independent output queues, one per device, with no architectural ceiling. The practical limit is the Bluetooth radio's bandwidth (typically 4–6 simultaneous A2DP connections on modern adapters).

### Problem 3 — Latency desync
**Flaw:** Different Bluetooth codecs introduce different hardware delays. Naively routing the same PCM stream to two devices produces audible echo because one earphone plays tens or hundreds of milliseconds behind the other.
**Solution:** A per-device latency compensator holds faster devices in a ring buffer delay, releasing audio samples at the correct moment to align all outputs. The codec for each device is detected automatically via the Windows Bluetooth HCI (Host Controller Interface) stack, and the corresponding baseline delay from the table in Section 3 is applied. The user can also manually trim the offset in the UI.

### Problem 4 — Complex setup
**Flaw:** VoiceMeeter requires users to navigate a dense, technical UI with WDM buffer settings, engine modes, driver selection, bus routing configuration, and sample rate negotiation.
**Solution:** MultiEar exposes a single wizard: "Add earphone → select from paired devices list → confirm." All technical parameters are set automatically. Advanced settings exist in a collapsible panel but are never required.

### Problem 5 — Stability
**Flaw:** VoiceMeeter is known to crash or produce glitches when a Bluetooth device renegotiates its codec — a common event when two devices connect and the adapter transitions between codec profiles.
**Solution:** The MultiEar audio engine runs in an isolated background thread with a supervisor watchdog. If the engine throws an unhandled exception (e.g., due to a device disappearing mid-stream), the watchdog catches it, tears down the affected render client cleanly, and restarts just that output pipeline within 500 ms — without interrupting other active devices or requiring any user action.

### Problem 6 — No per-device volume
**Flaw:** Basic multi-output solutions apply the same volume level to all outputs. Users who share audio with a friend cannot independently control each person's listening level.
**Solution:** Each Bluetooth output device in MultiEar has its own gain stage (a float multiplier applied to the PCM samples before the delay buffer) controlled by a dedicated volume slider in the UI. The range is 0–100% and the control responds in real-time without audio dropout.

### Problem 7 — No auto-reconnect
**Flaw:** When a Bluetooth device disconnects (battery dies, goes out of range, is turned off), audio routing to it silently fails. The user must manually remove and re-add the device to restore routing after reconnect.
**Solution:** The connection monitor polls each active device's Bluetooth connection state every 2 seconds. When a device disconnects, its output pipeline is paused and its WASAPI render client is closed cleanly. When the device reconnects, the monitor detects the state change, reopens the render client, and resumes audio routing automatically — the user sees only a brief status indicator change in the system tray UI.

### Problem 8 — Codec blindness
**Flaw:** No existing multi-output solution reads which Bluetooth codec is actually active for each device. VoiceMeeter applies the same fixed buffer to all outputs, producing variable and uncontrolled latency differences.
**Solution:** MultiEar queries the Windows Bluetooth HCI stack to determine the active codec for each device when it connects and whenever a codec renegotiation event is detected. The result is displayed as a badge in the UI and used to set the baseline delay in the latency compensator. If HCI codec query fails (older drivers), the app falls back to manual calibration mode and prompts the user to adjust the offset slider while listening to a sync test tone.

---

## 5. Full application architecture

### 5.1 Architecture overview

```
┌────────────────────────────────────────────────────────────────────────────┐
│  UI Layer: system tray icon + floating control panel (WinUI 3)             │
│  • Add/remove device buttons                                               │
│  • Per-device: codec badge, latency offset spinner, volume slider, status  │
│  • Global: master mute, settings gear, auto-launch toggle                  │
└──────────────────────────────┬─────────────────────────────────────────────┘
                               │ user commands
┌──────────────────────────────▼─────────────────────────────────────────────┐
│  Virtual WASAPI Endpoint — "MultiEar Speaker"                              │
│  Registered as a standard Windows audio render device via                  │
│  IMMDeviceEnumerator. All applications (Spotify, Chrome, games, etc.)      │
│  see this as their default speaker and route PCM audio into it.            │
│  Sample rate: 48 000 Hz stereo 32-bit float (negotiated with loopback)    │
└──────────────────────────────┬─────────────────────────────────────────────┘
                               │ raw PCM audio stream (48 kHz, stereo, f32)
┌─────────────────┬────────────▼─────────────────┬──────────────────────────┐
│  Stream         │  Codec detector               │  Latency compensator     │
│  duplicator     │  • Queries HCI per device     │  • Ring buffer per device│
│  • Copies PCM   │  • Maps codec → latency ms   │  • Holds faster devices  │
│    to N queues  │  • Fires codec-changed event  │  • Emits samples in sync │
└────────┬────────┴───────────────────────────────┴────────────┬─────────────┘
         │ N synchronised PCM streams                          │
┌────────▼────────────────────────────────────────────────────▼─────────────┐
│  Device manager                                                            │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────────────────┐  │
│  │ Device          │  │ Connection      │  │ Auto-reconnect handler   │  │
│  │ enumerator      │  │ monitor         │  │ Watches reconnect events │  │
│  │ Windows.Devices │  │ Polls state     │  │ Re-opens render client   │  │
│  │ .Bluetooth      │  │ every 2 s       │  │ within 500 ms            │  │
│  └─────────────────┘  └─────────────────┘  └──────────────────────────┘  │
└────────────────────────────────┬───────────────────────────────────────────┘
                                 │
         ┌───────────────────────┼────────────────────────┐
         ▼                       ▼                        ▼
  ┌─────────────┐        ┌─────────────┐          ┌─────────────┐
  │ BT Device 1 │        │ BT Device 2 │   ...    │ BT Device N │
  │ (WASAPI     │        │ (WASAPI     │          │ (WASAPI     │
  │  render     │        │  render     │          │  render     │
  │  client)    │        │  client)    │          │  client)    │
  └─────────────┘        └─────────────┘          └─────────────┘
```

---

### 5.2 Layer 0 — UI layer (WinUI 3)

**System tray icon:**
- Appears in the Windows system tray when MultiEar is running
- Left-click: opens/closes the floating control panel
- Right-click: context menu with "Mute all", "Settings", "About", "Exit"
- Icon state changes: solid headphones = active, dimmed = no devices, red dot overlay = error

**Floating control panel:**
- Compact window, approximately 320 px wide, no fixed height (grows with device list)
- Does not appear in the taskbar; only accessible via tray icon
- Sections:
  - Header: app name, version, global mute toggle
  - Device cards (one per active BT device — see Section 7.3)
  - "Add earphone" button at the bottom — opens device picker
  - Settings link (opens separate settings window)

**Settings window:**
- Launch at startup toggle
- Default codec latency overrides (table of codec → ms, all editable)
- Manual sync calibration: plays a test tone to both devices simultaneously so user can hear and adjust with a seek bar
- Latency calibration mode: plays a sync test tone and lets user manually trim per-device offset using a slider while listening
- Diagnostics: shows active WASAPI format, buffer size, current ring buffer depth per device
- Audio format override: allow user to force 44 100 Hz vs 48 000 Hz if a specific device has issues

---

### 5.3 Layer 1 — Virtual WASAPI audio endpoint

This is the most technically complex component. The goal is to create a software-defined audio device that Windows registers as a real speaker, so every application on the system routes audio to it without any configuration change.

**Implementation path:**

Option A (recommended for initial release — no kernel driver required): Use **WASAPI loopback capture** on the actual default output device. MultiEar sets a real Bluetooth device (or the laptop's built-in speakers) as the Windows default output, then opens a loopback capture client on it using `AUDCLNT_STREAMFLAGS_LOOPBACK`. This captures a copy of whatever audio the OS is playing and feeds it into MultiEar's stream duplicator. The user experience is: "set MultiEar as the audio manager, it captures system audio and routes to your BT devices."

This approach requires zero kernel driver development and works with the standard WASAPI shared mode API available in .NET via NAudio.

Option B (ideal long-term — full virtual driver): Implement a Windows Audio Service plugin using the PortAudio or Audio Driver (AVStream miniport) model to register a true virtual render endpoint. This is significantly more complex and requires a kernel-mode driver with WHQL signing. This should be a Phase 4 goal, not Phase 1.

**For Phase 1 through Phase 3, use Option A (loopback capture).**

WASAPI loopback capture implementation notes:
- Open `IMMDeviceEnumerator`, enumerate the current default render device
- Open a loopback capture client: `IAudioClient::Initialize` with `AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK`
- Register an event handle for buffer-ready notifications
- In the capture callback, read available frames via `IAudioCaptureClient::GetBuffer`
- Pass each buffer of PCM frames to the stream duplicator
- Release the buffer with `IAudioCaptureClient::ReleaseBuffer`
- Use `AUDCLNT_SHAREMODE_SHARED` — do not use exclusive mode; it would block all other apps

---

### 5.4 Layer 2 — Core audio engine

#### 5.4.1 Stream duplicator

The stream duplicator receives a PCM buffer (array of float32 stereo frames at 48 000 Hz) from the loopback capture and creates N independent copies, one per active output device.

```
StreamDuplicator.OnBuffer(float[] pcmBuffer, int frameCount):
  foreach (DeviceOutputPipeline pipeline in activePipelines):
    pipeline.ApplyGain(pcmBuffer, frameCount, pipeline.VolumeLevel)
    pipeline.LatencyCompensator.Enqueue(pcmBuffer, frameCount)
```

Each pipeline is independent. A crash or stall in one pipeline must not affect others — each pipeline runs on its own dedicated thread with exception isolation.

**Thread model:**
- One capture thread: reads from WASAPI loopback capture event callback
- One output thread per device: reads from that device's compensator ring buffer and pushes to the device's WASAPI render client
- One monitor thread: polls connection state for all devices every 2 000 ms
- One watchdog thread: checks if any output thread has been stalled for more than 1 000 ms and restarts it if so

**Gain stage:** Before enqueuing, apply a per-device linear gain multiplier:
```csharp
for (int i = 0; i < frameCount * 2; i++) // stereo = 2 channels
    outputBuffer[i] = inputBuffer[i] * volumeMultiplier; // 0.0f to 1.0f
```
This must be applied before the delay buffer so that volume changes do not affect sync timing.

#### 5.4.2 Codec detector

The codec detector determines which Bluetooth audio codec is currently active between the PC and each connected Bluetooth audio device.

**Windows API path:**
1. Use `Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceFromIdAsync()` to get the BluetoothDevice object for each paired device
2. Query the device's HCI (Host Controller Interface) via the undocumented but accessible `DeviceAccessInformation` and Bluetooth radio APIs
3. Alternatively, query the registry key `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{36fc9e60-c465-11cf-8056-444553540000}` — some Bluetooth drivers write the active codec profile here
4. For Intel Bluetooth adapters, the Intel Wireless Bluetooth driver exposes codec information via WMI (Windows Management Instrumentation)
5. For Qualcomm adapters, use the Qualcomm aptX registry key or WMI extension

**Fallback behavior:** If codec detection fails (older or third-party Bluetooth drivers do not expose this information), the app defaults to SBC (the mandatory fallback codec, highest latency) for the unknown device and displays a "codec unknown — using SBC estimate" warning badge in the UI. The user can override via the settings manual offset.

**Codec change detection:** Subscribe to `DeviceInformation.Updated` events and re-query the codec when a device property changes. Also re-run detection when a device reconnects.

**Output:** A `BluetoothCodec` enum value per device: `SBC | AAC | AptX | AptXHD | AptXLL | AptXAdaptive | LDAC | LC3 | Unknown`

#### 5.4.3 Latency compensator

The latency compensator is a per-device ring buffer that introduces a calibrated delay to align audio output across all active devices.

**Algorithm:**

```
OnDeviceAdded(device):
  device.codecLatencyMs = CodecDetector.GetLatencyMs(device.codec)
  RecalculateDelays()

RecalculateDelays():
  maxLatency = activePipelines.Max(p => p.codecLatencyMs)
  foreach pipeline in activePipelines:
    pipeline.delayMs = maxLatency - pipeline.codecLatencyMs + pipeline.manualOffsetMs

RingBuffer implementation:
  bufferCapacityFrames = SampleRate * maxDelayMs / 1000   // 48000 * 500 / 1000 = 24000 frames max
  writePosition = 0
  readPosition  = 0
  readPosition starts at writePosition - (delayMs * SampleRate / 1000) frames behind

Enqueue(float[] pcm, int frameCount):
  write frames into ring buffer at writePosition, advance writePosition

GetNextFrames(int frameCount) → float[]:
  if (frames available at readPosition >= frameCount):
    read frameCount frames from readPosition, advance readPosition
    return frames
  else:
    return silence (zero-filled buffer) until frames become available
```

The maximum supported delay is 500 ms (covers SBC at its slowest). The ring buffer is pre-allocated at startup to avoid garbage collection pressure during audio playback.

**Manual trim:** The UI exposes a ±100 ms trim slider per device. `manualOffsetMs` is added to the computed delay. Default is 0. This allows fine-tuning when automatic codec detection is inaccurate.

---

### 5.5 Layer 3 — Device manager

#### 5.5.1 Device enumerator

On startup and whenever the user opens the "Add earphone" dialog, the enumerator queries all Bluetooth devices that are:
- Currently paired with the PC
- Of type AudioSink (A2DP profile)

```csharp
var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
var devices  = await DeviceInformation.FindAllAsync(selector);
// Filter: only devices with A2DP/AudioSink service
foreach (var d in devices):
  var btDevice = await BluetoothDevice.GetDeviceFromIdAsync(d.Id);
  var rfcServices = await btDevice.GetRfcommServicesForIdAsync(
      RfcommServiceId.AudioSink);
  if (rfcServices.Services.Count > 0) → include in picker list
```

Display in the picker: device name, paired status indicator, signal strength if available, current codec if already connected.

#### 5.5.2 Connection monitor

Runs on a dedicated background thread. Every 2 000 ms, checks `BluetoothDevice.ConnectionStatus` for each active device. When a disconnection is detected:
1. Sets the device's pipeline status to `Disconnected`
2. Closes the WASAPI render client for that device cleanly (`IAudioClient.Stop()`, then `IAudioClient.Reset()`)
3. Updates the UI status badge for that device card to "Disconnected"
4. Logs the event with timestamp

When a reconnection is detected:
1. Waits 800 ms for the Bluetooth A2DP profile to fully negotiate before opening the render client (avoids "device not ready" errors)
2. Re-detects the active codec (it may have changed)
3. Recalculates all delays (the new codec latency may shift the sync baseline)
4. Re-opens the WASAPI render client and resumes the output thread
5. Updates the UI status badge to "Connected"

#### 5.5.3 Auto-reconnect

The auto-reconnect handler subscribes to `DeviceWatcher.Updated` and `DeviceWatcher.Added` events from `DeviceInformation.CreateWatcher`. When a device that was previously active reappears in the device watcher:
1. Checks if the device ID matches any device in the active session that is currently marked `Disconnected`
2. If yes, triggers the reconnect sequence described above
3. If no (brand-new device), ignores it (user must explicitly add new devices via the UI)

---

### 5.6 Layer 4 — WASAPI render clients (per device)

Each Bluetooth audio device gets its own WASAPI render client pipeline:

```csharp
// Open the endpoint
IMMDeviceEnumerator enumerator = ...;
IMMDevice device = enumerator.GetDevice(deviceId); // BT device WASAPI ID
IAudioClient audioClient = device.Activate<IAudioClient>();

// Negotiate format: 48000 Hz, stereo, 32-bit float
WAVEFORMATEXTENSIBLE format = new() {
    Format.wFormatTag     = WAVE_FORMAT_EXTENSIBLE,
    Format.nSamplesPerSec = 48000,
    Format.nChannels      = 2,
    Format.wBitsPerSample = 32,
    SubFormat             = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
};

// Initialise in shared mode with event callback
audioClient.Initialize(
    AUDCLNT_SHAREMODE_SHARED,
    AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
    bufferDurationHns: 10_000_000, // 1 second in 100-ns units
    periodicity:       0,
    format,
    sessionGuid:       Guid.Empty);

// Register buffer-ready event
audioClient.SetEventHandle(bufferReadyEvent);

// Get render client
IAudioRenderClient renderClient = audioClient.GetService<IAudioRenderClient>();

// Start streaming
audioClient.Start();

// Render loop (runs on dedicated output thread per device):
while (running):
    WaitForSingleObject(bufferReadyEvent, 500);
    uint padding    = audioClient.GetCurrentPadding();
    uint available  = bufferSize - padding;
    if (available > 0):
        byte* data = renderClient.GetBuffer(available);
        Fill(data, LatencyCompensator.GetNextFrames(available));
        renderClient.ReleaseBuffer(available, 0);
```

**Format negotiation fallback:** If the device does not support 32-bit float at 48 000 Hz, call `IAudioClient.IsFormatSupported` with `AUDCLNT_SHAREMODE_SHARED` and use the returned `closestMatch` format. Convert sample rate using a simple linear interpolation resampler if necessary.

**Error handling per render client:**
- `AUDCLNT_E_DEVICE_INVALIDATED`: device disconnected → trigger monitor's disconnect handler
- `AUDCLNT_E_BUFFER_TOO_LARGE`: reduce buffer request size by half and retry
- `AUDCLNT_E_UNSUPPORTED_FORMAT`: fall back to 16-bit PCM at 44 100 Hz

---

## 6. Recommended tech stack

| Component | Technology | Rationale |
|---|---|---|
| Language | C# 12, .NET 8 | Excellent Windows API coverage, P/Invoke for low-level Win32, managed memory reduces crash risk, fast enough for 48 kHz PCM processing |
| Audio capture & render | NAudio 2.x (NuGet) | Wraps WASAPI cleanly in managed code; provides `WasapiLoopbackCapture`, `WasapiOut`, and `MMDeviceEnumerator` |
| Bluetooth enumeration | Windows.Devices.Bluetooth (WinRT API, available in .NET 8 via NuGet) | Native OS API for pairing status, connection state, and service discovery |
| Bluetooth extended | 32feet.NET (NuGet) | Supplements WinRT API for lower-level HCI queries on older Bluetooth stacks |
| UI framework | WinUI 3 via WinApp SDK 1.5+ | Native Windows look, system tray icon (`Microsoft.WindowsAppSDK` package), Fluent controls, modern XAML |
| System tray | `Microsoft.WindowsAppSDK.Tray` or custom `NativeWindow` P/Invoke | WinUI 3 does not natively support system tray icons in all versions — use Win32 Shell_NotifyIcon via P/Invoke as fallback |
| Installer | MSIX (Visual Studio packaging project) | Signed installer, auto-update support via Microsoft Store or winget |
| Logging | Serilog (NuGet) with file sink | Structured logging to `%APPDATA%\MultiEar\logs\` for diagnostics |
| Testing | xUnit + Moq | Unit tests for latency compensator math, codec detection mapping, and ring buffer logic |

---

## 7. Detailed feature requirements

### 7.1 Core audio features (must-have for v1.0)

- F-01: Capture system audio via WASAPI loopback from the current default output device
- F-02: Stream captured audio to 2 or more simultaneously active Bluetooth A2DP devices
- F-03: Per-device latency compensation using the ring buffer algorithm described in Section 5.4.3
- F-04: Automatic codec detection for each device; display codec name and estimated latency in UI
- F-05: Per-device volume control (0–100%, applied as linear gain multiplier before delay buffer)
- F-06: Global mute: suspend audio to all devices without disconnecting them
- F-07: Automatic reconnect when a device drops and comes back within the same session

### 7.2 Device management features (must-have for v1.0)

- F-08: Device picker dialog showing all paired Bluetooth AudioSink devices
- F-09: Add and remove devices from the active session without restarting the app
- F-10: Persistent session: save the list of active devices to disk; auto-connect them on next launch
- F-11: Per-device connection status badge: Connected (green) / Connecting (amber) / Disconnected (red)
- F-12: Show device name and Bluetooth hardware address in device card

### 7.3 UI features (must-have for v1.0)

- F-13: System tray icon with connection status indication
- F-14: Floating control panel with device cards
- F-15: Each device card shows: device name, codec badge, latency ms badge, volume slider, status indicator, remove button
- F-16: "Add earphone" button opens the device picker
- F-17: Settings window with codec latency overrides and manual calibration

### 7.4 Quality and resilience features (must-have for v1.0)

- F-18: Watchdog thread restarts crashed output pipeline within 500 ms
- F-19: All pipelines are independent — one device crashing does not affect others
- F-20: Graceful shutdown: stop all render clients cleanly before exit; no audio glitch on close
- F-21: On codec change detection, recalculate delays and briefly mute (< 50 ms) to avoid click artifacts

### 7.5 Nice-to-have features (target v1.1+)

- F-22: Sync calibration wizard: plays a test tone ("1-2-3 sync tone") through all devices and lets user trim offset with a slider
- F-23: Codec auto-calibration: record perceived latency via the loopback and automatically fine-tune per-device offset
- F-24: Multi-source routing: allow different apps to route to different BT devices (e.g., music to earphone 1, game audio to earphone 2)
- F-25: EQ per device: simple 5-band equaliser applied per output stream
- F-26: Battery level display for devices that expose it via GATT profiles
- F-27: Windows Startup shortcut installer (run at login)

---

## 8. UI/UX specification

### 8.1 Control panel layout (320 px wide)

```
┌─────────────────────────────────┐
│  🎧 MultiEar           [Mute ⚡] │  ← Header, 40 px tall
├─────────────────────────────────┤
│  [Device card — see 8.2]        │
│  [Device card]                  │
│  [Device card]                  │
├─────────────────────────────────┤
│  [+ Add earphone]  [⚙ Settings] │  ← Footer, 40 px tall
└─────────────────────────────────┘
```

### 8.2 Device card layout (per device)

```
┌─────────────────────────────────┐
│  ● Sony WH-1000XM5     [✕]     │  ← Name + status dot + remove button
│  [LDAC] [80 ms]                 │  ← Codec badge + latency badge
│  Vol: ███████░░░  70%           │  ← Volume slider
└─────────────────────────────────┘
```

- Status dot: green = streaming, amber = reconnecting, red = disconnected
- Codec badge: pill with codec name (SBC / AAC / aptX / LDAC / LC3 etc.)
- Latency badge: auto-computed ms value from codec table (grey if manually overridden, blue if auto-detected)
- Volume slider: horizontal, 0–100%, real-time gain adjustment
- Remove button (✕): removes device from session immediately, closes its render client cleanly

### 8.3 Device picker dialog

```
┌──────────────────────────────────────────┐
│  Select earphone to add                  │
│  ─────────────────────────────────────── │
│  ● OnePlus Buds Pro 2     [SBC]  Add →  │
│  ● JBL Tune 130NC         [AAC]  Add →  │
│  ● Realme Buds Air 5      [SBC]  Add →  │
│  ─────────────────────────────────────── │
│  Only showing paired Bluetooth earphones │
│                              [Cancel]    │
└──────────────────────────────────────────┘
```

- Lists only paired devices with A2DP AudioSink profile
- Shows currently active codec if device is already connected
- Devices already in the active session are shown greyed out with "Active" label instead of "Add →"

---

## 9. Implementation phases

### Phase 1 — Proof of concept (target: working audio duplication)

**Goal:** Get PCM audio flowing from the system to two Bluetooth devices simultaneously, even without sync.

Steps:
1. Create a new C# .NET 8 console application
2. Install NAudio: `dotnet add package NAudio`
3. Use `WasapiLoopbackCapture` to capture system audio from the default output device
4. Enumerate all Bluetooth audio endpoints using `MMDeviceEnumerator` (filter for Bluetooth render devices)
5. Open two `WasapiOut` instances pointing to two different Bluetooth device IDs
6. In the capture `DataAvailable` callback, write the captured buffer to both `WasapiOut` instances
7. Verify: play music on the PC — both earphones should produce audio (out of sync is expected at this stage)
8. Acceptance criteria: audio plays on both devices without dropout for at least 60 seconds

### Phase 2 — Sync engine (target: latency-compensated audio)

**Goal:** Make both earphones play the same audio sample at the same physical moment.

Steps:
1. Implement the `RingBuffer<float>` class — circular buffer with configurable capacity in frames
2. Implement the `LatencyCompensator` class — wraps a ring buffer, exposes `Enqueue(float[], int)` and `GetNextFrames(int) → float[]`
3. Implement `CodecLatencyTable` — static dictionary mapping `BluetoothCodec` enum → int (latency in ms), using the values from Section 3
4. Implement codec detection (start with registry-based detection; fall back to SBC default)
5. Implement `RecalculateDelays()` — finds max latency across all active devices, sets each device's compensator delay accordingly
6. Thread the output pipelines: replace the direct `WasapiOut` writes with per-device output threads reading from their compensators
7. Test: play music to two devices with known different codecs — audio should be perceptibly synchronised
8. Acceptance criteria: with earphones of different codecs, the sync error must be < 30 ms (imperceptible to human hearing)

### Phase 3 — Device manager and resilience (target: stable long-running service)

**Goal:** Handle device disconnects, reconnects, and multiple devices reliably.

Steps:
1. Implement `ConnectionMonitor` — polling loop every 2 s, fires `DeviceDisconnected` and `DeviceReconnected` events
2. Implement `WatchdogThread` — monitors output thread health, restarts stalled threads
3. Implement `AutoReconnectHandler` — subscribes to `DeviceWatcher`, triggers reconnect sequence on matching device reappearance
4. Implement graceful shutdown — `CancellationToken` propagated to all threads, `IAudioClient.Stop()` called on all render clients on exit
5. Implement session persistence — save active device list to `%APPDATA%\MultiEar\session.json` on change; restore on launch
6. Test: disconnect and reconnect one earphone mid-playback — audio should resume within 2 seconds without user action
7. Acceptance criteria: 30-minute continuous playback test with deliberate disconnects must produce zero permanent failures

### Phase 4 — UI and shipping (target: usable application)

**Goal:** Wrap the engine in a WinUI 3 application with system tray support.

Steps:
1. Create a WinUI 3 application project (WinApp SDK 1.5+)
2. Implement system tray icon via Win32 Shell_NotifyIcon P/Invoke (WinUI 3 does not have native tray support yet)
3. Build the floating control panel XAML with device card template (see Section 8)
4. Bind device cards to an `ObservableCollection<DeviceViewModel>` — updates from connection monitor flow into the UI via `DispatcherQueue.TryEnqueue`
5. Implement per-device volume slider binding → gain stage in the audio pipeline
6. Implement the "Add earphone" device picker dialog
7. Implement the settings window with codec latency override table and manual calibration
8. Build MSIX installer
9. Acceptance criteria: non-technical user can add two earphones and start listening within 60 seconds of first launch

---

## 10. Key algorithms — detailed pseudocode

### 10.1 Ring buffer (lock-free circular buffer for audio)

```csharp
public class AudioRingBuffer {
    private readonly float[] buffer;
    private readonly int     capacity;     // in frames (stereo = 2 floats per frame)
    private int              writePos = 0;
    private int              readPos  = 0;
    private int              available = 0; // frames currently in buffer

    public AudioRingBuffer(int capacityFrames) {
        capacity = capacityFrames;
        buffer   = new float[capacityFrames * 2]; // stereo
    }

    public void Write(float[] samples, int frameCount) {
        // samples.Length == frameCount * 2
        for (int i = 0; i < frameCount * 2; i++) {
            buffer[(writePos * 2 + i) % buffer.Length] = samples[i];
        }
        writePos  = (writePos + frameCount) % capacity;
        available += frameCount;
    }

    public int Read(float[] output, int frameCount) {
        int toRead = Math.Min(frameCount, available);
        for (int i = 0; i < toRead * 2; i++) {
            output[i] = buffer[(readPos * 2 + i) % buffer.Length];
        }
        readPos   = (readPos + toRead) % capacity;
        available -= toRead;
        // Zero-fill remainder if not enough data
        if (toRead < frameCount)
            Array.Clear(output, toRead * 2, (frameCount - toRead) * 2);
        return toRead;
    }
}
```

### 10.2 Delay initialisation

```csharp
void RecalculateDelays(List<DevicePipeline> pipelines) {
    int maxLatency = pipelines.Max(p => p.CodecLatencyMs);
    foreach (var p in pipelines) {
        int delayMs     = maxLatency - p.CodecLatencyMs + p.ManualTrimMs;
        int delayFrames = (int)(48000.0 * delayMs / 1000.0); // at 48 kHz
        p.RingBuffer.SetReadOffset(delayFrames);
        // Pre-fill buffer with silence equal to the delay
        p.RingBuffer.PreFillSilence(delayFrames);
    }
}
```

### 10.3 Codec detection (registry + fallback)

```csharp
BluetoothCodec DetectCodec(string bluetoothAddress) {
    // Attempt 1: Intel Bluetooth driver registry path
    string regPath = $@"SYSTEM\CurrentControlSet\Services\BthA2dp\Parameters\{bluetoothAddress}";
    using var key = Registry.LocalMachine.OpenSubKey(regPath);
    if (key != null) {
        string codecName = key.GetValue("ActiveCodec")?.ToString();
        if (codecName != null) return ParseCodecString(codecName);
    }
    // Attempt 2: Qualcomm aptX registry
    string qcomPath = $@"SOFTWARE\Qualcomm\AptX\{bluetoothAddress}";
    using var qkey = Registry.LocalMachine.OpenSubKey(qcomPath);
    if (qkey != null) {
        int aptxActive = (int)(qkey.GetValue("AptXActive") ?? 0);
        if (aptxActive == 1) return BluetoothCodec.AptX;
    }
    // Attempt 3: WMI Bluetooth class
    // ... ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%Bluetooth%'")
    // Attempt 4: Default — assume SBC (universal fallback)
    return BluetoothCodec.SBC;
}
```

### 10.4 Output thread loop

```csharp
void OutputThreadMain(DevicePipeline pipeline, CancellationToken ct) {
    while (!ct.IsCancellationRequested) {
        try {
            WaitHandle.WaitAny(new[] { pipeline.BufferReadyEvent, ct.WaitHandle }, 500);
            if (ct.IsCancellationRequested) break;
            if (pipeline.Status != PipelineStatus.Streaming) { Thread.Sleep(50); continue; }

            uint padding   = pipeline.AudioClient.GetCurrentPadding();
            uint available = pipeline.BufferSize - padding;
            if (available == 0) continue;

            float[] frames  = new float[available * 2]; // stereo
            pipeline.RingBuffer.Read(frames, (int)available);

            unsafe {
                byte* data = pipeline.RenderClient.GetBuffer(available);
                fixed (float* src = frames)
                    Buffer.MemoryCopy(src, data, available * 8, available * 8);
                pipeline.RenderClient.ReleaseBuffer(available, 0);
            }
        }
        catch (AudioClientException ex) when (ex.HResult == AUDCLNT_E_DEVICE_INVALIDATED) {
            pipeline.Status = PipelineStatus.Disconnected;
            pipeline.Watchdog.SignalDeviceInvalidated(pipeline.DeviceId);
            break;
        }
        catch (Exception ex) {
            pipeline.Logger.Error(ex, "Output thread error for {DeviceId}", pipeline.DeviceId);
            pipeline.Watchdog.SignalThreadCrash(pipeline.DeviceId);
            break;
        }
    }
}
```

---

## 11. Error handling requirements

| Error condition | Detection method | Recovery action |
|---|---|---|
| Bluetooth device disconnects mid-stream | `AUDCLNT_E_DEVICE_INVALIDATED` from render client | Close render client; set status Disconnected; start auto-reconnect watcher |
| Device codec changes (A2DP renegotiation) | `DeviceInformation.Updated` event | Brief 50 ms mute; re-detect codec; recalculate all delays; resume |
| Ring buffer underrun (pipeline too slow) | `available == 0` at render client | Emit silence; log underrun; increment underrun counter (expose in diagnostics) |
| Output thread stall (no buffer writes for > 1 s) | Watchdog heartbeat timeout | Terminate thread via CancellationToken; restart it; reset render client |
| WASAPI format negotiation failure | `IsFormatSupported` returns false | Fall back to device's preferred format; resample if needed |
| All Bluetooth devices disconnect | Connection monitor | Display "no active devices" state in UI; keep loopback capture running; auto-reconnect will restore |
| Loopback capture device changes (user switches default speaker) | `IMMNotificationClient.OnDefaultDeviceChanged` | Re-open loopback capture on the new default device; brief mute of < 100 ms |
| Unhandled exception in engine thread | Global thread exception handler | Log full stack trace to file; attempt clean restart of affected pipeline |

---

## 12. Testing requirements

### 12.1 Unit tests

- `RingBufferTests`: write N frames, read N frames, verify correct values. Write > capacity, verify circular wrap. Read with underrun, verify silence padding.
- `LatencyCompensatorTests`: add 3 devices with different codecs, verify computed delays are correct (max − own). Change codec on one device, verify delays recalculate.
- `CodecLatencyTableTests`: verify all 8 codec entries return values within documented range.
- `GainStageTests`: apply 0.5 multiplier to a buffer, verify all samples halved. Apply 0.0, verify silence. Apply 1.0, verify passthrough.

### 12.2 Integration tests

- Open loopback capture with NAudio, verify `DataAvailable` fires at expected rate (~10 ms intervals at 48 kHz with 480-frame buffers).
- Open two WASAPI render clients on two different audio endpoints (can be virtual endpoints for testing), push the same buffer to both, verify no exceptions.
- Simulate device disconnect by disabling a render endpoint via policy; verify pipeline status changes to Disconnected within 2 s.
- Re-enable the endpoint; verify pipeline auto-reconnects within 3 s.

### 12.3 Manual acceptance tests

- T-01: Play a movie through Spotify on two earphones; verify audio is perceptibly synchronised (< 30 ms perceived offset).
- T-02: Adjust volume slider for one earphone to 50%; verify the other earphone is unaffected.
- T-03: Turn off one earphone mid-playback; verify the other continues without interruption; turn it back on; verify it rejoins within 3 s.
- T-04: Add a third earphone; verify all three play synchronised audio.
- T-05: Close the app; reopen it; verify the same earphones reconnect automatically.
- T-06: Switch the Windows default audio device to something other than the loopback source; verify MultiEar adapts within 1 s.
- T-07: Run for 60 minutes continuously; verify no audio dropout or memory growth > 20 MB.

---

## 13. File and project structure

```
MultiEar/
├── MultiEar.sln
├── src/
│   ├── MultiEar.Engine/                  # Class library — audio engine
│   │   ├── Audio/
│   │   │   ├── LoopbackCapture.cs        # WASAPI loopback capture wrapper
│   │   │   ├── StreamDuplicator.cs       # Copies PCM to N pipelines
│   │   │   ├── AudioRingBuffer.cs        # Lock-free circular buffer
│   │   │   ├── LatencyCompensator.cs     # Per-device delay manager
│   │   │   ├── GainStage.cs              # Per-device volume multiplier
│   │   │   └── WasapiRenderPipeline.cs   # WASAPI render client wrapper
│   │   ├── Bluetooth/
│   │   │   ├── BluetoothDeviceEnumerator.cs
│   │   │   ├── CodecDetector.cs
│   │   │   ├── ConnectionMonitor.cs
│   │   │   ├── AutoReconnectHandler.cs
│   │   │   └── BluetoothCodec.cs         # Enum + latency table
│   │   ├── Models/
│   │   │   ├── DeviceSession.cs          # Runtime state of a device
│   │   │   └── SessionConfig.cs          # Persisted configuration
│   │   └── Watchdog.cs                   # Thread health monitor
│   ├── MultiEar.UI/                      # WinUI 3 application
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── TrayIcon.cs                   # Shell_NotifyIcon P/Invoke wrapper
│   │   ├── Views/
│   │   │   ├── ControlPanel.xaml         # Floating tray panel
│   │   │   ├── DevicePickerDialog.xaml
│   │   │   └── SettingsWindow.xaml
│   │   ├── ViewModels/
│   │   │   ├── ControlPanelViewModel.cs
│   │   │   ├── DeviceCardViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   └── Package.appxmanifest
│   └── MultiEar.Installer/               # MSIX packaging project
└── tests/
    ├── MultiEar.Engine.Tests/
    │   ├── RingBufferTests.cs
    │   ├── LatencyCompensatorTests.cs
    │   └── GainStageTests.cs
    └── MultiEar.Integration.Tests/
        └── PipelineIntegrationTests.cs
```

---

## 14. Configuration and persistence

Save session state to `%APPDATA%\MultiEar\session.json`:

```json
{
  "activeDevices": [
    {
      "deviceId": "BluetoothLE#BluetoothLE...",
      "friendlyName": "Sony WH-1000XM5",
      "bluetoothAddress": "AA:BB:CC:DD:EE:FF",
      "volumeLevel": 0.85,
      "manualOffsetMs": 0,
      "codecOverride": null
    },
    {
      "deviceId": "BluetoothLE#BluetoothLE...",
      "friendlyName": "OnePlus Buds Pro 2",
      "bluetoothAddress": "11:22:33:44:55:66",
      "volumeLevel": 1.0,
      "manualOffsetMs": -10,
      "codecOverride": null
    }
  ],
  "codecLatencyOverrides": {
    "SBC":        200,
    "AAC":        160,
    "AptX":       150,
    "AptXHD":     155,
    "AptXLL":     40,
    "AptXAdaptive": 65,
    "LDAC":       100,
    "LC3":        20
  },
  "launchAtStartup": true,
  "globalMuted": false
}
```

---

## 15. Minimum system requirements

| Requirement | Minimum value |
|---|---|
| Operating system | Windows 10 version 1903 (May 2019 Update) or later |
| Architecture | x64 |
| RAM | 4 GB |
| Bluetooth | Bluetooth 4.0 or later (Bluetooth 5.0 recommended for multi-device stability) |
| .NET runtime | .NET 8 Desktop Runtime (auto-installed by MSIX installer) |
| WinApp SDK | 1.5 or later (bundled in installer) |
| Paired Bluetooth earphones | Any standard A2DP Bluetooth audio device — no LE Audio requirement |

---

*End of MultiEar build prompt. All sections above are required inputs to the build. Implement in the phase order defined in Section 9. Start with Phase 1 proof-of-concept before adding UI.*
