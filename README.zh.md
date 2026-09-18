# Aurora Audio

![许可](https://img.shields.io/github/license/NOMOAX/aurora.audio)
![版本](https://img.shields.io/badge/version-2.0.3-blue)
![最低 Unity 版本](https://img.shields.io/badge/Unity-2021.2%2B-blue)

管理音频的异步加载、播放生命周期与事件分发的音频抽象层。

[English](README.md) | 中文

## 依赖

- [Aurora](https://github.com/NOMOAX/aurora.git)

## 安装

1. 打开 Unity package manager。
2. 点击左上角的 `+` 按钮，然后选择 `Add package from git URL...`。
3. 填入 `https://github.com/NOMOAX/aurora.audio.git` 并点击 `Add` 按钮。

## 概览

整个包由三个类型构成，标识符类型 `T` 贯穿其中。`T` 是音频文件在项目中的标识方式——资源相对路径、GUID、address，或由你自己定义的、携带加载所需数据的类型都可以，由派生类型自行决定；本包只要求它实现 `IEquatable<T>`，因为它被用作字典的键。

| 类型              | 职责                                                 |
|-------------------|------------------------------------------------------|
| `AudioManager<T>` | 加载音频、缓存、创建与轮询播放、释放播放与音频       |
| `Sound<T>`        | 已加载、可播放的音频数据，可同时支撑多个播放         |
| `Playback<T>`     | 某一次独立的播放：状态、音量、播放位置以及相应的事件 |

```text
AudioManager<T>
├── Sound<T>          （由标识符 T 加载，以 T 为键缓存，由播放计数引用）
│   ├── Playback<T>
│   ├── Playback<T>
│   └── ...
└── Sound<T>
    └── Playback<T>
```

## 标识符类型

`T` 是音频文件标识符的类型，由派生管理器决定，所有接收标识符的成员都使用该类型。

一个项目通常只有一个音频管理器，因此下面的示例统一使用 `string`。最佳的标识符类型是由你自己定义的类型：一个实现了 `IEquatable<T>`、并携带加载所需数据的 `struct`——路径、ab 包名等——这样管理器就能用与加载时相同的值查回对应的音频。

```csharp
public sealed class GameAudioManager : AudioManager<string>
{
}
```

`Playback<T>.Id` 是区分每一次播放的数字。它由管理器从一个 **按标识符类型静态共享** 的计数器分配：只要两个管理器使用相同的 `T`，它们的播放标识符就不会重复。该值同时被用作 Unity 播放所创建对象的名称，调试时可以直接在 Hierarchy 窗口中看到。

## AudioManager\<T\>

`AudioManager<T>` 是入口。它是抽象类、实现了 `IDisposable`，并且 **不保证线程安全**——请在单一线程（在 Unity 中是主线程）上调用。

```csharp
var audioManager = new GameAudioManager();

var isDisposed = audioManager.IsDisposed;
```

### 加载音频

`GetSoundAsync` 获取 `id` 对应的音频，尚未加载时进行加载。结果会被缓存：用相同标识符再次调用返回同一个 `Sound<T>` 实例；并发调用会共享已经在进行中的那次加载，而不会重复开始加载。

```csharp
var sound = await audioManager.GetSoundAsync("Sounds/click.wav");

// 可取消：取消也会取消底层的加载
var sound2 = await audioManager.GetSoundAsync("Sounds/confirm.wav", cancellationToken);
```

不带 `CancellationToken` 的重载无法被调用方取消，只有在管理器被释放时才会取消。

加载失败或被取消时 **不会** 被缓存——对应的记录会被移除，之后可以重试该标识符。当派生管理器的任务以 `null` 完成，或以 `Id` 与请求的标识符不一致的音频完成时，任务以 `InvalidOperationException` 失败，而不是把错误的结果缓存下来。

```csharp
audioManager.GetSoundAsync(null); // ArgumentNullException
audioManager.GetSoundAsync("Sounds/click.wav"); // 管理器被释放后是 ObjectDisposedException
```

### 创建播放

`Sound<T>` 是已加载的音频数据，而不是正在播放的声音；`CreatePlayback` 把它包装成可以播放、暂停、停止、定位和观察的 `Playback<T>`。一个音频可以同时支撑任意多个播放。

```csharp
var playback = audioManager.CreatePlayback(sound); // 创建时处于 PlaybackStatus.None 状态
```

管理器会记录新建播放的初始状态、音量、播放位置与归一化位置，因此第一次 `Update` 不会把它们当作变化上报。

```csharp
audioManager.CreatePlayback(null); // ArgumentNullException
audioManager.CreatePlayback(foreignSound); // 该音频不是由本管理器加载时是 KeyNotFoundException
audioManager.CreatePlayback(disposedSound); // ObjectDisposedException
```

### 播放

`Play` 系列创建播放并立刻开始播放。

```csharp
var playback = audioManager.Play(sound);
var playback2 = audioManager.Play(sound, 0.5); // 半音量
```

音频尚未加载时，`PlayAsync` 系列会先加载，再在开始播放后以该播放完成任务。"是否指定音量"与"是否可取消"的每种组合都有一个重载，因此每个调用点都显式地表明了这两点。

```csharp
var playback = await audioManager.PlayAsync("Sounds/click.wav");
var playback2 = await audioManager.PlayAsync("Sounds/click.wav", 0.5);
var playback3 = await audioManager.PlayAsync("Sounds/click.wav", cancellationToken);
var playback4 = await audioManager.PlayAsync("Sounds/click.wav", 0.5, cancellationToken);
```

音量不在 0 到 1 之间（包括 `NaN`）时抛出 `ArgumentOutOfRangeException`。任务被取消时，音频加载也一起被取消。

`BeginPlayAndForget` 与 `PlayAsync` 相同，但返回 `void`，也不把播放交出来。它适用于不需要观察播放的场景：这样创建的播放会把 `AutoDisposeWhenStopped` 置为 `true`，停止后由 `Update` 自动释放，调用方不必持有引用来回收内存。

```csharp
audioManager.BeginPlayAndForget("Sounds/click.wav");
audioManager.BeginPlayAndForget("Sounds/click.wav", 0.5);
audioManager.BeginPlayAndForget("Sounds/click.wav", cancellationToken);
audioManager.BeginPlayAndForget("Sounds/click.wav", 0.5, cancellationToken);
```

### 轮询与事件分发

`Update` 轮询每一个存活的播放、触发变化事件、释放已经结束的播放。 **每帧调用一次，或按较短的间隔调用**——不调用它，就既不会有事件，也不会有播放被释放。

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

每次调用分两轮：

1. 读取每个播放的状态、音量、播放位置与归一化位置。与上一次调用读到的值不同时，触发对应的事件。事件处理器可以释放播放或释放管理器，循环会发现并停止轮询这个播放。
2. 释放已经被释放的播放，以及 `AutoDisposeWhenStopped` 为 `true` 且 `Status` 在第一轮中变成 `PlaybackStatus.None` 的播放。因为释放发生在第二轮， **被这次调用释放的播放仍然会上报它最后的变化**。

释放播放会减少所属音频的播放计数；计数归零时，音频也一并被释放。没有播放的已加载音频会留在缓存里，直到 `DisposeSound` 或 `Dispose` 把它移除。

`Update` 是 `virtual` 的，可以重写；重写时先调用 `ThrowIfDisposed`，再调用基类实现。

派生管理器可以通过 `GetPlaybacks` 查看当前存活的播放，它以 `Playback<T>.Id` 为键填充传入的字典。

```csharp
protected override void Update()
{
    base.Update();
    var playbacks = new Dictionary<int, Playback<string>>();
    GetPlaybacks(playbacks);
}
```

### 卸载音频

`DisposeSound` 释放该音频的所有存活播放，并卸载音频本身，同时移除缓存中的记录。

```csharp
audioManager.DisposeSound("Sounds/click.wav"); // 该标识符未加载时是 KeyNotFoundException
```

仍在加载中的音频无法卸载，这样做会抛出 `InvalidOperationException`。

### 释放

释放管理器会取消所有进行中的加载、释放所有存活的播放与所有已加载的音频，并清空两个缓存。`IsDisposed` 反映该状态；释放之后，任何需要管理器的成员都会抛出 `ObjectDisposedException`。

### 实现一个管理器

派生管理器需要实现两个成员。

```csharp
public sealed class GameAudioManager : AudioManager<string>
{
    // 加载 id 对应的音频文件并包装成音频
    protected override async Task<Sound<string>> CreateSoundAsync(string id, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); // 重写时应当先调用
        var audioBytes = await ReadAudioFileAsync(id, cancellationToken);
        return new MySound(id, audioBytes);
    }

    // 为已加载的音频创建播放
    protected override Playback<string> CreatePlaybackImpl(int id, Sound<string> sound)
    {
        ThrowIfDisposed(); // 重写时应当先调用
        return new MyPlayback(id, sound);
    }
}
```

`CreateSoundAsync` 与 `CreatePlaybackImpl` 只由管理器调用：标识符的校验、缓存的维护、以及返回实例的检查都由管理器负责（返回 `null`，或返回状态不是 `PlaybackStatus.None` 的播放，都会抛出 `InvalidOperationException`）。

## Sound\<T\>

`Sound<T>` 是已加载、可播放的音频数据。它实现了 `IDisposable`，并持有加载时使用的标识符。

```csharp
var id = sound.Id;
var length = sound.Length; // 秒；实现类型应保证它大于 0
var isDisposed = sound.IsDisposed;
```

实例在加载完成时由管理器创建；在它的播放计数归零时、调用 `DisposeSound` 时、或管理器本身被释放时由管理器释放。释放音频会置空它的资源，之后除 `IsDisposed` 以外的成员都会抛出 `ObjectDisposedException`。

派生的音频把标识符传给基类构造函数，传入 `null` 会抛出 `ArgumentNullException`——因此标识符类型要么是引用类型，要么是值类型。

```csharp
public sealed class MySound : Sound<string>
{
    private byte[] _audioBytes;

    public MySound(string id, byte[] audioBytes) : base(id)
    {
        _audioBytes = audioBytes;
    }

    public override double Length => /* 秒为单位的长度 */;

    protected override void Dispose(bool disposing)
    {
        _audioBytes = null;
        base.Dispose(disposing);
    }
}
```

## Playback\<T\>

`Playback<T>` 是某个音频的一次独立播放。与作为数据的音频不同，播放是状态：它持有当前的状态、音量和播放位置，并在它们变化时触发事件。

### 属性

```csharp
var id = playback.Id; // 由管理器分配的标识符
var sound = playback.Sound; // 创建该播放所用的音频
var isDisposed = playback.IsDisposed;
var autoDisposeWhenStopped = playback.AutoDisposeWhenStopped; // 由 BeginPlayAndForget 置位
```

```csharp
var status = playback.Status; // PlaybackStatus.None / Playing / Paused

var volume = playback.Volume; // 0 到 1
playback.Volume = 0.5;

var position = playback.Position; // 秒
playback.Position = 3.5;

var normalizedPosition = playback.NormalizedPosition; // 0 是开头，1 是结尾
playback.NormalizedPosition = 0.5; // 通过归一化位置定位
```

`NormalizedPosition` 基于 `Position` 与 `Sound.Length` 实现；`Length` 来自音频，因此音频被释放后 `NormalizedPosition` 不可再用。

### 播放控制

```csharp
playback.Play(); // PlaybackStatus.None 时从头播放，PlaybackStatus.Paused 时从当前位置继续
playback.Pause();
playback.Stop(); // 重置播放位置并回到 PlaybackStatus.None
```

`Stop` 的默认实现先把 `Position` 置为 0，再调用 `Pause`。派生的播放可以重写它（通常是直接调用底层实现），但必须保证调用之后 `Status` 变成 `PlaybackStatus.None`。

播放被释放后，所有属性和播放控制方法都会抛出 `ObjectDisposedException`。`Play`、`Pause`、`Stop` 会根据当前状态作出相应行为，因此不必先自行判断状态。

### 事件

四个事件上报播放发生了什么。它们 **只由 `AudioManager<T>.Update` 触发**，并且只在轮询到的值与上一次调用读到的值不同时触发——不会因为调用了 `Play`、`Pause` 或 `Stop` 而直接触发。

```csharp
playback.StatusChanged += (sender, status) => Debug.Log($"status: {status}");
playback.VolumeChanged += (sender, volume) => Debug.Log($"volume: {volume}");
playback.PositionChanged += (sender, position) => Debug.Log($"position: {position}");
playback.NormalizedPositionChanged += (sender, normalizedPosition) => Debug.Log($"normalized position: {normalizedPosition}");

// 也可以再取消注册
playback.StatusChanged -= onStatusChanged;
```

事件处理器中可以释放播放或释放管理器，`Update` 会发现并停止轮询这个播放。释放播放会解除所有事件处理器。

## PlaybackStatus

`PlaybackStatus` 描述播放的状态。

| 值        | 含义                                                                                     |
|-----------|------------------------------------------------------------------------------------------|
| `None`    | 未在播放：尚未开始、被 `Playback<T>.Stop` 停止、或已经播放到音频结尾（也是枚举的默认值） |
| `Playing` | 正在播放                                                                                 |
| `Paused`  | 已暂停，可以从当前位置继续播放                                                           |

没有单独的"播放完毕"状态：播放到结尾会回到 `None`，这也正是即发即弃的播放能够被自动释放的原因。
