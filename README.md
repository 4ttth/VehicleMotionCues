# Vehicle Motion Cues for laptops

Animated dots at the edges of your laptop screen that move with the car, bus or train you're
riding in. It works like **iOS Vehicle Motion Cues**, but for Windows (macOS coming next).

Motion sickness while using a screen comes from a mismatch: your body feels the vehicle turn and
brake while your eyes see a perfectly still screen. The dots give your peripheral vision motion
that matches what you feel. See [docs/RESEARCH.md](docs/RESEARCH.md) for the background and the
evidence.

> **For passengers only.** Never use this while driving.

## Download

Grab `VehicleMotionCues.exe` from the [latest release](https://github.com/4ttth/VehicleMotionCues/releases/latest).
It's a single self-contained file with nothing to install. Windows SmartScreen may warn because the
exe isn't code-signed yet: click **More info → Run anyway**.

## Motion sources

Most laptops have **no motion sensor**, so the app can use either of these:

| Source | When it's used |
| --- | --- |
| **iPhone** (recommended) | Your phone streams its accelerometer to the laptop over Wi-Fi or your iPhone's Personal Hotspot. No app install: it's a Safari page. |
| **Laptop accelerometer** | Picked up automatically on 2-in-1s, Surface devices and other laptops whose sensor Windows exposes. |

If both are live, the phone wins.

## Using it with an iPhone

1. Run `VehicleMotionCues.exe`. A tray icon appears, and on first run the **Connect iPhone**
   window opens. You can reopen it from the tray menu.
2. Put the phone and laptop on the **same Wi-Fi**, or join the laptop to your **iPhone's Personal
   Hotspot** (ideal in a car).
3. Scan the QR code with the iPhone Camera and open the link in Safari.
4. Safari says *"This Connection Is Not Private"* because the laptop made its own certificate.
   Tap **Show Details → visit this website → Visit Website**. You only do this once per laptop.
5. Tap **Start sending motion** and allow **Motion & Orientation** access.
6. Lay the phone flat with its **top toward the front of the car**, or stand it in a cup holder
   with the **screen facing you**. Keep Safari open and the screen on.

If the phone can't load the page, allow *Vehicle Motion Cues* through Windows Firewall when
prompted. When you're on the iPhone hotspot, Windows usually classifies it as a **Public** network,
so tick that too.

## Controls

- **Ctrl + Alt + M**, or click the tray icon: turn motion cues on or off.
- **Tray menu**:
  - **Connect iPhone…**: opens the pairing window.
  - **When to show**: *Automatically while moving* (default, like iOS) shows dots only while the
    vehicle is moving and hides them about 8 s after it stops. *Always* keeps them up.
  - **Dots**: *Larger dots* and *More dots*, as on iOS.
  - **Sensitivity**: Low, Medium or High.
  - **Direction**: flips left/right or forward/back if the dots move the wrong way for how your
    phone is placed.
  - **Preview with demo motion**: shows simulated turns and braking so you can see the effect
    while parked.
  - **Quit**.

The dots behave like loose objects sliding around the cabin, as on iOS. **Turning right sends them
streaming left, braking sends them up (forward), and speeding up sends them down (toward you).** A
sustained turn keeps them flowing for as long as you feel it. When the push stops, they glide to a
halt. The overlay is click-through and never takes focus.

## How it works

```
iPhone Safari page ──wss / https──▶ PhoneBridgeServer ─┐
 (DeviceMotion + orientation,                          │   lateral / longitudinal
  sign-normalised, ~60 Hz)                             ├──▶ MotionHub ──▶ CueDynamics ──▶ click-through overlay
Laptop accelerometer (WinRT) ──────────────────────────┘     (m/s²)      (smoothing,       (Avalonia, top-most,
                                                                          bias removal,      transparent)
                                                                          spring)
```

- `VehicleFrameSolver` finds "up" from gravity and "forward" from the device axis lying flattest,
  so the phone can be tilted any way and still produce clean lateral and longitudinal acceleration.
- `CueDynamics` smooths out road vibration, removes slow bias (sensor offset, sloped roads),
  applies a dead zone, then treats acceleration as a push on the dot field with friction. The
  field's velocity is integrated into an unbounded flow, which the renderer wraps inside a band at
  each screen edge, fading dots at the band boundaries. It also tracks motion energy to decide
  when the vehicle is moving (Automatic mode).
- The phone URL carries a random pairing key, so other devices on the same network can't inject
  motion.
- When nothing is moving, the overlay stops animating, so idle cost is about 3% of one CPU core.

Settings and the bridge certificate live in `%APPDATA%\VehicleMotionCues\`.

## Building

Requires the .NET 8 SDK.

```bash
dotnet publish src/VehicleMotionCues/VehicleMotionCues.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

Run `VehicleMotionCues.exe --demo` to start with demo motion on. CI builds the exe on every push
and attaches it to releases tagged `v*`.

## Roadmap

- [ ] **macOS build.** The code is already cross-platform (Avalonia). The Mac click-through path is
      written but untested, and CI compiles an experimental `osx-arm64` binary. Still to do:
      `.app` bundle, signing and notarisation, and reading the MacBook's built-in IMU where present.
      Note: macOS 26 reportedly ships its own Vehicle Motion Cues, so the Mac version mainly
      matters for older macOS and for using an iPhone as the sensor.
- [ ] Dot colour options (iOS offers automatic, black or white).
- [ ] Start with Windows.
- [ ] Multi-monitor support.
- [ ] Android phone page testing.

## License

MIT. See [LICENSE](LICENSE).
