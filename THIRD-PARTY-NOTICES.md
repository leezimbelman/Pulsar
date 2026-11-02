# Third-party software

Pulsar's own code is MIT-licensed (see [LICENSE](LICENSE)). It works with these projects, each under its own license.

| Component | How Pulsar uses it | License |
|---|---|---|
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Downloaded at runtime from its GitHub releases into `%LOCALAPPDATA%\Pulsar\bin` | Unlicense |
| [FFmpeg](https://ffmpeg.org) ([BtbN builds](https://github.com/BtbN/FFmpeg-Builds)) | Downloaded at runtime into `%LOCALAPPDATA%\Pulsar\bin` | GPL-3.0 (this build) |
| [whisper.cpp](https://github.com/ggml-org/whisper.cpp) and its ggml models | Downloaded at runtime on first transcription | MIT |
| [LibVLC](https://www.videolan.org/vlc/libvlc.html) via `VideoLAN.LibVLC.Windows` | Bundled with the app (player engine) | LGPL-2.1-or-later |
| [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) | Bundled (.NET bindings for LibVLC) | LGPL-2.1-or-later |
| [Windows App SDK / WinUI 3](https://github.com/microsoft/WindowsAppSDK) | Bundled (UI framework) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Bundled | MIT |
| [SponsorBlock](https://sponsor.ajay.app) | Segment data fetched by yt-dlp when you turn it on | Data: CC BY-NC-SA 4.0 |

yt-dlp, FFmpeg and whisper.cpp are not redistributed in this repository or in Pulsar's installer: Pulsar downloads them from their official release pages when you click Install.
