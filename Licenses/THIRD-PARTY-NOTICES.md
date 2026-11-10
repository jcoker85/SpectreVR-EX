# Third-party notices

Spectre VR-EX contains none of the original Spectre VR (1993). Its game data is built on the player's own
machine from their own copy. Spectre VR-EX's own code is under the MIT licence (`LICENSE`). The following
third-party works ship with Spectre VR-EX or are built into it, under their own licences.

## FluidSynth (shipped in `Setup/FluidSynth`)

FluidSynth 2.6.1, the software synthesiser the first-run setup uses to render the game's MIDI soundtrack.
Copyright (C) 2000-2026 Peter Hanappe and others. Licensed under the GNU Lesser General Public License
version 2.1 (`LGPL-2.1.txt`). The unmodified Windows release binaries are included; the source code is at
<https://github.com/FluidSynth/fluidsynth>.

Its binaries include:

- **libsndfile** (`sndfile.dll`), copyright Erik de Castro Lopo and others, LGPL 2.1 (`LGPL-2.1.txt`).
  Source: <https://github.com/libsndfile/libsndfile>. It contains the Xiph.Org libogg and libvorbis
  libraries, under the BSD licence (<https://xiph.org/licenses/bsd/>).
- **SDL3** (`SDL3.dll`), copyright Sam Lantinga, under the zlib licence (<https://www.libsdl.org/license.php>).

## GeneralUser GS (shipped in `Setup`)

GeneralUser GS v2.0.3 by S. Christian Collins, the General MIDI SoundFont the soundtrack is rendered with.
Licence in `GeneralUser-GS-LICENSE.txt`. <https://schristiancollins.com/generaluser.php>

## DSEG font

DSEG14 Classic by Keshikan, used for the HUD and menus. SIL Open Font License 1.1
(`Assets/Fonts/DSEG-LICENSE.txt`, shipped as `DSEG-LICENSE.txt`). <https://www.keshikan.net/fonts-e.html>

## Star skybox

The star skybox (`Assets/Resources/Skybox/Stars.cubemap`) is made from the HYG star database by David Nash
(<https://astronexus.com>), licensed under CC BY-SA 2.5 (<https://creativecommons.org/licenses/by-sa/2.5/>).
The skybox is shared under the same licence.

## Unity

Built with Unity 6.3 and the High Definition Render Pipeline. The Unity runtime in the release
(`UnityPlayer.dll`, `MonoBleedingEdge`, `Spectre VR-EX_Data`) is distributed under the Unity Terms of
Service. Unity is a trademark of Unity Technologies.

The Unity runtime also includes:

- **NVIDIA DLSS** (`nvngx_dlss.dll`), copyright NVIDIA Corporation, redistributed with the Unity runtime
  under the NVIDIA DLSS SDK licence. NVIDIA and DLSS are trademarks of NVIDIA Corporation.
- **DirectML** (`DirectML.dll`), copyright Microsoft Corporation, redistributed with the Unity runtime.
- **Mono** (`MonoBleedingEdge`), the .NET runtime, under the MIT licence and others
  (<https://github.com/mono/mono>).
