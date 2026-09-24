# Package Conventions

This package is its own git repository.

## Versioning

`X.Y.Z` in `package.json`.

- `X` — major: incompatible change.
- `Y` — minor: backward-compatible feature.
- `Z` — patch: backward-compatible change, including documentation, comments and other non-functional changes.

The version appears in three places: `package.json`, `README.md` and `README.zh.md`, the latter two as a version badge near the top, labelled in that file's own language (`![version]`, `![版本]`). Update all three together; a mismatch is a defect to correct.

## README

`README.md` and `README.zh.md` are one document in two languages — an edit to either is an edit to both.

## Files

Create the file; Unity creates its `.meta` on the next import. Unity picks the GUID and the importer block for the asset type, so a hand-written one is a different format and a duplicate GUID risk.

Move or rename an asset and its `.meta` goes along — same operation, same new name, contents left as they were.
