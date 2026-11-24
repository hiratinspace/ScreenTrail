"""Word error and invented words, with and without the voicing check (ST-027).

The gate goes by loudness, so a keyboard and a mouse beside the microphone pass it, and whisper writes
words for them that nobody said: on the laptop on 2026-11-22 a session with nobody talking kept 12 and 19
transcript segments. `Voicing` refuses a stretch with no pitch in it before the model reads it. This
measures what that costs on real speech and what it saves on typing and clicks, running the service's own
pipeline from two checkouts: the one this file is in (with the check) and a baseline (without it).

    git worktree add /tmp/screentrail-baseline origin/main
    python research/eval/voice_gate/measure.py \
        --work ~/screentrail-voice-gate --baseline /tmp/screentrail-baseline

Recordings, none of them committed (they stay in --work):

- AMI Meeting Corpus ES2004a, the four headsets, and LibriSpeech test-clean, fetched as
  research/eval/real_speech/measure.py fetches them (both CC BY 4.0).
- Typing and clicks: ESC-50's 40 "keyboard_typing" and 40 "mouse_click" clips (Piczak, 2015,
  https://github.com/karolpiczak/ESC-50, CC BY-NC 3.0), at 16 kHz over a quiet room floor, each clip at
  full level and again 12 dB down, a second of floor between clips. Nobody speaks in it, so every word
  heard is invented. Built by `noise_track` below.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import urllib.request
import wave
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "real_speech"))
sys.path.insert(0, str(HERE.parent))

import measure as real  # noqa: E402
import wer  # noqa: E402

ESC = "https://raw.githubusercontent.com/karolpiczak/ESC-50/master/"


def noise_track(work: Path) -> real.Recording:
    """The typing-and-clicks recording. numpy and scipy only for building it."""
    out = work / "noise" / "typing-and-clicks.wav"
    reference = work / "noise" / "typing-and-clicks.txt"
    if not out.exists():
        import numpy as np
        from scipy.signal import resample_poly

        (work / "esc").mkdir(parents=True, exist_ok=True)
        meta = work / "esc50.csv"
        if not meta.exists():
            urllib.request.urlretrieve(ESC + "meta/esc50.csv", meta)
        names = [
            line.split(",")[0]
            for line in meta.read_text().splitlines()[1:]
            if line.split(",")[3] in ("keyboard_typing", "mouse_click")
        ]
        rng = np.random.default_rng(7)

        def floor(n: int, db: float = -62.0):
            brown = np.cumsum(rng.standard_normal(n))
            brown -= np.convolve(brown, np.ones(400) / 400, mode="same")
            brown /= np.sqrt(np.mean(brown**2)) + 1e-9
            return brown * 10 ** (db / 20)

        parts = []
        for gain in (0.0, -12.0):
            for name in names:
                clip = work / "esc" / name
                if not clip.exists():
                    urllib.request.urlretrieve(ESC + "audio/" + name, clip)
                with wave.open(str(clip)) as f:
                    raw = np.frombuffer(f.readframes(f.getnframes()), dtype=np.int16) / 32768
                    rate = f.getframerate()
                x = resample_poly(raw, 160, rate // 100) * 10 ** (gain / 20)
                parts += [floor(16000), x + floor(len(x))]
        track = np.concatenate(parts + [floor(16000)])
        out.parent.mkdir(exist_ok=True)
        with wave.open(str(out), "wb") as f:
            f.setnchannels(1)
            f.setsampwidth(2)
            f.setframerate(16000)
            f.writeframes((np.clip(track, -1, 1) * 32767).astype(np.int16).tobytes())
    reference.write_text("", encoding="utf-8")
    return real.Recording("Typing and clicks (ESC-50)", "noise", out, reference)


def transcribe(harness: Path, work: Path, recording: real.Recording):
    """
    real_speech's run, except that hearing nothing is a result here. The service's transcribe command
    exits 1 when it kept no segment, which on typing and clicks is the outcome this measures.
    """
    try:
        return real.transcribe(harness, work, recording, False, "docker", "base.en")
    except SystemExit as failed:
        message = str(failed)
        summary = real._SUMMARY.search(message)
        if summary is None or not summary.group(1) == "0":
            raise
        return "", summary.groups(), None


def build(checkout: Path) -> Path:
    project = checkout / "research" / "eval" / "real_speech" / "harness" / "SpeechHarness.csproj"
    subprocess.run(
        ["dotnet", "build", str(project), "-c", "Release", "--nologo", "-v", "quiet"],
        check=True,
        stdout=sys.stderr,
    )
    subprocess.run(
        ["docker", "build", "-q", "-t", real.IMAGE, str(project.parent)], check=True, stdout=sys.stderr
    )
    return project.parent / "bin" / "Release" / "net10.0"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True, help="A checkout without the voicing check.")
    parser.add_argument("--librispeech-minutes", type=float, default=12.0)
    parser.add_argument("--jobs", type=int, default=4)
    parser.add_argument("--no-meeting", action="store_true", help="Leave the AMI meeting out.")
    parser.add_argument("--no-noise", action="store_true", help="Leave the typing-and-clicks track out.")
    args = parser.parse_args(argv)
    work = args.work.expanduser().resolve()

    recordings = [] if args.no_meeting else real.ami(work, "ES2004a")
    if args.librispeech_minutes > 0:
        recordings += real.librispeech(work, args.librispeech_minutes)
    if not args.no_noise:
        recordings.append(noise_track(work))

    arms = {"without": build(args.baseline.expanduser().resolve()), "with": build(HERE.parent.parent.parent)}
    with ThreadPoolExecutor(max_workers=max(args.jobs, 1)) as pool:
        first = recordings[0]
        warm = pool.submit(transcribe, arms["without"], work, first)
        warm.result()
        runs = {
            (r.name, arm): pool.submit(transcribe, h, work, r)
            for r in recordings
            for arm, h in arms.items()
            if not (r is first and arm == "without")
        }
        runs[(first.name, "without")] = warm

    rows = []
    for r in recordings:
        reference = r.reference.read_text(encoding="utf-8")
        row = {"recording": r.name, "corpus": r.corpus}
        for arm in arms:
            text, summary, _ = runs[(r.name, arm)].result()
            out = work / "heard" / f"{r.wav.stem}.voice-{arm}.txt"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(text, encoding="utf-8")
            row[arm] = wer.measure(reference, text).as_dict() | {
                "segments": int(summary[0]) if summary else None,
                "rejected": int(summary[1]) if summary else None,
                "stretches": int(summary[2]) if summary else None,
                "pipeline_seconds": float(summary[3]) if summary else None,
                "words_heard": len(text.split()),
            }
            row["audio_seconds"] = float(summary[4]) if summary else None
        rows.append(row)
    name = "voice-gate-results" + ("-librispeech" if args.no_meeting else "") + ".json"
    (work / name).write_text(json.dumps(rows, indent=2) + "\n", encoding="utf-8")

    print(table(rows))
    return 0


def cell(row: dict, arm: dict) -> str:
    if row["corpus"] == "noise":
        return "n/a"
    return f"{arm['wer']:.1%} ({arm['substitutions']} sub, {arm['deletions']} del, {arm['insertions']} ins)"


def table(rows: list[dict]) -> str:
    lines = [
        "| Recording | Words said | Word error, without | with |"
        " Words heard, without | with | Pipeline s, without | with |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for row in rows:
        a, b = row["without"], row["with"]
        lines.append(
            f"| {row['recording']} | {a['reference_words']} | {cell(row, a)} | {cell(row, b)} |"
            f" {a['words_heard']} | {b['words_heard']} |"
            f" {a['pipeline_seconds']:.0f} | {b['pipeline_seconds']:.0f} |"
        )
    return "\n".join(lines)


if __name__ == "__main__":
    raise SystemExit(main())
