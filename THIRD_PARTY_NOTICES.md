# Third-Party Notices

RadioPad bundles or downloads the following third-party components. Their
licenses are reproduced or referenced below as required.

## On-device speech-to-text (desktop)

### NVIDIA Parakeet-TDT-0.6B-v3 (ASR model weights)
- Source: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3
- Packaged for sherpa-onnx and distributed via the sherpa-onnx `asr-models`
  GitHub release (downloaded to the user's machine on first run; not bundled in
  the installer).
- License: **CC-BY-4.0** (https://creativecommons.org/licenses/by/4.0/).
  **Attribution:** ASR model © NVIDIA Corporation, licensed under CC-BY-4.0.

### sherpa-onnx (k2-fsa)
- Source: https://github.com/k2-fsa/sherpa-onnx
- In-process ASR runtime (C#/.NET bindings + native libraries).
- License: **Apache-2.0**.

### ONNX Runtime (Microsoft)
- Inference engine used by sherpa-onnx.
- License: **MIT**.

### SharpZipLib
- Source: https://github.com/icsharpcode/SharpZipLib
- Used to extract the downloaded model bundle (`.tar.bz2`).
- License: **MIT**.

<!--
Phase 2 additions (when the second engine lands):
- OpenAI Whisper model weights — MIT.
- whisper.cpp (ggml-org) — MIT.
- Whisper.net (sandrohanea) — MIT.
-->
