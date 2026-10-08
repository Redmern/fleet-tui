#!/usr/bin/env sh
# Print the RELEASE_NOTES.md bullets for one version, as the GitHub release text.
#
#   scripts/release-notes.sh 0.6.0.27      (a leading v is fine too)
#
# A prerelease (0.7.0-rc.1) uses its own entry when there is one, else the entry
# of the version it leads to (0.7.0). Exits 1 when neither has an entry, so a
# release without notes fails. Entries under "# Earlier builds" (the old version
# numbers, from before the restart at 0.1.0) never match.
set -eu

file="${RELEASE_NOTES:-$(dirname "$0")/../RELEASE_NOTES.md}"
version="${1#v}"

entry() {
  awk -v want="$1" '
    { sub(/\r$/, "") }
    /^# Earlier builds/ { exit }
    /^## / { v = $2; inside = (v == want); next }
    inside && /^- / { print; found = 1 }
    inside && /^  / && found { print }
    END { exit found ? 0 : 1 }
  ' "$file"
}

entry "$version" || entry "${version%%-*}" || {
  echo "RELEASE_NOTES.md has no entry for $version; add '## $version (yyyy-mm-dd)' with its bullets" >&2
  exit 1
}
