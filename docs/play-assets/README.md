# Play Console visual assets

Upload these on **Grow → Store presence → Main store listing → Graphics**.

Play does not read this folder by itself. Drag the files into the matching fields.

| Play field | File | Spec |
| --- | --- | --- |
| App icon | `icon-512.png` | 512×512, 32-bit PNG |
| Feature graphic | `feature-graphic-1024x500.png` | 1024×500, no transparency |
| Phone screenshot 1 | `screenshots/phone-01-android.png` | 1080×1920, 9:16 — USB host, shared device highlighted |
| Phone screenshot 2 | `screenshots/phone-02-windows.png` | 1920×1080, 16:9 — Windows client, same device highlighted |
| 7-inch tablet (optional) | `screenshots/tablet-7-android.png` | 1440×2560, 9:16 |
| 10-inch tablet (optional) | `screenshots/tablet-10-windows.png` | 1920×1080, 16:9 |
| Promo video | `video/promo-16x9.mp4` | Upload to YouTube (unlisted is fine), then paste the URL. Play does not accept the MP4 in this form. |

`video/promo-9x16.mp4` is a vertical cut if you want a YouTube Short.

Phone shots are your real captures of **CX 2.4G Receiver** shared to the PC, with a Shared callout on both sides. Source photos are in `screenshots/source/`.

Regenerate icon / feature graphic:

```bat
powershell -File docs\play-assets\build-play-assets.ps1
```

Re-apply the Shared highlight after new captures (put JPGs in `screenshots/source/` first):

```bat
powershell -File docs\play-assets\annotate-screenshots.ps1
```
