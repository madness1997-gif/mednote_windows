# M5.1/M5.2 — shutdown and Google Drive

## Shutdown contract

`AppWindow.Closing` starts one coordinated shutdown and cancels the first close
request while local work drains. The coordinator has one three-second deadline:

1. cancel OAuth/Drive network work without waiting for it;
2. write `shutdown-recovery.json`;
3. drain queued Note/PDF integration, flush the active RTF Sheet, workspace
   preferences and Reader position;
4. detach UI controllers and dispose native resources;
5. remove the recovery journal only after all local work completes.

A timeout or failure never leaves Windows waiting indefinitely. The window is
closed and the journal remains. On the next successful native-library load the
app reports the interrupted close and removes the marker. The repository's
atomic manifest and immutable Sheet blobs remain the recovery boundary.

## OAuth and credential boundary

The Drive button accepts a Google OAuth JSON file whose root contains
`installed`. Web-client JSON is rejected. Authorization uses the system browser,
a random `127.0.0.1` port, a random state value and PKCE S256. The only requested
scope is:

```text
https://www.googleapis.com/auth/drive.appdata
```

Client Secret (when Google supplies one) and refresh token are serialized into
one Generic Credential in Windows Credential Manager. They are never written to
the repository or normal JSON settings. Access tokens live only in memory. The
context menu on the Drive button can replace the OAuth client or revoke and
delete the credential; neither action deletes local or remote library data.

## Native manifest v2

The canonical remote file is `MedNote Native Library v2.json` inside
`appDataFolder`. It deliberately uses `mednote-native-library-v2`, not the web
v6 format, because native Sheet bodies are RTF. Each manifest includes:

- the complete native snapshot;
- one SHA-256 hash per UTF-8 RTF Sheet;
- a canonical-JSON SHA-256 hash for the complete library;
- native schema and manifest versions plus export time.

Downloads are parsed, validated and hash-checked before
`FileNoteRepository.ReplaceLibraryAsync` stages, reloads and atomically commits
them. Uploads replace an existing file only with its latest HTTP ETag in
`If-Match`; HTTP 412 is surfaced and never retried as an overwrite.

The local sync state records the remote file ID, ETag and last synchronized
library hash. When only one side changed it becomes authoritative. When both
sides changed—or no trustworthy baseline exists—the app archives both manifests
under `%LOCALAPPDATA%\MedNote Reader\sync-conflicts` and asks the user to keep the
machine copy, use the Drive copy, or defer. There is no timestamp-based silent
winner.
