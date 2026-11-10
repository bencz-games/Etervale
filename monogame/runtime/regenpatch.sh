#!/bin/bash
# Regenerates monogame-v3.8.6.patch after a change to src/: recovers the upstream files by reverse-applying the
# committed patch to the sources of the commit that last wrote it (where the two agree, even if later commits changed
# src/ without regenerating it), then diffs them against the working tree.
#
#   monogame/runtime/regenpatch.sh
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
base="$(git -C "$root" log -1 --format=%H -- monogame/runtime/monogame-v3.8.6.patch)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
files=(premake5.lua vulkan/MGG_Vulkan.cpp sdl/MGP_sdl.cpp include/api_enums.h faudio/MGA_faudio.cpp common/mg_audio.cpp)
mkdir -p "$tmp/a/native/monogame/common" "$tmp/b/native/monogame/common"
mkdir -p "$tmp/a/native/monogame/faudio" "$tmp/b/native/monogame/faudio" "$tmp/a/native/monogame/vulkan" "$tmp/b/native/monogame/vulkan" "$tmp/a/native/monogame/sdl" "$tmp/b/native/monogame/sdl" "$tmp/a/native/monogame/include" "$tmp/b/native/monogame/include"
for f in "${files[@]}"; do
  git -C "$root" show "$base:monogame/runtime/src/$f" > "$tmp/a/native/monogame/$f"
  cp "$here/src/$f" "$tmp/b/native/monogame/$f"
done
git -C "$root" show "$base:monogame/runtime/monogame-v3.8.6.patch" > "$tmp/committed.patch"
(cd "$tmp/a" && patch -s -R -p1 < "$tmp/committed.patch")
(cd "$tmp" && for f in "${files[@]}"; do diff -u --label "a/native/monogame/$f" --label "b/native/monogame/$f" "a/native/monogame/$f" "b/native/monogame/$f" || true; done) > "$here/monogame-v3.8.6.patch"
echo "wrote $here/monogame-v3.8.6.patch ($(wc -l < "$here/monogame-v3.8.6.patch") lines)"
