Sidetone Control for HyperX 1.2.0
=================================

Adjust the sidetone (mic monitoring) volume of the HyperX Cloud III S Wireless
from -6 dB to +6 dB. NGENUITY only offers an on/off switch for it.

The level is set inside the headset itself, the same way NGENUITY does it.
There is no extra delay, unlike Windows "Listen to this device" or VoiceMeeter.


Requirements
------------
- Windows 10 (1809 or newer) or Windows 11
- HyperX NGENUITY desktop app (from hyperx.com), installed and running.
  Sidetone Control for HyperX talks to the headset through NGENUITY's
  background service.
- HyperX Cloud III S Wireless connected through its USB dongle


How to use
----------
1. Start NGENUITY and switch on the headset.
2. Run SidetoneControl.exe. No installation is needed.
3. Move the slider. Your voice gets quieter or louder right away.

- "Mic monitoring" switches sidetone on or off.
- "Start with Windows" starts the app in the tray when you log in.
- Closing the window keeps the app in the tray. Right-click the tray icon
  and choose Exit to quit.

The headset forgets the sidetone level whenever it is switched off. The app
remembers your level and sets it again when the headset reconnects, as long as
it is running (in the tray is enough).


Privacy
-------
The app collects no data, has no telemetry and makes no internet connections.
It only connects to NGENUITY's own service on this PC (127.0.0.1).


Notes
-----
- Windows SmartScreen may warn about an unknown publisher because this
  download is not code-signed. Choose "More info" and then "Run anyway".
  The Microsoft Store version does not show this warning.
- Settings are stored in %APPDATA%\SidetoneControl. A helper is unpacked to
  %LOCALAPPDATA%\SidetoneControl.
- Uninstall: untick "Start with Windows", exit the app, then delete
  SidetoneControl.exe and the two folders above.
- A NGENUITY update could change its internals and break the app. If that
  happens, the status line shows an error and NGENUITY keeps working normally.


Disclaimer
----------
Unofficial community tool. Not affiliated with, endorsed by, or supported by
HyperX or HP Inc. HyperX and NGENUITY are trademarks of HP Inc. Use at your own
risk.
