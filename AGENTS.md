- Run git in this package's repository.

Versioning:
- Set `X.Y.Z` in `package.json`:
  - `X`: an incompatible change;
  - `Y`: a backward-compatible feature;
  - `Z`: a backward-compatible change, documentation, comments and other non-functional changes included.
- Bump the same version in three places: `package.json`, the `![version]` badge in `README.md`, the `![版本]` badge in `README.zh.md`, each badge labelled in its own file's language.
- Treat a mismatch between the three as a defect to correct.

README:
- Edit `README.md` and `README.zh.md` together: one document in two languages.

Files:
- Create the file and leave the `.meta` to Unity's next import; Unity picks the GUID and the importer block, so a hand-written one differs in format and risks a duplicate GUID.
- Move or rename an asset together with its `.meta`: same operation, same new name, contents unchanged.
