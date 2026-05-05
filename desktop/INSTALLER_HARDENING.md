# RadioPad desktop installer hardening (DESK-002)

**Status:** Active  **Owner:** Desktop / Release Engineering  **Last Updated:** 2026-05-04

This runbook describes the per-OS signing, notarization, and verification steps
applied to every RadioPad desktop installer artifact produced by
`.github/workflows/desktop-release.yml`. Post-build verification is enforced in
CI by `.github/workflows/desktop-installer-verify.yml`.

---

## 1. Windows (MSI + NSIS)

### Code-signing certificate

- Use an **EV code-signing certificate** issued to `RadioPad, Inc.` and stored
  on a FIPS 140-2 Level 2 HSM (YubiKey 5 FIPS or equivalent).
- The certificate is **pinned by SHA-1 thumbprint** in
  `desktop/src-tauri/tauri.conf.json` → `bundle.windows.certificateThumbprint`.
  Never commit the thumbprint of a real production cert; the value is injected
  at build time from the `RADIOPAD_WIN_CERT_THUMBPRINT` GitHub secret.
- Digest algorithm: **SHA-256** (`bundle.windows.digestAlgorithm = "sha256"`).
  Dual-signing with SHA-1 is no longer required (Windows 7 deprecated).

### Timestamping

A signed binary with no timestamp expires when the cert expires. We use a
fallback chain (in order):

1. `http://timestamp.digicert.com` (default, set in `tauri.conf.json`)
2. `http://timestamp.sectigo.com`
3. `http://timestamp.globalsign.com/tsa/r6advanced1`

The release workflow retries `signtool sign /tr <url> /td sha256 /fd sha256`
against each URL until success.

### Dual-sign

Both the inner `.exe` (Tauri output) **and** the outer installer
(MSI from WiX, EXE from NSIS) must be signed. Tauri does this automatically when
`certificateThumbprint` is set; we additionally re-sign the final artifact in CI
to apply the latest timestamp.

### AppLocker / WDAC

A minimal WDAC policy template lives at `desktop/wdac/RadioPad.xml`. The
**production** WDAC policy is generated post-build from the published binary's
Authenticode hash via:

```powershell
New-CIPolicy -FilePath RadioPad.xml -Level FilePublisher -ScanPath .\target\release
ConvertFrom-CIPolicy -XmlFilePath RadioPad.xml -BinaryFilePath RadioPad.cip
```

The `.cip` artifact is published alongside the MSI.

### Verify

```powershell
signtool verify /pa /v RadioPad.msi
signtool verify /pa /v RadioPad-setup.exe
```

---

## 2. macOS (DMG)

### Hardened runtime

- `bundle.macOS.hardenedRuntime = true`.
- Entitlements file: `desktop/src-tauri/entitlements.plist` — see that file
  for the rationale per entitlement. We grant `allow-jit` (V8 needs it),
  `network.client`, and `files.user-selected.read-write`. Library validation
  is NOT disabled; `allow-unsigned-executable-memory` is explicitly false.

### Signing

The Developer ID Application certificate is loaded into a keychain by the
release workflow:

```bash
security create-keychain -p "$KEYCHAIN_PWD" build.keychain
security import codesign.p12 -k build.keychain -P "$P12_PWD" -T /usr/bin/codesign
codesign --force --options runtime --timestamp \
         --entitlements desktop/src-tauri/entitlements.plist \
         --sign "Developer ID Application: RadioPad, Inc. (TEAMID)" \
         RadioPad.app
```

### Notarization (notarytool)

```bash
xcrun notarytool submit RadioPad.dmg \
      --apple-id "$APPLE_ID" \
      --team-id "$TEAM_ID" \
      --password "$NOTARY_PWD" \
      --wait
RC=$?
if [ $RC -ne 0 ]; then
  xcrun notarytool log <submission-id> \
        --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --password "$NOTARY_PWD"
  exit $RC
fi
xcrun stapler staple RadioPad.dmg
```

Exit codes:

| Code | Meaning |
| ---- | ------- |
| 0    | Notarized, ticket attached. |
| 65   | Unauthorized (bad credentials). |
| 75   | Submission rejected — pull `notarytool log`. |

### Gatekeeper verification

```bash
codesign --verify --deep --strict --verbose=2 RadioPad.app
spctl -a -t exec -vv RadioPad.app
```

Both must exit 0. The CI verify job aborts on any non-zero.

---

## 3. Linux (deb + rpm + AppImage)

### .deb — `dpkg-sig`

```bash
gpg --import release-signing.asc
dpkg-sig --sign builder -k "$GPG_KEY_ID" radiopad_0.1.0_amd64.deb
dpkg-sig --verify radiopad_0.1.0_amd64.deb
```

The repository GPG key fingerprint is published at
`https://radiopad.com/keys/release.asc` and pinned in `docs/04-security/`.

### .rpm — `rpmsign`

```bash
rpm --import release-signing.asc
rpmsign --addsign --define "_gpg_name $GPG_KEY_ID" radiopad-0.1.0-1.x86_64.rpm
rpm --checksig radiopad-0.1.0-1.x86_64.rpm   # expects "digests signatures OK"
```

### AppImage — zsync update channel + ed25519

The AppImage is shipped with a zsync metafile (`radiopad.AppImage.zsync`) for
delta updates. We additionally ship a detached GPG signature
(`radiopad.AppImage.sig`) generated with:

```bash
gpg --detach-sign --armor --output radiopad.AppImage.sig radiopad.AppImage
gpg --verify radiopad.AppImage.sig radiopad.AppImage
```

For the embedded AppImage updater, an **ed25519** key signs the update manifest
(`appimagetool --sign --sign-key "$ED25519_KEY"`).

---

## 4. CI verification

`.github/workflows/desktop-installer-verify.yml` is triggered via
`workflow_run` on `desktop-release.yml`. It downloads the published artifacts
on a matching matrix runner and re-runs the verify commands above. A failure
on any leg blocks the release from being marked `published: true`.

---

## 5. Secret inventory

| Secret | Used by |
| ------ | ------- |
| `RADIOPAD_WIN_CERT_THUMBPRINT` | Windows signtool / Tauri |
| `RADIOPAD_WIN_HSM_PIN`         | Windows signtool (HSM PIN) |
| `RADIOPAD_MAC_P12`             | macOS codesign import |
| `RADIOPAD_MAC_P12_PASSWORD`    | macOS codesign import |
| `RADIOPAD_MAC_NOTARY_APPLE_ID` | notarytool |
| `RADIOPAD_MAC_NOTARY_TEAM_ID`  | notarytool |
| `RADIOPAD_MAC_NOTARY_PASSWORD` | notarytool app-specific password |
| `RADIOPAD_GPG_KEY_ID`          | dpkg-sig / rpmsign / AppImage |
| `RADIOPAD_GPG_PRIVATE_KEY`     | dpkg-sig / rpmsign / AppImage |
| `RADIOPAD_APPIMAGE_ED25519`    | AppImage updater manifest |

None of these are stored in the repository.
