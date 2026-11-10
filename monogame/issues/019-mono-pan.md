# 019. A panned mono sound keeps its right channel's phase

Part: runtime (audio).

## Symptom

A mono `SoundEffectInstance` with `Pan` at 0 or to the left played its right channel with the phase inverted, and to
the left louder instead of quieter: at the centre the two channels cancelled on speakers summing to mono, and a sound
moving left grew in the right ear.

## Cause

`MGA_Voice_CalculatePanMatrix` (`src/common/mg_audio.cpp`) gave the right channel `-pan - 1` for `pan <= 0`: -1 at the
centre, -0.5 at -0.5, 0 at -1. The left channel's mirror is `pan + 1` (1 at the centre, 0 at -1).

## Change

The right channel of a mono source is `pan + 1` for `pan <= 0` (1 otherwise), times the volume scale. The stereo
matrix was already right.

Found and fixed by Gabriel Asafe.

## Test

The matrix at pan -1, -0.5, 0, 0.5 and 1 gives left 1, 1, 1, 0.5, 0 and right 0, 0.5, 1, 1, 1. The runtime builds on
Windows (`build-windows.ps1`); the sound was not listened to here, and Linux and macOS were not rebuilt.
