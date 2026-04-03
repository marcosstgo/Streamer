# Folder Mode Architecture

`Folder` mode used to rely on FFmpeg's concat demuxer. That approach looked simple, but it was fragile with real-world clip folders that mix metadata-heavy files, multiple audio tracks, timestamps from game capture tools, and randomized playback.

## Why concat was unstable

The old implementation hit these failure patterns:

- audio drift growing over time
- `End of file` / `Conversion failed` on RTMP output
- muxer instability between clips
- playlist folders that looked valid in UI but failed in practice
- repeated false recoveries triggered by watchdog logic after timestamps went bad

Even when clips were all `.mp4`, many capture tools still produced files with extra metadata streams, multiple audio tracks, and timestamp discontinuities that made concat a poor fit for a 24/7 live RTMP workflow.

## Current stable model

`Folder` mode now follows the same core idea as `Highlight` mode:

1. A persistent **streamer** FFmpeg process owns the RTMP output.
2. A per-clip **supplier** FFmpeg process reads one local file at a time.
3. The supplier writes MPEG-TS to `stdout`.
4. The streamer reads MPEG-TS from `stdin`, applies overlay/encode, and pushes RTMP.

This keeps the RTMP publisher alive while clips rotate underneath it.

## Responsibilities

### Supplier

The supplier is clip-specific and intentionally minimal:

- reads one file with `-re`
- maps only the primary video and primary audio stream
- ignores data/subtitle/unknown streams
- strips metadata and chapters
- outputs `mpegts` to `pipe:1`

The supplier is restarted for every new clip.

### Streamer

The streamer is persistent across the whole folder session:

- reads `mpegts` from `pipe:0`
- applies overlay when enabled
- encodes video/audio for RTMP
- owns the final network output state

This is the process shown as the active FFmpeg session for the stream.

## Why this is more stable

This model avoids treating a heterogeneous folder like one giant synthetic media file.

Benefits:

- cleaner separation between clip ingestion and RTMP publishing
- no long concat timeline accumulating timestamp problems
- safer handling of multi-audio clips by forcing `0:v:0` and `0:a:0?`
- loop/random/wait can be handled as queue logic, not FFmpeg playlist tricks
- much closer to a playout engine than a one-shot concat playlist

## Feature compatibility

The new `Folder` engine still supports:

- `Loop`
- `Randomize`
- `Wait mode`
- logo overlay
- scheduled start
- logging/history

## Debugging guidance

When `Folder` mode is under test, the most useful log file is:

`Streamer\bin\Release\net8.0-windows\win-x64\publish\streamer.log`

Look for these patterns:

- `Playing clip:`
- `[supply] Output #0, mpegts, to 'pipe:1'`
- `Output #0, flv, to 'rtmp://...'`
- `WATCHDOG state=`
- `FFmpeg exited`

If the supplier changes clips cleanly and the streamer RTMP session remains alive, `Folder` mode is behaving correctly.

## Design takeaway

Not every source mode should share the exact same FFmpeg strategy.

- `Online`, `File`, and `Capture` are single-source modes.
- `Highlight` and `Folder` are queue/playout modes.

The product should keep a shared UI/session/output layer, but input strategies should stay mode-specific when needed.
