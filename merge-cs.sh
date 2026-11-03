#!/usr/bin/env bash
# merge-cs.sh - merges all .cs files of one project into a single .cs file.
# Run from the solution root:  ./merge-cs.sh VadesDev.Rag
# Output: <solution root>/VadesDev.Rag.cs

set -euo pipefail

# ---------- CONFIG ----------
EXCLUDED_DIRS=(
  "bin"
  "obj"
  ".git"
  ".vs"
  ".idea"
  "node_modules"
  "packages"
  "TestResults"
)

EXCLUDED_FILES=(
  "*.g.cs"
  "*.g.i.cs"
  "*.Designer.cs"
  "AssemblyInfo.cs"
  "*.AssemblyAttributes.cs"
  "GlobalUsings.g.cs"
)
# ----------------------------

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <ProjectName>" >&2
  exit 1
fi

PROJECT_NAME="$1"
SOLUTION_ROOT="$(pwd)"
OUTPUT_FILE="$SOLUTION_ROOT/$PROJECT_NAME.cs"

# Prune expression for excluded dirs
dir_prune=()
for d in "${EXCLUDED_DIRS[@]}"; do
  [[ ${#dir_prune[@]} -gt 0 ]] && dir_prune+=(-o)
  dir_prune+=(-name "$d")
done

# Exclusion expression for excluded files
file_excl=()
for f in "${EXCLUDED_FILES[@]}"; do
  file_excl+=(! -name "$f")
done

# Locate project directory: ./<ProjectName> first, otherwise via <ProjectName>.csproj
if [[ -d "$SOLUTION_ROOT/$PROJECT_NAME" ]]; then
  PROJECT_DIR="$SOLUTION_ROOT/$PROJECT_NAME"
else
  csproj="$(find "$SOLUTION_ROOT" -type d \( "${dir_prune[@]}" \) -prune -o \
            -type f -name "$PROJECT_NAME.csproj" -print -quit)"
  if [[ -z "$csproj" ]]; then
    echo "Project '$PROJECT_NAME' not found in $SOLUTION_ROOT" >&2
    exit 1
  fi
  PROJECT_DIR="$(dirname "$csproj")"
fi

: > "$OUTPUT_FILE"
count=0

while IFS= read -r -d '' file; do
  rel="${file#"$PROJECT_DIR"/}"
  {
    printf '// Path: %s/%s\n' "$PROJECT_NAME" "$rel"
    # strip UTF-8 BOM, normalize CRLF -> LF
    sed -e '1s/^\xEF\xBB\xBF//' -e 's/\r$//' "$file"
    printf '\n\n'
  } >> "$OUTPUT_FILE"
  count=$((count + 1))
done < <(
  find "$PROJECT_DIR" -type d \( "${dir_prune[@]}" \) -prune -o \
    -type f -name '*.cs' "${file_excl[@]}" -print0 | sort -z
)

echo "Merged $count file(s) from '$PROJECT_NAME' into: $OUTPUT_FILE"