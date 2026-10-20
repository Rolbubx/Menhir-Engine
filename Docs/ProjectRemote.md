# Project remotes (planned)

Status: **not implemented**. The Hub already asks for a remote when creating a project and saves the
options below in `ProjectSettings/ProjectSettings.json`; opening a project from a remote is disabled.
This page records how remotes are meant to work, so the implementation can follow it.

## Idea

A project created with a remote (`http://...`) can be shared: other people who have the owner's token
open it from the Hub (**Projects → Project's remote**), get the game on their machine, and stay in sync
with what each collaborator does.

```json
"remote": {
  "url": "http://my-server:8080",
  "onOwnerLeave": "TerminateAndSave",
  "assetDelivery": "EncryptedCopy"
}
```

Tokens and keys are never stored in this file: it is committed with the project.

## When the owner quits while others are working (`onOwnerLeave`)

| Value | Behavior |
|-------|----------|
| `TerminateWithoutSaving` | Collaborators' sessions end; their unsaved work is lost. Not recommended. |
| `TerminateAndSave` (default) | Sessions end after their work is saved. The remote keeps it in a GitHub repository, with a private key. |
| `HandOverToCollaborator` | They keep working; one of them becomes the *second admin*. When the owner reconnects while they are still connected, the owner gets their version. Not recommended. |

## How files reach collaborators (`assetDelivery`)

| Value | Behavior |
|-------|----------|
| `StreamedChunks` | Collaborators only get chunks, streamed as needed (security level 1). Not recommended. |
| `EncryptedCopy` (default) | Collaborators get an encrypted copy of the game and a secret key to decrypt it. Files uploaded later need a newer key. |

## Open questions

- **Server**: what runs at the remote URL (a Menhir server to host, a hosted service)? Its API decides
  the protocol (HTTP + WebSocket for live changes?).
- **Tokens**: who issues them, can they be revoked, are there read-only collaborators?
- **Sync granularity**: scene edits are already commands (undo/redo), so they could be sent as operations;
  assets would be sent as files (by GUID). What happens when two people edit the same thing?
- **GitHub saving**: which account owns the repository, and where does its private key live?
- **Second admin**: on reconnecting, is the owner's local version replaced, or merged?
- **Security**: whatever a collaborator's editor can display can be copied, so streaming and encryption
  make copying harder rather than impossible; a revoked key does not erase files already decrypted.
