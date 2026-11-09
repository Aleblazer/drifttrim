# QMK / VIK and MIDI input

DriftTrim 1.6 supports ZMK vendor HID, QMK/VIK MIDI, generic absolute MIDI CC,
virtual faders and hotkeys together in the same global layout. All sources use
the existing per-app, category, output-device, cap, inversion and soft-takeover
pipeline. Windows 10 version 2004/build 19041 or later, or Windows 11, is required.

## Add a MIDI fader

1. Connect the controller. QMK must expose a USB MIDI interface (`MIDI_ENABLE = yes`).
   If using the separate `midi_fader` keymap prepared for ZodiarkPi, flash it on
   both halves of the split. The Windows release does not contain a firmware image.
2. Open **Fader Layout → Add physical fader**. Select the MIDI device once in
   the device dropdown, then use the separate **Input profile** selector.
   New device selections default to **14-bit CC**. The available profiles are:

   | Profile | Values | When to use it |
   | --- | --- | --- |
   | 14-bit CC (default) | 0..16383 counts | The supplied firmware or another controller sending both coarse and fine CCs for every update; no heartbeat required. |
   | 7-bit CC | 0..127 counts | A controller sending a single absolute CC per update. |
   | 14-bit CC (QMK/VIK heartbeat) | 0..16383 counts | The supplied firmware, with per-fader dropout detection using its one-second resend. |

   The profile selector is hidden for HID devices. Existing saved faders retain
   their profiles, and **Source…** restores the saved profile when editing.

3. Sweep one fader fully end to end, then select **Capture**. The dialog learns
   its MIDI channel, controller/pair and actual endpoint range. The first report
   establishes a baseline; it is never mistaken for movement from an assumed zero.
4. Repeat for other faders, then select **Done** and choose each audio target.
   Existing **Source…** controls can rebind a fader while preserving its target.
5. Use **Options → calibration** for taper, inversion and endpoint mute settings.
   Generic controllers already reporting linear travel normally use **Straight**.

Connect/open errors appear in **Options → Diagnostics** and `error.log`. A port
may be unavailable while another MIDI program owns it. Discovery is event-driven;
the layout picker updates when MIDI devices appear. Only configured inputs and
the selected capture input are opened, and removing them releases the port.

## VIK module configuration and MIDI map

The firmware supports one to four populated inputs per ADS1115, at each of
`0x48`, `0x49`, `0x4A`, `0x4B` on each local bus. That permits four modules / 16
faders per half, 32 total. All modules default to ADDR pulled to GND (`0x48`).
Devices on the same bus must use different address jumpers.

The friendly grounded-ADDR configuration is:

```c
// Split keyboard
#define VIK_FADER_LEFT_COUNT 4
#define VIK_FADER_RIGHT_COUNT 4

// Non-split keyboard, instead of the split settings
#define VIK_FADER_COUNT 4
```

Additional modules are optional; omitted counts default to disabled:

```c
// Two-fader module at 0x49 on a split keyboard's left bus
#define VIK_FADER_LEFT_49_COUNT 2

// Corresponding non-split setting
#define VIK_FADER_49_COUNT 2
```

Use the analogous `_4A_COUNT` and `_4B_COUNT` names, and `_RIGHT_` for the right
half. The smaller-module configuration assumes consecutive inputs from AIN0,
with the same ADC and FET circuitry as the supplied four-fader schematic. A count
of zero disables an address; 1..4 selects the populated inputs.

With the default firmware MIDI channel base:

| Address on either bus | MIDI channel |
| --- | --- |
| 0x48 | 1 |
| 0x49 | 2 |
| 0x4A | 3 |
| 0x4B | 4 |

Within each channel:

| Side | AIN0 | AIN1 | AIN2 | AIN3 |
| --- | --- | --- | --- | --- |
| Left or non-split | CC 16/48 | CC 17/49 | CC 18/50 | CC 19/51 |
| Right | CC 20/52 | CC 21/53 | CC 22/54 | CC 23/55 |

Each cell lists coarse/fine CCs. `value = (coarse << 7) | fine`. The app supports
all 16 MIDI channels and any standard coarse controller 0..31 paired with 32..63;
the firmware's address/side assignment is a convention rather than an app limit.
Adding, resizing or losing a module does not renumber the others. Moving USB to
the other split half keeps the firmware's side assignments fixed, although Windows
may expose that half as a different endpoint requiring a rebind. Changing an
address jumper changes its channel and requires a new binding.

## Precision, pairing and calibration

The firmware's +/-4.096 V ADS1115 setting and right shift produce **0.25 mV per
MIDI count**. A nominal 3.3 V endpoint is about **13200**, not 16383. Capture the
real endpoints; DriftTrim preserves the original counts and applies taper after
reception. Do not divide these values to 7 bits before correcting a nonlinear pot.

The 14-bit profiles require coarse first, then fine **within 100 ms**, for each
update. Orphan, expired or reused fine bytes are ignored. No coarse-only volume
change is exposed while waiting for the pair. Interleaved channels and controls
have separate pairing state, reset when a port is reopened. Controllers sending
only a coarse message, fine first, or fine-only subsequent updates should use a
compatible output mode or the 7-bit profile instead.

MIDI bypasses the HID millivolt noise filter so it does not stall a one-count
movement. Firmware/controller filtering remains responsible for analog noise.
Calibration, capture thresholds and mute settings use each source's own units.
Unchanged reports refresh source liveness without taking volume ownership again.

## Disconnects, takeover and persistence

The QMK profile expects the supplied firmware's one-second complete-pair resend.
A control with no pair for 3.5 seconds is marked offline at the next one-second
maintenance tick. It stops contributing to the desired volume map and retains
its last displayed level; the app never invents a zero reading. Each control is
tracked independently, so a working module cannot keep a missing module alive.
When reports resume, soft takeover reads the current target volume before the
fader can take control, if soft takeover is enabled.

Generic CC profiles have no inactivity timeout: many controllers send only on
movement. Unplugging their MIDI endpoint still disconnects them. Standard MIDI
CC provides no module-presence announcement; generic silent module removal cannot
be distinguished from an untouched fader. Choose the QMK profile only for firmware
that actually provides the periodic resend.

Bindings use the Windows MIDI endpoint ID (stored as a hash), profile, channel
and controller. A changing Windows enumeration index or another device with the
same product name does not redirect a binding. If Windows assigns a new endpoint
ID after changing USB ports, firmware descriptors or drivers, use **Source…** to
rebind. The picker includes an endpoint-ID suffix to distinguish duplicate names.
The same physical CC pair cannot be added twice through different profiles.

Settings schema 5 preserves MIDI control addresses above the legacy eight-axis
HID limit. Existing layouts migrate with a backup through the established loader;
settings remain at `%APPDATA%\ZmkVolumeFader\settings.v2.json`. Older builds reject
schema 5 instead of silently truncating those bindings. HID packets still carry
up to eight axes; the global layout supports up to 128 configured faders.

## Validation and current limits

Automated checks exercise pairing order/expiry, channel isolation, endpoint
identity, all 32 default firmware controls, cross-profile duplicate prevention,
mixed-source settings round trips and 7-bit calibration. The optional test-runner
argument `--midi-smoke` initializes Windows MIDI discovery without opening ports.
The packaged executable also accepts `--diag-midi-list`, which writes JSON listing
MIDI source choices and exits without opening ports, loading settings, showing the
main UI or starting audio. This checks the self-contained Windows MIDI runtime.

The ZodiPact was observed in the MIDI device picker during early testing. Full
hardware validation of fader movement, Vial/MIDI coexistence, module removal and
recovery, and a 32-fader split setup remains open. An installed MSI upgrade from
1.5 to 1.6 has not been verified.

This implementation receives absolute MIDI 1.0 CC. Relative encoders, MIDI notes
as mute buttons, SysEx, motor-fader output/feedback and MIDI 2.0 are future work.

Implementation reference: [Microsoft Windows MIDI discovery and input](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/midi).
