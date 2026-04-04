# Operations Guide

This document collects day-to-day operational notes for running and testing `Streamer Pro`.

## Source Modes

`Streamer Pro` uses different input strategies depending on the selected mode.

### Online

- single remote source
- suitable for relaying HTTP/RTMP streams
- reconnect logic is focused on network recovery

### File

- single local file
- simplest playback path
- useful for one-off broadcasts or tests

### Folder

- local clip queue with `Loop`, `Randomize`, and `Wait mode`
- uses the **Persistent Streamer Pipeline** documented in `docs/FOLDER_MODE.md`
- best suited for long-running local clip channels

### Highlight

- watches one or more folders for newly created clips
- also uses a supplier -> streamer pipeline
- designed for SteelSeries Moments / NVIDIA Highlights / AMD ReLive style workflows

### Capture

- live source mode
- monitor capture + optional audio source
- not queue-based like `Folder` / `Highlight`

## Scheduled Start

`Scheduled Start` is designed as a lightweight local scheduler.

### Current behavior

- one scheduled start at a time
- local date + local time
- countdown shown in the UI
- can be cancelled manually
- survives app restart through config persistence
- uses the same preflight and start flow as a manual stream start

### Practical limitations

- the app must remain open or minimized to tray
- if Windows is asleep or the machine is off, the stream will not start
- if a stream is already active when the time is reached, the scheduled start is ignored

## Logging

### Preferred log location

When the executable folder is writable, `Streamer Pro` writes the main runtime log beside the executable:

`streamer.log`

This is the preferred log during publish-folder testing.

### Fallback log location

If the executable folder is not writable, logging falls back to:

`%AppData%\CorilloStreamer\streamer.log`

### Useful log patterns

- `Playing clip:`
- `Folder selected:`
- `WATCHDOG state=`
- `FFmpeg exited`
- `Output #0, flv, to 'rtmp://...'`
- `[supply] Output #0, mpegts, to 'pipe:1'`

## Health Model

The UI can show these practical health states:

- `Connecting`
- `Live`
- `Unstable`
- `Frozen`

`Unstable` does not always mean the stream is down. It often means FFmpeg is still alive but progress signals slowed down. The runtime log should be checked before assuming a total failure.

## Recommended Validation

### Folder stability test

Use this when validating the playlist engine:

1. Enable `Folder`
2. Enable `Loop`
3. Enable `Randomize`
4. Enable `Save Logs`
5. Let the session run long enough to cross multiple clip boundaries

What to verify:

- the stream remains visible on the destination
- clips rotate without dropping the RTMP session
- overlay remains active
- audio stays synchronized
- no repeated `FFmpeg exited` failures appear in the log

### Scheduled Start test

1. Set a local date/time a few minutes ahead
2. Confirm countdown moves every second
3. Confirm the stream starts automatically at the scheduled time
4. Confirm the schedule clears after it triggers

## Design Notes

- Not every mode should share the exact same FFmpeg strategy.
- `Online`, `File`, and `Capture` are single-source modes.
- `Folder` and `Highlight` are queue/playout modes and benefit from a persistent output pipeline.
- Stability should be preferred over clever FFmpeg shortcuts when the app is meant for unattended streaming.
