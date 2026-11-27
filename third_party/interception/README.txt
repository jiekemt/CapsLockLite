interception.dll (x64) -- vendored third-party binary
===================================================

What it is : user-mode client library for the Interception keyboard/mouse
             filter driver. CapsLock Lite loads it on demand for the optional
             "driver injection" backend and ships it next to the executable.
Source     : https://github.com/oblitum/Interception (release v1.0.1)
File       : library/x64/interception.dll from Interception.zip
Version    : 1.0.1
SHA-256    : ab88164c11b1b48488772d4c3bfaa4509d5b0ae9dbc5a691dc4f96f0260443c8
License    : LGPL 3.0 for non-commercial use (see
             interception-LICENSE-LGPL-3.0.txt); commercial use requires a
             separate license from the author. The binary is redistributed
             unmodified; the corresponding source is the upstream repository
             above.

The kernel driver itself (keyboard.sys / mouse.sys) and its installer are NOT
bundled: the user installs Interception separately. This DLL does nothing
without the driver, and the app falls back to SendInput when it is missing.
