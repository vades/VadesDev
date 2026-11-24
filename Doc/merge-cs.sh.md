# merge-cs.sh

Developer documentation for the script that merges all `.cs` files of one .NET project into a single file.

## 1. Purpose

`merge-cs.sh` collects every C# source file of a single project in a .NET solution and writes them into one `.cs` file named after the project. Each file is preceded by a `// Path:` comment so you can always tell where a piece of code came from.

Typical use cases:

- Pasting a whole project into an LLM or code-review tool as one context block.
- Producing a single-file snapshot of a project for archiving or sharing.
- Quickly reading a project top to bottom without opening many files.

> The merged file is meant for **reading and sharing**, not for compiling. Multiple `using` blocks and file-scoped namespaces in one file will not build.

## 2. Requirements

| Requirement | Details |
| --- | --- |
| OS | Windows 11 (also works on Linux/macOS) |
| Shell | Git Bash (Git for Windows) |
| Tools used | `bash`, `find`, `sort`, `sed`, `dirname`, `pwd` (all bundled with Git Bash) |
| Input | A solution root containing the project folder |

No .NET SDK is required. The script only reads text files.

## 3. Installation

1. Copy `merge-cs.sh` into the **solution root** (the folder that contains the `.sln` / `.slnx` file).
2. Make it executable (once):

```bash
chmod +x merge-cs.sh
```

Example layout:

```text
MySolution/                 <- solution root (run the script here)
├── MySolution.sln
├── merge-cs.sh
├── VadesDev.Rag/           <- project folder
│   ├── VadesDev.Rag.csproj
│   ├── Program.cs
│   ├── Models/
│   │   └── SearchHit.cs
│   └── Cli/
│       └── RagPipeline.cs
└── VadesDev.Rag.Tests/
```

## 4. Usage

Run from the solution root with exactly one argument: the project name.

```bash
cd /c/path/to/MySolution
./merge-cs.sh VadesDev.Rag
```

Syntax:

```text
./merge-cs.sh <ProjectName>
```

| Argument | Required | Description |
| --- | --- | --- |
| `ProjectName` | Yes | Name of the project folder, or of the `.csproj` file without extension |

Running without an argument (or with more than one) prints a usage message and exits with code `1`.

### Output

The result is written to the solution root as `<ProjectName>.cs`:

```text
MySolution/
├── merge-cs.sh
├── VadesDev.Rag.cs         <- generated
└── VadesDev.Rag/
```

On success the script prints:

```text
Merged 3 file(s) from 'VadesDev.Rag' into: VadesDev.Rag.cs
```

If the output file already exists it is **overwritten** without prompting.

## 5. Output format

Every source file is written as a `// Path:` comment line followed by the file contents and two blank lines. Paths are relative to the project folder and prefixed with the project name.

```csharp
// Path: VadesDev.Rag/Cli/RagPipeline.cs
using System;

namespace VadesDev.Rag.Cli;

public class RagPipeline { }


// Path: VadesDev.Rag/Models/SearchHit.cs
namespace VadesDev.Rag.Models;

public record SearchHit(string Id, double Score);


// Path: VadesDev.Rag/Program.cs
Console.WriteLine("Hello");
```

Files are sorted alphabetically by full path, so the order is stable between runs.

While copying, the script also:

- removes a UTF-8 BOM from the first line of each file,
- converts Windows line endings (CRLF) to Unix line endings (LF).

## 6. Configuration

Both exclusion lists are plain Bash arrays at the top of the script. Edit them to fit your repository.

### 6.1 Excluded directories

```bash
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
```

- Matching is by **folder name** at **any depth** inside the project (for example `Models/obj` is also skipped).
- Excluded directories are pruned, so `find` never descends into them.
- To exclude another folder, add a new quoted entry, for example `"Migrations"`.

### 6.2 Excluded files

```bash
EXCLUDED_FILES=(
  "*.g.cs"
  "*.g.i.cs"
  "*.Designer.cs"
  "AssemblyInfo.cs"
  "*.AssemblyAttributes.cs"
  "GlobalUsings.g.cs"
)
```

- Entries are shell wildcard patterns matched against the **file name** only.
- These cover typical generated code: source generators, WinForms/WPF designers and assembly attributes.
- Only files ending in `.cs` are ever read, regardless of this list.

## 7. How it works

1. **Validate arguments.** Exactly one argument is required.
2. **Build the `find` expressions** from `EXCLUDED_DIRS` (prune rules) and `EXCLUDED_FILES` (`! -name` rules).
3. **Locate the project directory.**
   - If `./<ProjectName>` exists, it is used.
   - Otherwise the script searches the solution for `<ProjectName>.csproj` (excluded directories are skipped) and uses the folder containing it. This supports layouts such as `src/VadesDev.Rag/`.
   - If nothing is found, it prints an error and exits with code `1`.
4. **Truncate the output file** `<ProjectName>.cs` in the solution root.
5. **Find and sort** all matching `.cs` files using null-delimited output (`-print0` and `sort -z`), which is safe for paths with spaces.
6. **Append each file** to the output with its `// Path:` header, BOM removed and line endings normalized.
7. **Print a summary** with the number of merged files.

The output file lives in the solution root, outside the project folder, so the script can never merge its own output.

## 8. Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Wrong number of arguments, or project not found |
| other | An unexpected error (the script uses `set -euo pipefail`, so any failing command stops it) |

## 9. Examples

Project folder directly in the solution root:

```bash
./merge-cs.sh VadesDev.Rag
```

Project in a subfolder (found through its `.csproj`):

```bash
# Layout: src/VadesDev.Rag/VadesDev.Rag.csproj
./merge-cs.sh VadesDev.Rag
# Output: ./VadesDev.Rag.cs
```

Merge several projects in a row:

```bash
for p in VadesDev.Rag VadesDev.Core VadesDev.Api; do
  ./merge-cs.sh "$p"
done
```

Count lines in the result:

```bash
wc -l VadesDev.Rag.cs
```

## 10. Troubleshooting

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| `Usage: ./merge-cs.sh <ProjectName>` | No argument, or too many arguments | Pass exactly one project name |
| `Project 'X' not found` | Wrong name, wrong working directory, or the project sits in an excluded folder | Run from the solution root; check spelling and `EXCLUDED_DIRS` |
| `Permission denied` | Script is not executable | `chmod +x merge-cs.sh` |
| `bad interpreter` or `\r: command not found` | The script was saved with Windows (CRLF) line endings | Convert to LF: `sed -i 's/\r$//' merge-cs.sh` (in VS Code, set the line ending to LF) |
| `Merged 0 file(s)` | All files were excluded, or the folder has no `.cs` files | Review both exclusion lists |
| A needed file is missing from the output | It matches an `EXCLUDED_FILES` pattern, or lives in an excluded directory | Remove or narrow the pattern |
| Output order looks odd | `sort` follows the shell locale | Run with `LC_ALL=C ./merge-cs.sh <ProjectName>` for plain byte ordering |

## 11. Limitations

- Only one project is processed per run.
- The project is identified by name, not by parsing the `.csproj`. Files linked from outside the project folder or added through custom `<Compile Include>` items are not picked up.
- Excluded directory names apply at every depth, so a legitimate folder named `packages` inside the project is skipped too.
- The output is not buildable code (see the note in section 1).
- Very large projects produce very large files, so check the size before pasting into a tool with context limits.

## 12. Maintenance tips

- Keep the script in the repository root and commit it, so the whole team uses the same exclusion lists.
- Add the generated `*.cs` files in the solution root to `.gitignore` (for example `/VadesDev.Rag.cs`) so they are not committed by accident.
- Keep the script saved with LF line endings. A `.gitattributes` entry helps:

```text
*.sh text eol=lf
```
