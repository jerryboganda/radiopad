# Desktop Whisper sidecar (PRD §17.5)

The Tauri shell can ship `whisper.cpp` as a sidecar binary so dictation works
without any cloud round trip. The binary is wired the same way as the API
sidecar (see [.github/workflows/desktop-bundle.yml](../.github/workflows/desktop-bundle.yml)):

1. Build `whisper.cpp` for the target triple:

   ```powershell
   git clone https://github.com/ggerganov/whisper.cpp /tmp/whisper.cpp
   cd /tmp/whisper.cpp; make
   ```

2. Copy the produced binary into `desktop/src-tauri/binaries/whisper-<triple>[.exe]`.

3. Drop a model file (e.g. `ggml-base.en.bin`) under
   `desktop/src-tauri/resources/whisper/`.

4. Use the `tauri::api::process::Command::new_sidecar("whisper")` binding
   from Rust to feed audio captured via `cpal`. The transcript is emitted
   back to the renderer through `app.emit("dictation:transcript", text)`,
   which the React hook in `frontend/components/DictateButton.tsx`
   subscribes to when `window.__TAURI__` is present.

The model never leaves the device — this is the only dictation path
approved for PHI.
