# Byte-equivalence fixtures (#1498)

What today's workflow engine (specVersion 30) produces, frozen as in-repo files. Every file here
is read as exact bytes: `.gitattributes` holds `* -text`, so git never normalises a newline.

## `vendored/`

The thirteen public workflow packages, each vendored from the tag that pins it in the
`consultologist-workflows` repo, as git holds the files (`manifest.json`, `dag.mmd`, prompts,
macros, snippets, everything under `packages/<name>/` at the tag). `test/general` sits at
`vendored/test/general/`.

`vendored/index.json` records, per package, the tag, the commit it resolved to, and the SHA-256 of
every file. The tags are the pins (the list lives in the script); the index is what CI checks:
`VendoredPackagesTests` fails if a file's bytes differ from its hash, or a file is on disk that
the index does not name.

Regenerate from a checkout of the content repo that has the tags:

```
dotnet run -v q --file scripts/vendor-public-packages.cs -- <path to consultologist-workflows> [<fixture root>]
```

The script deletes `vendored/` first, so a file removed upstream cannot linger, and writes nothing
if a tag does not resolve. Move a pin by editing the tag list in the script, then re-run it.
