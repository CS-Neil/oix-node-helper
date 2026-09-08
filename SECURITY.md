# Security Policy

## Reporting a vulnerability

Please do not post access tokens, controller secrets, logs containing credentials,
or other private account data in a public issue, discussion, pull request, or
screenshot.

Use GitHub private vulnerability reporting when it is enabled for this repository.
If private reporting is unavailable, contact the maintainer privately using the
address listed on their GitHub profile and share only the minimum reproduction
information required.

## If a token was exposed

Treat a token as compromised as soon as it has been committed, pushed, pasted into
an issue, or included in a release artifact:

1. Revoke or rotate the token at its provider immediately.
2. Remove the value from the current files and release artifacts.
3. Review the complete Git history and open pull requests for additional copies.
4. Rewrite published history when appropriate, while remembering that rewriting
   history does not make the old token safe again.
5. Review account activity for unauthorized use.

Never rely on deleting a file or commit as a substitute for rotating a real token.
