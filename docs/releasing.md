# Releasing

How to cut a Nook release. The workflow is `.github/workflows/release.yml`.

## Cutting a release

1. On a branch, set `<Version>` in `Nook.csproj` to the new version (`X.Y.Z`) and add `docs/releases/vX.Y.Z.md`, the release body. Both go in the same pull request.
2. Merge to `main`.
3. Tag the merged commit with an annotated tag and push it:

   ```
   git tag -a vX.Y.Z -m "Nook X.Y.Z" <commit>
   git push origin vX.Y.Z
   ```

4. The tag push starts the `release` workflow. It creates a draft, checks it, and publishes it as the latest release.

The release body file must not contain the SHA-256 line. The workflow appends `SHA-256 \`<hash>\`` itself.

## What the workflow checks

Every check that can fail cheaply runs before anything is built or created:

1. `Nook.csproj` `<Version>` equals the tag without its `v`.
2. The tagged commit is an ancestor of `origin/main`.
3. `docs/releases/vX.Y.Z.md` exists and is not empty.
4. No release for the tag exists yet.

Then it restores, builds with warnings as errors, runs the tests, and publishes a single-file `win-x64` build. Then it:

5. Checks that the file version of `Nook.exe` matches the version.
6. Creates a build provenance attestation for `Nook.exe`.
7. Creates a **draft** release with `Nook.exe` and the notes.
8. Checks that the draft's asset digest equals the SHA-256 of the built file. If it does not, the run stops and the draft stays unpublished.
9. Publishes the release as the latest.
10. Downloads the published `Nook.exe`, checks that it matches the built file, and runs `gh attestation verify`.

## Failed runs

Releases are immutable and `v*` tags cannot be moved or deleted. A failed run cannot be repaired in place. Fix the problem and release the next patch version.

- A run that fails before step 7 has created nothing. Re-running it is fine for a transient error such as a network failure. A failed check needs a fix and the next version, because the tag already exists and cannot be moved.
- A run that stops at step 8 leaves a draft release. Inspect it, then delete it if it is wrong. The tag stays, so the same version cannot be released again: use the next version.
- A failure at step 10 happens after the release is public. Treat that release as bad and release a new version.

## Dry run

Run the workflow manually from the Actions tab, or:

```
gh workflow run release.yml
```

A dry run builds, tests and checks the version, but creates nothing. It uploads `Nook.exe` and `notes.md` as the `Nook-release-dry-run` artifact for inspection. It also runs on pull requests that change the workflow or `docs/releases/`.

## Verifying a download

```
gh attestation verify Nook.exe -R IACBI/Nook
```
