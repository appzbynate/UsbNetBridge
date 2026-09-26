# Publishing UsbNetBridge

You can sell the Android app on Google Play. The USB/IP stack is GPL-3, so the **corresponding source must stay public** (this GitHub repo + version tags). Charging for the app is allowed.

## You must do these first (local / GitHub)

1. **Git identity** — this machine cannot create a commit until Git knows who you are:

```bat
git config user.name "Your Name"
git config user.email "you@example.com"
git commit -m "Release UsbNetBridge 2.0.0 as GPL-3 with a Play-ready Android host."
git tag v2.0.0
```

2. **Log in to GitHub CLI**, create the public repo, and push:

```bat
gh auth login
gh repo create UsbNetBridge --public --source=. --remote=origin --push
git push origin v2.0.0
```

3. **GitHub Pages** — Settings → Pages → Deploy from branch `main`, folder `/docs`. Privacy URL for Play:

`https://appzbynate.github.io/UsbNetBridge/privacy.html`

4. **Back up** `android/upload-keystore.jks` and `android/keystore.properties` (they are not in git). Without them you cannot upload updates.

5. Attach `windows/publish/UsbNetBridge.Client-win-x64.zip` to the GitHub Release for `v2.0.0` (already built on this PC).

## You must do these in Play Console

1. Create the app (`com.usbnetbridge.server`).
2. Paste listing copy from [`play-listing.md`](play-listing.md). Upload graphic assets from [`play-assets/`](play-assets/README.md) (icon, feature graphic, phone screenshots). Promo video: upload `play-assets/video/promo-16x9.mp4` to YouTube and paste the URL.
3. Privacy policy URL — use the GitHub Pages URL above once Pages is on. Until then Play may accept:
   `https://github.com/appzbynate/UsbNetBridge/blob/main/PRIVACY.md`
4. **Foreground service / special use** — declare `specialUse` and paste the paragraph in `play-listing.md`.
5. **Data safety** — follow the Data safety section in `play-listing.md`.
6. Content rating questionnaire (typically Everyone / Tools).
7. Upload the signed **AAB** (not the debug APK).
8. Use Play App Signing (accept the prompt). Keep the upload keystore backed up.

## Build a Play bundle on this machine

```bat
cd android
gradlew.bat bundleRelease
```

Output: `android/app/build/outputs/bundle/release/app-release.aab`

Requires Android SDK 36 and NDK r28 (already installed on this machine).

## Windows client download

GitHub Release `v2.0.0` (or later) should include `UsbNetBridge.Client-win-x64.zip`. Users still install [usbip-win2](https://github.com/vadimgrn/usbip-win2/releases) once.
