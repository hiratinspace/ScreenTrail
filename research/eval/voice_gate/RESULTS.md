# Only voiced stretches go to the model (ST-027)

**Measured 2026-11-24, of `st-027-voiced-segments-only` against `main` at `e7936926`.**
`python research/eval/voice_gate/measure.py` ran the service's speech pipeline over each recording twice:
from a checkout without the change ("without") and with it ("with"). Model `base.en`, one thread, full
encoder window, Whisper.net 1.9.1 in a Linux container, scored by `research/eval/wer.py`.

## What changed

Two checks, both before anything reaches the transcript:

- **`Voicing`**: a stretch the gate let through goes to the model only if enough of it has a pitch in it
  (normalised autocorrelation peak of at least 0.6 at a lag of 70 to 400 Hz, after a 100 Hz high-pass, in
  at least 15% of its audible 32 ms frames and at least 150 ms in all). Keys and clicks are loud but have
  no pitch. Refused stretches are counted (`NarrationRecorder.NotVoiced`) and cost no model time.
- **Sound tags**: a segment whose text is only bracketed descriptions ("(keyboard clicking)",
  "[MUSIC PLAYING]", "*sighs*") is rejected by the hallucination filter whatever its confidence. A tag among
  real words is kept.

## The short answer

**Typing and clicks with nobody speaking no longer produce a transcript, and the model's time on them
drops by nine tenths. Word error on real speech goes down, not up.**

## The numbers

| Recording | Length | Words said | Word error, without | with | Words written, without | with |
|---|---|---|---|---|---|---|
| AMI ES2004a, speaker A | 17.5 min | 303 | 46.9% (47 sub, 21 del, 74 ins) | 42.2% (36 sub, 30 del, 62 ins) | 364 | 333 |
| AMI ES2004a, speaker B | 17.5 min | 1,067 | 14.5% (59 sub, 77 del, 19 ins) | 13.9% (58 sub, 78 del, 12 ins) | 1,008 | 1,000 |
| AMI ES2004a, speaker C | 17.5 min | 440 | 65.5% (81 sub, 45 del, 162 ins) | 47.3% (73 sub, 52 del, 83 ins) | 567 | 471 |
| AMI ES2004a, speaker D | 17.5 min | 804 | 41.3% (62 sub, 40 del, 230 ins) | 38.7% (53 sub, 48 del, 210 ins) | 1,003 | 965 |
| **AMI, all four** | 70 min | 2,614 | **35.1%** (249 sub, 183 del, 485 ins) | **30.4%** (220 sub, 208 del, 367 ins) | 2,942 | 2,769 |
| LibriSpeech test-clean | 14.9 min | 2,017 | 3.8% (52 sub, 8 del, 17 ins) | 3.8% (53 sub, 8 del, 16 ins) | 2,026 | 2,025 |
| Typing and clicks (ESC-50) | 16.0 min | 0 | n/a | n/a | 224 | **0** |

The "without" column matches `research/eval/real_speech/RESULTS.md` (same pipeline, same audio).

What the pipeline did with the same audio:

| Recording | Stretches heard | Segments kept, without | with | Rejected as invented, without | with | Model seconds, without | with |
|---|---|---|---|---|---|---|---|
| Speaker A | 147 | 64 | 46 | 64 | 45 | 408 | 563 |
| Speaker B | 136 | 107 | 99 | 15 | 10 | 719 | 633 |
| Speaker C | 234 | 139 | 54 | 69 | 139 | 1,275 | 1,186 |
| Speaker D | 225 | 113 | 89 | 91 | 58 | 1,219 | 918 |
| LibriSpeech | 87 | 87 | 86 | 0 | 0 | 462 | 379 |
| Typing and clicks | 206 | 122 | **0** | 36 | 11 | 665 | **73** |

The pipeline seconds are of four runs at once on a Mac, so they are indicative only (speaker A's went up
where its work went down). The typing-and-clicks figure is not close: 192 of its 206 stretches never
reach the model.

## What it costs

A few more deleted words where people speak quietly or briefly: 21 to 30, 45 to 52 and 40 to 48 for the
meeting's three quieter speakers, 77 to 78 for its main one, none on read-aloud speech. Those are short
or breathy stretches with too little pitch to pass. Against that, insertions fall by more in every case,
and most of the meeting's improvement for speaker C is tag-only segments ("[MUSIC]", "(laughs)") no
longer counted as words. Whispering has no pitch and would not be transcribed; a technician narrating to
a ticket does not whisper.

## The recordings

None committed; `--work` holds them.

- AMI Meeting Corpus ES2004a headsets and LibriSpeech test-clean, as in `research/eval/real_speech`
  (CC BY 4.0).
- Typing and clicks: ESC-50's 40 `keyboard_typing` and 40 `mouse_click` clips (Piczak, 2015, CC BY-NC
  3.0), at 16 kHz over a quiet room floor, each at full level and again 12 dB down, a second apart. Nobody
  speaks in it, so every word written is invented.

Still not ST-027 AC1, which is a technician narrating a real support session into their own microphone.
On the laptop, a silent session that kept 12 and 19 segments on 2026-11-22 is the check to repeat.
