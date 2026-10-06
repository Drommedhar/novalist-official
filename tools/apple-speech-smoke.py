#!/usr/bin/env python3
"""Exercise the real Mac backend/Swift bridge with synthetic audio, never a mic.

Run after building the backend. --prepare permits Apple-managed asset downloads.
Point --backend at the executable inside a packaged .app to test its library too.
This does not replace live microphone, offline, or signed-device validation.
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import subprocess
import tempfile
import threading
import uuid
from concurrent.futures import Future
from pathlib import Path


class Backend:
    def __init__(self, executable: Path, settings: Path):
        self.pending: dict[int, Future] = {}
        self.next_id = 0
        self.process = subprocess.Popen(
            [str(executable.resolve())],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            env={
                **os.environ,
                "NOVALIST_SETTINGS_DIR": str(settings),
                "NOVALIST_EXTENSIONS_DISABLED": "1",
                "NOVALIST_ALLOW_LEGACY_MIGRATION": "0",
            },
        )
        threading.Thread(target=self.read, daemon=True).start()

    def read(self):
        try:
            while True:
                headers = {}
                while (line := self.process.stdout.readline()) != b"\r\n":
                    if not line:
                        raise RuntimeError("Backend closed its RPC stream")
                    key, value = line.decode().split(":", 1)
                    headers[key.lower()] = value.strip()
                message = json.loads(
                    self.process.stdout.read(int(headers["content-length"]))
                )
                future = self.pending.pop(message.get("id"), None)
                if future is not None:
                    future.set_result(message)
        except (OSError, RuntimeError, ValueError, KeyError, TypeError) as error:
            for future in list(self.pending.values()):
                future.set_exception(error)

    def send(self, method: str, params=None) -> Future:
        self.next_id += 1
        future = Future()
        self.pending[self.next_id] = future
        body = json.dumps(
            {
                "jsonrpc": "2.0",
                "id": self.next_id,
                "method": method,
                "params": params or {},
            }
        ).encode()
        self.process.stdin.write(f"Content-Length: {len(body)}\r\n\r\n".encode() + body)
        self.process.stdin.flush()
        return future

    def call(self, method: str, params=None, timeout=180):
        response = self.send(method, params).result(timeout)
        if "error" in response:
            raise RuntimeError(f"{method}: {response['error']['message']}")
        return response.get("result")

    def close(self):
        self.process.terminate()
        try:
            self.process.wait(10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()


def transcribe_params(audio: bytes, language: str):
    return {
        "requestId": str(uuid.uuid4()),
        "providerId": "novalist.system",
        "audioBase64": base64.b64encode(audio).decode(),
        "mimeType": "audio/wav",
        "language": language,
    }


def check_sample(backend: Backend, root: Path, sample, status, prepare: bool):
    language, voice, sentence, expected = sample
    ready = next(item for item in status["languages"] if item["language"] == language)
    assert ready["supported"], f"{language}: unsupported on this Mac"
    if not ready["installed"] and not prepare:
        raise RuntimeError(f"{language}: assets missing; rerun with --prepare")
    if prepare:
        backend.call(
            "dictation/prepareSystem",
            {"requestId": str(uuid.uuid4()), "language": language},
            timeout=1800,
        )
        installed = backend.call("dictation/systemStatus")
        assert next(
            item["installed"]
            for item in installed["languages"]
            if item["language"] == language
        )
        # Preparation is also safe to repeat once assets are installed.
        backend.call(
            "dictation/prepareSystem",
            {"requestId": str(uuid.uuid4()), "language": language},
        )
    wav = root / f"{language}.wav"
    subprocess.run(
        [
            "say",
            "-v",
            voice,
            "-o",
            str(wav),
            "--data-format=LEI16@16000",
            sentence,
        ],
        check=True,
        timeout=60,
    )
    audio = wav.read_bytes()
    text = backend.call("dictation/transcribe", transcribe_params(audio, language))
    print(f"{language} synthetic transcript: {text}", flush=True)
    assert all(word in text.lower() for word in expected), text
    assert all(text.lower().count(word) == 1 for word in expected), text
    # Exercise native cancellation and the managed callback lifetime,
    # then verify a new transcription still succeeds.
    params = transcribe_params(audio, language)
    pending = backend.send("dictation/transcribe", params)
    backend.call("dictation/cancel", {"requestId": params["requestId"]})
    cancelled = pending.result(30)
    assert "error" in cancelled and cancelled["error"]["code"] == -32800, cancelled
    retry = backend.call("dictation/transcribe", transcribe_params(audio, language))
    assert all(word in retry.lower() for word in expected), retry
    print(f"{language}: cancellation and retry passed", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--backend",
        type=Path,
        default=Path("Novalist.Backend/bin/Debug/net8.0/Novalist.Backend"),
    )
    parser.add_argument("--prepare", action="store_true")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="novalist-apple-speech-") as directory:
        root = Path(directory)
        backend = Backend(args.backend, root / "settings")
        try:
            status = backend.call("dictation/systemStatus")
            print("Apple status:", json.dumps(status), flush=True)
            assert status["engine"] == "apple" and status["available"], status
            providers = backend.call("dictation/providers")
            assert len(providers) == 1 and providers[0]["id"] == "novalist.system", (
                providers
            )
            samples = [
                (
                    "en",
                    "Samantha",
                    "The blue notebook is on the wooden table.",
                    ["notebook", "table"],
                ),
                (
                    "de",
                    "Anna",
                    "Das blaue Notizbuch liegt auf dem kleinen Tisch.",
                    ["notizbuch", "tisch"],
                ),
            ]
            for sample in samples:
                check_sample(backend, root, sample, status, args.prepare)
            invalid = backend.send(
                "dictation/transcribe", transcribe_params(b"invalid wave", "en")
            ).result(30)
            assert "error" in invalid and "invalid wave" not in json.dumps(invalid), (
                invalid
            )
            print(
                "PASS: real Apple speech via backend RPC; synthetic audio only",
                flush=True,
            )
        finally:
            backend.close()


if __name__ == "__main__":
    main()
