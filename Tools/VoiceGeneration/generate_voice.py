"""
Generates the VR instructor's voice clips (Assets/Audio/Voice/VO_<line>.wav) from voice_script.tsv.

Runs completely offline with the open Kokoro-82M text-to-speech model (Apache-2.0), so the clips are ordinary local
audio files: the app itself never uses text-to-speech or the internet.

    1. In Unity: Tools > VR Armory > VR > Export Voice Script   (writes voice_script.tsv from VRTrainingText.Spoken)
    2. pip install kokoro-onnx soundfile
       Download kokoro-v1.0.onnx and voices-v1.0.bin from
       https://github.com/thewh1teagle/kokoro-onnx/releases/tag/model-files-v1.0 into one folder.
    3. python generate_voice.py --model-dir <that folder>                 (all lines)
       python generate_voice.py --model-dir <that folder> --only FC_LetGo  (just the lines you changed)
    4. Back in Unity, run Tools > VR Armory > VR > 2. Setup VR Scene (additive) so new lines are assigned.

Every clip uses the same voice, speed, pauses and loudness, so the instructor sounds like one person throughout.
A recorded human voice can replace any clip: keep the file name (mono WAV, a little silence at both ends).
"""

import argparse
import csv
import os
import re

import numpy as np
import soundfile as sf
from kokoro_onnx import Kokoro

# Blend of voice styles (weights sum to 1): onyx keeps it deep, fenrir adds energy. Tweak the weights to taste.
VOICE_MIX = {"am_onyx": 0.5, "am_fenrir": 0.5}
VOICE = None              # built from VOICE_MIX in main()
SPEED = 1.05              # brisk, drill-sergeant pace (0.85 sounded sleepy)
SENTENCE_PAUSE = 0.3      # seconds of silence between two sentences of one line
LEAD_IN, TAIL = 0.12, 0.25
TARGET_RMS_DB = -20.0     # every line equally loud
PEAK_LIMIT_DB = -1.5

# The TTS reads a lone "A" as the article ("uh"). Spelled for the sound only; the text shown in the app is unchanged.
PRONUNCIATION = [
    (re.compile(r"\bA\b(?= button| again)"), "Eigh"),
]

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))


def sentences(text):
    return [s for s in re.split(r"(?<=[.!?])\s+", text.strip()) if s]


def trim(audio, rate, threshold=0.008):
    """Cuts the model's own leading and trailing silence, so the pauses are ours and always the same."""
    loud = np.where(np.abs(audio) > threshold)[0]
    if len(loud) == 0:
        return audio
    pad = int(0.03 * rate)
    return audio[max(0, loud[0] - pad): min(len(audio), loud[-1] + pad)]


def speak(kokoro, text):
    for pattern, sound in PRONUNCIATION:
        text = pattern.sub(sound, text)
    parts, rate = [], 24000
    for i, sentence in enumerate(sentences(text)):
        audio, rate = kokoro.create(sentence, voice=VOICE, speed=SPEED, lang="en-us")
        if i > 0:
            parts.append(np.zeros(int(SENTENCE_PAUSE * rate), dtype=np.float32))
        parts.append(trim(np.asarray(audio, dtype=np.float32), rate))
    audio = np.concatenate([np.zeros(int(LEAD_IN * rate), np.float32), *parts, np.zeros(int(TAIL * rate), np.float32)])

    # Same loudness for every line. The odd loud consonant is rounded off by a soft limiter instead of turning the
    # whole line down, so no line ends up quieter than the others. Then soft edges.
    rms = np.sqrt(np.mean(audio ** 2)) + 1e-9
    audio *= 10 ** (TARGET_RMS_DB / 20) / rms
    limit = 10 ** (PEAK_LIMIT_DB / 20)
    knee = 0.7 * limit
    over = np.abs(audio) > knee
    audio[over] = np.sign(audio[over]) * (knee + (limit - knee) * np.tanh((np.abs(audio[over]) - knee) / (limit - knee)))
    fade = int(0.01 * rate)
    audio[:fade] *= np.linspace(0, 1, fade)
    audio[-fade:] *= np.linspace(1, 0, fade)
    return audio, rate


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model-dir", required=True, help="folder with kokoro-v1.0.onnx and voices-v1.0.bin")
    parser.add_argument("--script", default=os.path.join(HERE, "voice_script.tsv"))
    parser.add_argument("--out", default=os.path.join(PROJECT, "Assets", "Audio", "Voice"))
    parser.add_argument("--only", default="", help="comma-separated line names to (re)generate")
    args = parser.parse_args()

    only = {s.strip() for s in args.only.split(",") if s.strip()}
    kokoro = Kokoro(os.path.join(args.model_dir, "kokoro-v1.0.onnx"), os.path.join(args.model_dir, "voices-v1.0.bin"))
    global VOICE
    VOICE = sum(w * kokoro.get_voice_style(name) for name, w in VOICE_MIX.items()).astype(np.float32)
    os.makedirs(args.out, exist_ok=True)

    with open(args.script, encoding="utf-8") as f:
        rows = list(csv.DictReader(f, delimiter="\t"))
    for row in rows:
        if only and row["line"] not in only:
            continue
        audio, rate = speak(kokoro, row["text"])
        path = os.path.join(args.out, row["file"])
        sf.write(path, audio, rate, subtype="PCM_16")
        print(f"{row['file']:<34} {len(audio) / rate:5.1f} s   {row['text']}")


if __name__ == "__main__":
    main()
