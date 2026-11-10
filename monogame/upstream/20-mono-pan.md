> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI. The fix itself was found and written by a person
> (Gabriel Asafe), but this note was written by an AI agent, so what can go upstream is an issue or a fix written by a
> human. The facts below are the material for that.

# A mono voice panned to the centre or left inverts its right channel

**Where:** `native/monogame/common/mg_audio.cpp`, `MGA_Voice_CalculatePanMatrix` (MonoGame v3.8.6).

**Problem:** for a one-channel source the right output gain is `(pan <= 0 ? (-pan - 1.f) : 1.f) * scale`: -1 at
`pan = 0`, -0.5 at `pan = -0.5`, 0 at `pan = -1`. A centred mono sound plays its right channel with the phase inverted
(it cancels where the speakers sum to mono), and panning left raises the right channel's magnitude instead of lowering
it.

**Fix:** `(pan <= 0 ? (pan + 1.f) : 1.f) * scale`, the mirror of the left channel's `(pan >= 0 ? (1.f - pan) : 1.f)`.
