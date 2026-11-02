<p align="center">
  <img src="docs/logo.png" width="128" alt="Pulsar logo" />
</p>

<h1 align="center">Pulsar</h1>

<p align="center">
  Download, play, cut, convert and transcribe video and audio on Windows.<br/>
  A modern WinUI 3 front end for <b>yt-dlp</b>, <b>FFmpeg</b>, <b>LibVLC</b> and <b>whisper.cpp</b>.
</p>

<p align="center">
  <a href="../../actions/workflows/build.yml"><img src="../../actions/workflows/build.yml/badge.svg" alt="Build" /></a>
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/{owner}/{repo}?label=download" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4" alt="Windows 10 and 11" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT license" /></a>
</p>

---

## Features

### Download
- Paste a link (or just copy one: Pulsar offers to grab it) and see the title, thumbnail and every available format.
- Presets for best-quality MP4, 1080p/720p/480p, MP3, M4A, Opus and FLAC, or pick an exact format.
- A queue with live progress, speed and ETA for each item, plus cancel, retry, play and show in folder.
- Clips: download only part of a video (for example 1:30 to 2:45).
- Playlists, English subtitles, embedded thumbnails, metadata and chapters.
- **Split by chapter:** one file per chapter, great for albums, mixes and long podcasts.
- **SponsorBlock:** mark sponsors, intros, outros and other segments as chapters, or cut them out entirely.
- **Browser cookies:** use your signed-in Edge, Chrome, Firefox or Brave session for members-only or age-restricted videos.
- **Speed limit**, so downloads don't swamp your connection.
- One-click **yt-dlp updates** (stable or nightly), with an optional check at startup.

### Subscriptions
- Follow a channel or playlist and Pulsar downloads new uploads automatically on a schedule (hourly to weekly).
- "Only new uploads" mode skips everything already on the channel.

### History
- A searchable list of everything you've downloaded, kept across restarts. Play, show in folder, copy the link, or download again.

### Player
- Built on LibVLC (VLC's engine), so it plays practically any audio or video format.
- Seek, skip, volume, 0.5× to 2× speed, audio-track and subtitle switching (or load an .srt), full screen, drag and drop.
- **Mark in/out** (`I` / `O`) and **Send to Trim** in one click.
- **A-B loop** (`L`) for practicing songs or reviewing footage.
- **Frame grab** (`S`) saves the current frame as a PNG in `Pictures\Pulsar`.
- **Picture-in-picture** (`P`): a small always-on-top window.
- **Resume**: remembers where you left off in longer files.

### Tools
All tools work on a list of files, so you can batch-process a whole folder.

| Tool | What it does |
|---|---|
| Trim | Fast (stream copy) or frame-accurate cuts |
| Convert | H.264, HEVC, VP9/WebM, compress for sharing, remux, GIF |
| Extract audio | MP3, M4A, Opus, FLAC, WAV or the original track |
| Merge | Join clips in order: fast (same format) or re-encode (mixed formats and sizes) |
| Normalize audio | Even out loudness to −14, −16 or −23 LUFS |
| Burn subtitles | Permanently draw an .srt, .ass or .vtt onto the video |
| Rotate and crop | Rotate, flip, crop to 16:9 / 9:16 / 1:1 / 4:3 / 4:5, resize |
| Transcribe | Subtitles (.srt/.vtt) or a transcript (.txt) from speech, with optional translation to English |

**Transcription** runs either on your own PC with whisper.cpp (private and free; Pulsar downloads the engine and model the first time) or on any OpenAI-compatible `/v1/audio/transcriptions` server, including OpenAI itself, faster-whisper-server, or a GPU box on your network.

### Everyday comforts
- Minimize or close to the **system tray**, **start with Windows**, and get **notifications** when downloads or tools finish in the background.
- **Explorer "Send to"** entries: right-click any file → Send to → *Pulsar Player* or *Pulsar Tools*.
- Light, dark or follow-Windows **theme**, Mica backdrop, and remembered window sizes.

## Install

Download the latest **Pulsar-Setup** installer (or the portable zip) from [Releases](../../releases/latest).

- The installer is per-user by default, so no admin rights are needed.
- On first launch, click **Install now** to download yt-dlp and FFmpeg (about 150 MB) into `%LOCALAPPDATA%\Pulsar\bin`.
- Requires Windows 10 1809 or later (x64).

> Windows SmartScreen may warn about an unrecognized app because releases aren't code-signed yet. Choose **More info → Run anyway**.

## Keyboard shortcuts (player)

| Key | Action | Key | Action |
|---|---|---|---|
| Space | Play / pause | I / O | Mark clip in / out |
| ← / → | Back / forward 5 s | L | A-B loop (set A, set B, clear) |
| ↑ / ↓ | Volume | S | Save frame as PNG |
| M | Mute | P | Picture-in-picture |
| F, double-click | Full screen | Ctrl+O | Open file |
| Esc | Leave full screen / PiP | | |

## Command line

```text
Pulsar.exe --play "C:\Videos\clip.mp4"      open a file in the player
Pulsar.exe --tools file1.mp4 file2.mov      add files to the Tools page
Pulsar.exe --url https://example.com/video  start a download
Pulsar.exe --tray                           start hidden in the tray
```

Pulsar is single-instance: a second launch hands its arguments to the running copy.

## Build from source

Requirements: Windows 10/11, the [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer), and optionally Visual Studio 2022 with the *Windows application development* workload.

```powershell
git clone https://github.com/{owner}/{repo}.git
cd Pulsar\Pulsar
dotnet run -p:Platform=x64
```

Or open `Pulsar.sln` in Visual Studio, set the platform to **x64**, and press F5. The app is unpackaged (`WindowsPackageType=None`), so it runs as a normal `.exe`.

To build a release locally:

```powershell
dotnet publish Pulsar\Pulsar.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publish
iscc /DAppVersion=1.0.0 installer\Pulsar.iss   # optional, needs Inno Setup 6
```

Pushing a tag like `v1.0.0` runs the [Release workflow](.github/workflows/release.yml), which builds the installer and portable zip and publishes a GitHub release.

### Project layout

```text
Pulsar/
  App.xaml(.cs)          startup, activation, tray, notifications, shared view models
  Program.cs             single-instance entry point (forwards "Send to" arguments)
  MainWindow.xaml(.cs)   Mica shell with navigation; remembers size; close/minimize to tray
  PlayerWindow.xaml(.cs) LibVLC player: marks, A-B loop, snapshots, PiP, resume
  Pages/                 Download, Subscriptions, History, Tools, Settings
  ViewModels/            CommunityToolkit.Mvvm view models
  Services/              yt-dlp, FFmpeg, whisper, updates, history, tray, shell integration…
  Models/                download jobs, presets, history entries, subscriptions
installer/Pulsar.iss     Inno Setup script
docs/                    logo
```

## Where Pulsar keeps things

Everything lives in `%LOCALAPPDATA%\Pulsar`: tools in `bin`, Whisper models in `models`, plus `settings.json`, `history.json`, `subscriptions.json`, `resume.json` and `pulsar.log`. Uninstalling leaves this folder in case you reinstall; delete it to remove everything.

## Please download responsibly

Pulsar is a general-purpose tool. Only download content you own or have permission to save, and respect each site's terms of service and your local copyright law.

## Credits and license

Pulsar is MIT-licensed; see [LICENSE](LICENSE). It stands on the shoulders of [yt-dlp](https://github.com/yt-dlp/yt-dlp), [FFmpeg](https://ffmpeg.org), [LibVLC / LibVLCSharp](https://www.videolan.org), [whisper.cpp](https://github.com/ggml-org/whisper.cpp), [SponsorBlock](https://sponsor.ajay.app) and the [Windows App SDK](https://github.com/microsoft/WindowsAppSDK). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for their licenses.
