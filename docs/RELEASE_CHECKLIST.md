# Public Release Checklist

Use this checklist before the first public push and before every release.

## Before creating a commit

- Keep tokens only in the application's encrypted runtime credential store.
- Do not put real tokens in source code, tests, screenshots, logs, examples, issue
  descriptions, commit messages, or build scripts.
- Run the full local scan:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\scan-secrets.ps1 -All
  ```

- After `git init`, enable the repository-owned pre-commit hook once:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\enable-git-hooks.ps1
  ```

## Before pushing

- Review every staged path with `git status --short`.
- Review staged content with `git diff --cached`.
- Confirm that `.env`, `.dart_tool`, `.flutter-plugins-dependencies`, runtime YAML,
  logs, credential stores, build output, and the local core executable are absent.
- Run `scripts/scan-secrets.ps1 -Staged` explicitly if the commit hook was not
  enabled.
- Do not use `git add -f`, `--no-verify`, or a Gitleaks bypass to force a warning
  through without investigating it.

## GitHub repository settings

- Enable Secret scanning and Push protection under repository security settings.
- Do not allow routine push-protection bypasses.
- Require the `Secret scan` workflow to pass before merging into the default branch.
- Use GitHub Actions secrets for release signing credentials; never store signing
  material in the repository or workflow YAML.

## If a scan detects a real token

Stop the release. Revoke or rotate the token first, then remove it from files and
history. A deleted commit, force-push, or private-to-public repository transition
does not make an exposed credential trustworthy again.
