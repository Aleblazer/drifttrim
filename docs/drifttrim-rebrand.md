# DriftTrim naming check and rebrand

Initial checks: September 24, 2026. Repository transition: October 16, 2026.
This is a preliminary name-conflict and availability
check, not trademark clearance or a reservation of any name.

## Findings

| Check | Observation |
| --- | --- |
| Exact-name web/software search | No obvious app or audio-product brand named DriftTrim found. Some unrelated automotive text uses the same word. |
| GitHub repository-name search | GitHub API query `drifttrim in:name` returned zero repositories. |
| Proposed repository `Aleblazer/drifttrim` | Authenticated GitHub repository API returned HTTP 404. It appears unused to this account; it has not been reserved or created. |
| `drifttrim.com` | Verisign's registry RDAP lookup returned HTTP 404 (no domain record). |
| `drifttrim.app` | Google Registry's RDAP lookup returned HTTP 404 (no domain record). |
| App-store and trademark-domain web searches | No indexed exact-name result found in the searches performed. Direct trademark-database clearance and registrar checkout availability remain unverified. |

Sources / reproducible lookups:

- [GitHub repository search API](https://api.github.com/search/repositories?q=drifttrim+in%3Aname)
- [Proposed repository API lookup](https://api.github.com/repos/Aleblazer/drifttrim)
- [.com registry RDAP](https://rdap.verisign.com/com/v1/domain/drifttrim.com)
- [.app registry RDAP](https://pubapi.registry.google/rdap/domain/drifttrim.app)

Registry absence does not establish price or guarantee that a registrar can sell
the domain. Search-engine absence does not establish a legal right to a name.
At the initial check, no domain purchases, repository renames or releases had
been performed. On October 16, the user approved and the existing GitHub
repository was renamed to `Aleblazer/drifttrim`; the local origin URL was updated.
No domain purchases or new releases were performed.

## First rebrand release

Starting with the 1.6.0 test build, the app uses DriftTrim in window/tray text, dialogs, diagnostics/settings
exports, assembly product/title metadata, installer display/shortcut names, CI
release titles and downloadable asset names.

Compatibility identities deliberately remain unchanged:

- `%APPDATA%\ZmkVolumeFader\settings.v2.json` and the existing error log location.
- Startup registry value `ZMK Volume Fader` and its executable path.
- Single-instance mutex and window activation message, so old and renamed
  copies cannot independently drive the same controls or settings.
- Internal `ZmkVolumeFader.exe`, source project/namespace names and install path.
- MSI UpgradeCode and file component GUID/key path, preserving the existing
  installation's upgrade lineage. An installed-version upgrade remains unverified.

The existing icon is retained. Version 1.6 now adds desktop MIDI input alongside
ZMK HID, virtual faders and hotkeys. See [the QMK/MIDI guide](qmk-midi.md) for
profiles, module configuration, mapping, testing status and current limits.

Validation: the Release application build passed with warnings treated as errors,
all existing logic tests passed, and the self-contained Windows executable and
WiX installer built successfully. The executable product name and file description
were checked as DriftTrim, version 1.6.0.0; the MSI product name/version and
unchanged UpgradeCode were also inspected. These checks do not replace an actual
installed-version upgrade test.

October 16 MIDI validation: the full release build and logic suite passed, the
dependency audit reported no vulnerable packages, and the packaged EXE's
`--diag-midi-list` completed successfully with exit code 0 and no input endpoints.
The EXE/MSI SHA-256 checksums and MSI repository link were verified. Hardware
input and a real installed-version upgrade remain untested.

Version 1.6.1 simplifies setup to one entry per MIDI endpoint and a separate
Input profile selector, defaulting to generic 14-bit CC. Saved profiles and
bindings retain their identity. The patch-version increase permits upgrading
the 1.6.0 test installer. UI smoke checks cover profile selection, capture and
restoration of an existing QMK profile.

Version 1.6.2 is the first public DriftTrim release. It also adds a resizable
main window that uses two columns when there's room for them. See the
[release notes](releases/v1.6.2.md) for the full changes since 1.5.0.

## Repository transition

The existing repository was renamed to [`Aleblazer/drifttrim`](https://github.com/Aleblazer/drifttrim)
on October 16, 2026, preserving its history, issues, stars and releases. The local
Git origin, README badge and release links, Options link and installer support
links now use the new URL. The local source folder retains its original name.
Do not recreate a repository at the old name, which would interfere with GitHub
redirects. The rename itself did not publish the desktop changes.

See [GitHub's repository rename documentation](https://docs.github.com/en/repositories/creating-and-managing-repositories/renaming-a-repository).

Further work: decide whether to register a domain, complete any desired trademark
clearance, test an installed upgrade from 1.5.0, and continue validating the MIDI
path with ZodiarkPi/Sailstone hardware.
