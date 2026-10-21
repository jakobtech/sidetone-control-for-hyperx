# Publishing: Sidetone Control for HyperX 1.2.0

The release folder holds:

| File | Use |
|---|---|
| `SidetoneControlForHyperX-1.2.0.zip` | Direct download (GitHub release, website). Contains `SidetoneControl.exe` and `README.txt`. |
| `SidetoneControlForHyperX-1.2.0.0-x64.msix` | Microsoft Store upload. **Rebuild it after step 1 below**, because it still has placeholder identity values. |
| `store-screenshot-1920x1080.png` | Store listing screenshot. |
| `PRIVACY.md` | Privacy policy text. Host it somewhere public, such as a GitHub repo, and paste the URL into Partner Center. |
| `SHA256SUMS.txt` | Checksums, so downloaders can verify the files. |

## 1. Partner Center (one-time)
1. Register as an **individual developer**: https://storedeveloper.microsoft.com
2. Partner Center → **Apps and games** → **New product** → **MSIX or PWA app**. Reserve the name **Sidetone Control for HyperX**.
3. Open the app → **Product management** → **Product identity**. Copy these three values into `msix\store.config.json`:
   - `Package/Identity/Name` → `IdentityName`
   - `Package/Identity/Publisher` → `Publisher` (starts with `CN=`)
   - `Package/Properties/PublisherDisplayName` → `PublisherDisplayName`
4. Rebuild: `powershell -ExecutionPolicy Bypass -File build-msix.ps1`. Upload the new `.msix` from `dist\`. Leave it unsigned; the Store signs it.

For every update, raise the version in `src\App.cs`, `build.ps1` and `build-msix.ps1`. The Store version must end in `.0`.

## 2. Submission fields
- **Pricing:** Free. Markets: all.
- **Category:** Utilities & tools.
- **Privacy policy URL:** the hosted `PRIVACY.md`.
- **System requirements:** HyperX NGENUITY desktop app and a HyperX Cloud III S Wireless headset.
- **Age rating:** answer "no" to every question.
- **Restricted capability `runFullTrust`.** Justification:
  > Sidetone Control for HyperX is a Win32 desktop app (Windows Forms). It needs full trust to start a helper process
  > that talks to the locally installed HyperX NGENUITY service on localhost. That service applies
  > the sidetone level to the headset. The app makes no network connections beyond localhost and collects no data.

## 3. Store listing (English)
**Description**
> Adjust the sidetone (mic monitoring) volume of your HyperX Cloud III S Wireless from -6 dB to +6 dB.
> HyperX NGENUITY only offers an on/off switch for mic monitoring. This app adds the missing volume slider.
>
> The level is applied inside the headset itself, exactly like NGENUITY does it. There is no added
> latency, unlike software monitoring such as "Listen to this device".
>
> - Sidetone volume from -6 dB to +6 dB
> - Turn mic monitoring on or off
> - Battery level at a glance
> - Optional start with Windows: runs quietly in the tray and restores your level whenever the headset reconnects
>
> Requires the HyperX NGENUITY desktop app from hyperx.com, installed and running, and a HyperX Cloud III S Wireless headset.
>
> Unofficial community tool. Not affiliated with, endorsed by, or supported by HyperX or HP Inc.
> HyperX and NGENUITY are trademarks of HP Inc.

**Short description**
> Sidetone volume slider for the HyperX Cloud III S Wireless.

**Notes for certification**
> This app controls a hardware feature of the HyperX Cloud III S Wireless headset through the locally
> installed HyperX NGENUITY desktop app (from hyperx.com). Without NGENUITY or the headset, the app starts and
> shows "Waiting for NGENUITY and the headset dongle…" or "HyperX NGENUITY is not installed." It does not crash,
> and all controls are disabled until a headset is connected.

## Risks
- **Trademark in the app name (highest risk).** "for HyperX" puts a third-party trademark in the product name.
  Microsoft Store policy forbids names that could be confused with another company's product, and HP can file a
  complaint even after approval. If certification rejects the name, use a neutral name, for example
  "Sidetone Control for Cloud III S", or ask HP/HyperX for permission.
- **Only the NGENUITY installer from hyperx.com has been tested.** The app looks for NGENUITY in
  `%LOCALAPPDATA%\NGENUITY\current`.
- **NGENUITY updates** can break the connection. The app then shows an error and does not crash, but needs an update.
