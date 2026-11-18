"""Build-time only: pip install edge-tts numpy; FFmpeg must be on PATH.
Generate neutral Xiaoxiao female speech once and ship validated WAVs. The plugin never calls
an online speech service or requires Python at alert time.
"""
import argparse
import asyncio
import hashlib
import json
import subprocess
import tempfile
import wave
from pathlib import Path
import edge_tts
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--ffmpeg', default='ffmpeg')
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
VOICE = 'zh-CN-XiaoxiaoNeural'
PROFILE = {'voice': VOICE, 'rate': '+0%', 'pitch': '+0Hz',
           'generator_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
profile_file = args.output / 'voice-profile.json'
if list(args.output.glob('*.wav')) and (not profile_file.exists() or json.loads(profile_file.read_text(encoding='utf-8')) != PROFILE):
    raise ValueError('Output contains clips from another voice/profile; choose a fresh output directory.')
profile_file.write_text(json.dumps(PROFILE, ensure_ascii=False, indent=2), encoding='utf-8')

def chinese(number):
    digits = '零一二三四五六七八九'
    if number < 10: return digits[number]
    return (digits[number // 10] if number >= 20 else '') + '十' + (digits[number % 10] if number % 10 else '')

async def generate(name, text, limit=None):
    with tempfile.TemporaryDirectory() as temp:
        mp3 = Path(temp) / 'source.mp3'
        raw = Path(temp) / 'raw.wav'
        await edge_tts.Communicate(text, VOICE, rate=PROFILE['rate'], pitch=PROFILE['pitch']).save(str(mp3))
        subprocess.run([args.ffmpeg, '-v', 'error', '-y', '-i', str(mp3), '-ac', '1', '-ar', '24000', '-c:a', 'pcm_s16le', str(raw)], check=True)
        with wave.open(str(raw)) as w: data = np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768
        audible = np.flatnonzero(np.abs(data) > 0.002)
        if audible.size == 0: raise ValueError(f'Empty clip: {name}')
        data = data[max(0, audible[0] - 360):min(data.size, audible[-1] + 360)]
        data *= 0.86 / float(np.max(np.abs(data)))
        duration = data.size / 24000
        tempo = max(1.0, duration / limit) if limit else 1.0
        if tempo > 1.40: raise ValueError(f'{text} requires unnatural tempo {tempo:.2f}')
        with wave.open(str(raw), 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(24000)
            w.writeframes((np.clip(data, -1, 1) * 32767).astype('<i2').tobytes())
        subprocess.run([args.ffmpeg, '-v', 'error', '-y', '-i', str(raw), '-af', f'atempo={tempo:.7f},afade=t=in:st=0:d=0.006', '-c:a', 'pcm_s16le', str(args.output / f'{name}.wav')], check=True)
    with wave.open(str(args.output / f'{name}.wav')) as w: final = w.getnframes() / w.getframerate()
    if limit and final > limit + 0.025: raise ValueError(f'{text} exceeded its slot: {final}')
    print(f'{name}: {duration:.3f}s -> {final:.3f}s, tempo {tempo:.2f}', flush=True)

async def main():
    semaphore = asyncio.Semaphore(1)
    async def limited(name, text, limit=None):
        async with semaphore:
            target = args.output / f'{name}.wav'
            if target.exists():
                with wave.open(str(target)) as w:
                    if w.getnframes() > 2400 and (limit is None or w.getnframes() / w.getframerate() <= limit + 0.025): return
            for attempt in range(5):
                try:
                    await generate(name, text, limit)
                    await asyncio.sleep(0.4)
                    return
                except Exception:
                    if attempt == 4: raise
                    await asyncio.sleep(2 * (attempt + 1))
    await asyncio.gather(*(limited(str(number), chinese(number) + '。', 0.64 if number <= 10 else 1.64) for number in range(1, 61)))
    for name, text in {
        'intro': '地震预警。预计震动即将到达，请就近避险。',
        'intro-drill': '地震预警演练。请就近避险，注意倒计时。',
        'intro-urgent': '地震预警，立即避险。',
        'intro-urgent-drill': '演练，请就近避险。',
        'arrived': '预计震动已到达，请保护头部，等待震动结束。',
        'arrived-drill': '演练，预计震动已到达。请保持就近避险。',
    }.items(): await limited(name, text)
asyncio.run(main())
