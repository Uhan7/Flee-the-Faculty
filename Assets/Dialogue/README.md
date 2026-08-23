# Dialogue System

## Add a conversation

1. In the Project window, choose `Create > Dialogue > Conversation`.
2. Add speaker/text entries to the asset's `Lines` list.
3. Add `DialogueTrigger` to a GameObject with a 2D collider.
4. Assign the conversation asset and, optionally, the player Transform as `Activator`.

The trigger collider becomes a trigger automatically. Leave `Dialogue Manager` empty to use the shared runtime manager and temporary on-screen view.

## Trigger options

- `Start On Enable`: play without requiring a collider to enter.
- `Play Once`: prevent the trigger from replaying until `ResetTrigger()` is called.
- `Close On Exit`: close this conversation when the activator leaves.

## Extend it

Implement `IDialogueView` on another MonoBehaviour and assign it to a scene `DialogueManager` to replace the temporary UI. Gameplay systems can subscribe to `DialogueStarted`, `LineChanged`, and `DialogueEnded` without modifying the dialogue runner.
