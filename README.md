# Aurora Audio

![license](https://img.shields.io/github/license/NOMOAX/aurora.audio)
![version](https://img.shields.io/badge/version-2.0.4-blue)
![lowest Unity version](https://img.shields.io/badge/Unity-2021.2%2B-blue)

Audio abstraction layer that manages async loading, playback lifetime and event dispatch.

English | [中文](README.zh.md)

## Dependencies

- [Aurora](https://github.com/NOMOAX/aurora.git)

## Installation

1. Open Unity package manager.
2. Click the `+` button in the upper-left corner, then select `Add package from git URL...`.
3. Input `https://github.com/NOMOAX/aurora.audio.git` and then click the `Add` button.

## Overview

Three types make up the whole package, and the identifier type `T` threads through all of them. `T` is whatever identifies an audio file in your project — a relative asset path, a GUID, an address, or a type of your own that carries the data the load needs. The derived types decide that; the package only requires `IEquatable<T>`, because it uses `T` as a dictionary key.

| Type              | Role                                                                            |
|-------------------|---------------------------------------------------------------------------------|
| `AudioManager<T>` | Loads sounds, caches them, creates and polls playbacks, releases them           |
| `Sound<T>`        | Loaded audio data, ready to be played; can back many playbacks at the same time |
| `Playback<T>`     | One independent playback of one sound: status, volume, position, and events     |

```text
AudioManager<T>
├── Sound<T>          (loaded from the identifier T, cached by it, reference counted by playbacks)
│   ├── Playback<T>
│   ├── Playback<T>
│   └── ...
└── Sound<T>
    └── Playback<T>
```

## Identifier Type

`T` is the type of the audio file identifier. It is declared by the derived manager, and every member that takes an identifier accepts that type.

A project normally has a single audio manager, so the examples below all use `string`. The best identifier type is a custom type of your own: a `struct` that implements `IEquatable<T>` and carries the data the load needs — a path, a bundle name — so that the manager can look a sound up by the same value it loaded it with.

```csharp
public sealed class GameAudioManager : AudioManager<string>
{
}
```

`Playback<T>.Id` is a number that distinguishes one playback from another. It is assigned by the manager from a counter that is **static per identifier type**: as long as two managers share the same `T`, their playback identifiers never collide. The value is also used as the name of the object a Unity playback creates, so it can be found in the Hierarchy window while debugging.

## AudioManager\<T\>

`AudioManager<T>` is the entry point. It is abstract, implements `IDisposable`, and is **not guaranteed to be thread safe** — call it from a single thread (in Unity, from the main thread).

```csharp
var audioManager = new GameAudioManager();

var isDisposed = audioManager.IsDisposed;
```

### Loading a Sound

`GetSoundAsync` gets the sound identified by `id`, loading it when it is not loaded yet. The result is cached: a second call with the same identifier returns the same `Sound<T>` instance, and concurrent calls share the load that is already in flight rather than starting another one.

```csharp
var sound = await audioManager.GetSoundAsync("Sounds/click.wav");

// Cancellable; the cancellation cancels the underlying load as well
var sound2 = await audioManager.GetSoundAsync("Sounds/confirm.wav", cancellationToken);
```

The overload without a `CancellationToken` cannot be cancelled by the caller: its load is only cancelled when the manager is disposed.

A load that faults or is cancelled is **not** cached — the entry is removed, so the identifier can be retried later. When the derived manager completes with `null`, or with a sound whose `Id` is not the identifier that was requested, the task faults with `InvalidOperationException` instead of caching a wrong result.

```csharp
audioManager.GetSoundAsync(null); // ArgumentNullException
audioManager.GetSoundAsync("Sounds/click.wav"); // ObjectDisposedException after the manager is disposed
```

### Creating a Playback

A `Sound<T>` is loaded audio data, not a playing sound; `CreatePlayback` wraps it in a `Playback<T>` that can be played, paused, stopped, seeked and observed. A sound can back any number of playbacks, concurrently.

```csharp
var playback = audioManager.CreatePlayback(sound); // Created in the PlaybackStatus.None status
```

The manager records the initial status, volume, position and normalized position of the created playback, so the first `Update` does not report them as changes.

```csharp
audioManager.CreatePlayback(null); // ArgumentNullException
audioManager.CreatePlayback(foreignSound); // KeyNotFoundException when the sound was not loaded by this manager
audioManager.CreatePlayback(disposedSound); // ObjectDisposedException
```

### Playing

The `Play` overloads create a playback and start it immediately.

```csharp
var playback = audioManager.Play(sound);
var playback2 = audioManager.Play(sound, 0.5); // At half volume
```

When the sound is not loaded yet, the `PlayAsync` overloads load it first, and complete with the playback once it is playing. There is one overload per combination of "with or without a volume" and "with or without a cancellation token", so every call site states both explicitly.

```csharp
var playback = await audioManager.PlayAsync("Sounds/click.wav");
var playback2 = await audioManager.PlayAsync("Sounds/click.wav", 0.5);
var playback3 = await audioManager.PlayAsync("Sounds/click.wav", cancellationToken);
var playback4 = await audioManager.PlayAsync("Sounds/click.wav", 0.5, cancellationToken);
```

A volume outside the range 0 to 1 (including `NaN`) throws `ArgumentOutOfRangeException`. When the task is cancelled, the sound load is cancelled together with it.

`BeginPlayAndForget` does the same as `PlayAsync` but returns `void` and does not hand the playback back. It is for cases where nothing needs to observe the playback: the playback is created with `AutoDisposeWhenStopped` set, so `Update` disposes it automatically once it stops, and the memory is reclaimed without the caller holding a reference.

```csharp
audioManager.BeginPlayAndForget("Sounds/click.wav");
audioManager.BeginPlayAndForget("Sounds/click.wav", 0.5);
audioManager.BeginPlayAndForget("Sounds/click.wav", cancellationToken);
audioManager.BeginPlayAndForget("Sounds/click.wav", 0.5, cancellationToken);
```

### Polling and Event Dispatch

`Update` polls every live playback, raises the change events, and releases the playbacks that are finished. **Call it once per frame, or at short intervals** — without it, no event is ever raised and no playback is ever released.

```csharp
public sealed class AudioManagerHost : MonoBehaviour
{
    private GameAudioManager _audioManager;

    private void Update()
    {
        _audioManager.Update();
    }
}
```

Each call works in two passes:

1. The status, volume, position and normalized position of every playback are read. When a value differs from the value read by the previous call, the corresponding event is raised. A handler may dispose the playback or the manager inside the event; the loop notices and stops polling that playback.
2. The playbacks that were disposed, and those whose `AutoDisposeWhenStopped` is `true` and whose `Status` became `PlaybackStatus.None` during the first pass, are released. Because releasing happens in the second pass, **a playback released by this call still reports its last changes**.

Releasing a playback decrements the playback count of its sound; when the count reaches zero, the sound is disposed as well. A loaded sound with no playback is left in the cache until `DisposeSound` or `Dispose` removes it.

`Update` is `virtual` and can be overridden; the override should call `ThrowIfDisposed` first and then the base implementation.

The active playbacks can be inspected from a derived manager through `GetPlaybacks`, which fills a dictionary keyed by `Playback<T>.Id`.

```csharp
protected override void Update()
{
    base.Update();
    var playbacks = new Dictionary<int, Playback<string>>();
    GetPlaybacks(playbacks);
}
```

### Unloading a Sound

`DisposeSound` disposes every active playback of the sound and unloads the sound itself, freeing the entry in the cache.

```csharp
audioManager.DisposeSound("Sounds/click.wav"); // KeyNotFoundException when the identifier is not loaded
```

A sound that is still loading cannot be unloaded; that throws `InvalidOperationException`.

### Disposal

Disposing the manager cancels every in-flight load, disposes every live playback and every loaded sound, and clears both caches. `IsDisposed` reports the state, and every member that needs the manager throws `ObjectDisposedException` once it is disposed.

### Implementing a Manager

A derived manager implements two members.

```csharp
public sealed class GameAudioManager : AudioManager<string>
{
    // Loads the audio file identified by id and wraps it in a sound
    protected override async Task<Sound<string>> CreateSoundAsync(string id, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); // Required when overriding
        var audioBytes = await ReadAudioFileAsync(id, cancellationToken);
        return new MySound(id, audioBytes);
    }

    // Creates a playback of a loaded sound
    protected override Playback<string> CreatePlaybackImpl(int id, Sound<string> sound)
    {
        ThrowIfDisposed(); // Required when overriding
        return new MyPlayback(id, sound);
    }
}
```

`CreateSoundAsync` and `CreatePlaybackImpl` are only called by the manager: the identifier is validated, the cache is maintained, and the returned instance is checked (returning `null`, or a playback whose status is not `PlaybackStatus.None`, throws `InvalidOperationException`).

## Sound\<T\>

`Sound<T>` is loaded audio data that is ready to be played. It implements `IDisposable` and holds the identifier it was loaded from.

```csharp
var id = sound.Id;
var length = sound.Length; // In seconds; the implementing type should keep it greater than 0
var isDisposed = sound.IsDisposed;
```

The instance is created by the manager when a load completes, and disposed by the manager when its playback count reaches zero, when `DisposeSound` is called, or when the manager itself is disposed. Disposing a sound nulls its resources, and every member except `IsDisposed` throws `ObjectDisposedException` afterwards.

A derived sound passes the identifier to the base constructor, which rejects `null` with `ArgumentNullException` — so the identifier type has to be a reference type, or a value type.

```csharp
public sealed class MySound : Sound<string>
{
    private byte[] _audioBytes;

    public MySound(string id, byte[] audioBytes) : base(id)
    {
        _audioBytes = audioBytes;
    }

    public override double Length => /* the length in seconds */;

    protected override void Dispose(bool disposing)
    {
        _audioBytes = null;
        base.Dispose(disposing);
    }
}
```

## Playback\<T\>

`Playback<T>` is one independent playback of one sound. Unlike a sound, which is data, a playback is state: it owns the current status, the volume and the position, and it raises events when they change.

### Properties

```csharp
var id = playback.Id; // The identifier assigned by the manager
var sound = playback.Sound; // The sound this playback was created from
var isDisposed = playback.IsDisposed;
var autoDisposeWhenStopped = playback.AutoDisposeWhenStopped; // Set by BeginPlayAndForget
```

```csharp
var status = playback.Status; // PlaybackStatus.None / Playing / Paused

var volume = playback.Volume; // 0 to 1
playback.Volume = 0.5;

var position = playback.Position; // In seconds
playback.Position = 3.5;

var normalizedPosition = playback.NormalizedPosition; // 0 is the beginning, 1 is the end
playback.NormalizedPosition = 0.5; // Seeking through the normalized position
```

`NormalizedPosition` is implemented on top of `Position` and `Sound.Length`; `Length` is read from the sound, so `NormalizedPosition` cannot be used after the sound is disposed.

### Transport

```csharp
playback.Play(); // Plays from the beginning in PlaybackStatus.None; resumes in PlaybackStatus.Paused
playback.Pause();
playback.Stop(); // Resets the position and returns to PlaybackStatus.None
```

The default `Stop` sets `Position` to 0 and then calls `Pause`. A derived playback can override it (usually to call into the underlying source directly), but must still ensure that `Status` becomes `PlaybackStatus.None` afterwards.

Every property and every transport method throws `ObjectDisposedException` once the playback is disposed. `Play`, `Pause` and `Stop` report the state they are in, so they can be called blindly without tracking the current status first.

### Events

Four events report what happened to the playback. They are **raised only from `AudioManager<T>.Update`**, and only when the polled value differs from the value read by the previous call — never as a direct consequence of calling `Play`, `Pause` or `Stop`.

```csharp
playback.StatusChanged += (sender, status) => Debug.Log($"status: {status}");
playback.VolumeChanged += (sender, volume) => Debug.Log($"volume: {volume}");
playback.PositionChanged += (sender, position) => Debug.Log($"position: {position}");
playback.NormalizedPositionChanged += (sender, normalizedPosition) => Debug.Log($"normalized position: {normalizedPosition}");

// The handler can be removed again
playback.StatusChanged -= onStatusChanged;
```

A handler is allowed to dispose the playback or the manager; `Update` notices and stops polling that playback. Disposing a playback detaches every handler.

## PlaybackStatus

`PlaybackStatus` describes the state of a playback.

| Value     | Meaning                                                                                                                           |
|-----------|-----------------------------------------------------------------------------------------------------------------------------------|
| `None`    | Not playing: it has not started yet, it was stopped by `Playback<T>.Stop`, or it reached the end of the sound (the default value) |
| `Playing` | Playing right now                                                                                                                 |
| `Paused`  | Paused, and able to resume from the current position                                                                              |

There is no separate "finished" state: reaching the end of the sound moves the playback back to `None`, which is what lets a play-and-forget playback be released automatically.
