using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Diagnostics;
using Aurora.Pooling;
using Aurora.Threading;

namespace Aurora.Audio
{
    /// <summary>
    /// Manages loading, playback, and unloading of audio for one identifier type.
    /// </summary>
    /// <typeparam name="T">The type of the audio file identifier. The derived type decides what type to use.</typeparam>
    /// <remarks>An instance of <see cref="AudioManager{T}"/> is not guaranteed to be thread safe.</remarks>
    public abstract class AudioManager<T> : IDisposable where T : IEquatable<T>
    {
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Gets a value that indicates whether the current instance has been disposed.
        /// </summary>
        public bool IsDisposed => _cancellationTokenSource == null;

        private Dictionary<T, SoundInfo<T>> _soundInfos = new();

        private Dictionary<int, PlaybackInfo<T>> _playbackInfos = new();

        private static int _idGenerator;

        /// <summary>
        /// Gets the active playbacks of this manager.
        /// </summary>
        /// <param name="playbacks">The dictionary used to store the results, keyed by <see cref="Playback{T}.Id"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="playbacks"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        protected void GetPlaybacks(Dictionary<int, Playback<T>> playbacks)
        {
            ThrowIfDisposed();
            if (playbacks == null)
            {
                throw new ArgumentNullException(nameof(playbacks));
            }
            foreach (var (id, playbackInfo) in _playbackInfos)
            {
                if (!playbackInfo.Playback.IsDisposed)
                {
                    playbacks.Add(id, playbackInfo.Playback);
                }
            }
        }

        private static int NewId => Interlocked.Increment(ref _idGenerator);

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioManager{T}"/> class.
        /// </summary>
        protected AudioManager()
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Loads a sound identified by <paramref name="id"/> asynchronously.
        /// </summary>
        /// <param name="id">The identifier of the sound to load.</param>
        /// <param name="cancellationToken">The cancellation token for the load.</param>
        /// <returns>A task that will complete when the sound identified by <paramref name="id"/> has loaded.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public Task<Sound<T>> LoadAsync(T id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<Sound<T>>(cancellationToken);
            }
            if (_soundInfos.TryGetValue(id, out var soundInfo))
            {
                return soundInfo.LoadTask;
            }
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token,
                cancellationToken
            );
            try
            {
                var taskCompletionSource = new TaskCompletionSource<Sound<T>>();
                var loadTask             = LoadAsyncImpl(id, cancellationTokenSource.Token);
                if (loadTask == null)
                {
                    throw new InvalidOperationException("LoadAsyncImpl returned null");
                }
                _soundInfos.Add(id, new SoundInfo<T>(taskCompletionSource.Task));
                TaskUtility.ContinueWithSynchronously(
                    loadTask,
                    OnLoadCompleted,
                    Tuple.Create(this, id, taskCompletionSource, cancellationTokenSource)
                );
                return taskCompletionSource.Task;
            }
            catch (Exception)
            {
                cancellationTokenSource.Dispose();
                throw;
            }
        }

        private static readonly Action<Task<Sound<T>>, object> OnLoadCompleted = (ancestor, state) =>
        {
            var (@this, id, taskCompletionSource, cancellationTokenSource) =
                (Tuple<AudioManager<T>, T, TaskCompletionSource<Sound<T>>, CancellationTokenSource>)state;
            try
            {
                if (ancestor.IsFaulted || ancestor.IsCanceled)
                {
                    if (!@this.IsDisposed)
                    {
                        @this._soundInfos.Remove(id);
                    }
                    TaskUtility.HandleFaultsAndCancellation(
                        ancestor,
                        taskCompletionSource,
                        cancellationTokenSource.Token
                    );
                    return;
                }
                var invalidOperationException = GetInvalidOperationException(id, ancestor, out var sound);
                if (invalidOperationException != null)
                {
                    if (!@this.IsDisposed)
                    {
                        @this._soundInfos.Remove(id);
                    }
                    DisposeNoThrow(sound);
                    taskCompletionSource.TrySetException(invalidOperationException);
                    return;
                }
                taskCompletionSource.TrySetResult(sound);
            }
            finally
            {
                cancellationTokenSource.Dispose();
            }

            static InvalidOperationException GetInvalidOperationException(
                T              id,
                Task<Sound<T>> loadTask,
                out Sound<T>   sound)
            {
                sound = loadTask.Result;
                return sound == null
                           ? new InvalidOperationException("Loaded sound is null")
                           : EqualityComparer<T>.Default.Equals(id, sound.Id)
                               ? null
                               : new InvalidOperationException("Loaded sound has unexpected id");
            }
        };

        /// <summary>
        /// Creates a playback of the given loaded sound.
        /// </summary>
        /// <param name="sound">The loaded sound to create a playback of.</param>
        /// <param name="volume">The volume of the playback, in the range 0 to 1.</param>
        /// <returns>The created playback.</returns>
        /// <remarks>The returned instance is exactly the one created by the derived type.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="sound"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="sound"/> has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> is less than 0 or greater than 1.</exception>
        /// <exception cref="KeyNotFoundException"><paramref name="sound"/> is not loaded by this manager.</exception>
        /// <exception cref="InvalidOperationException"><see cref="CreatePlaybackImpl"/> returned <see langword="null"/> or a playback that is not in the <see cref="PlaybackStatus.None"/> status.</exception>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public Playback<T> CreatePlayback(Sound<T> sound, double volume = 1)
        {
            ThrowIfDisposed();
            if (sound == null)
            {
                throw new ArgumentNullException(nameof(sound));
            }
            if (sound.IsDisposed)
            {
                throw new ArgumentException();
            }
            if (volume is double.NaN or < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(volume), volume, null);
            }
            var soundId   = sound.Id;
            var soundInfo = _soundInfos[soundId];
            var playback  = CreatePlaybackImpl(NewId, sound);
            if (playback == null)
            {
                throw new InvalidOperationException("CreatePlaybackImpl returned null");
            }
            if (playback.Status != PlaybackStatus.None)
            {
                DisposeNoThrow(playback);
                throw new InvalidOperationException(
                    "CreatePlaybackImpl returned a playback that is not in the None status"
                );
            }
            playback.Volume = volume;
            soundInfo.PlaybackCount++;
            _playbackInfos.Add(
                playback.Id,
                new PlaybackInfo<T>(
                    playback,
                    soundId,
                    playback.Status,
                    playback.Volume,
                    playback.Position,
                    playback.NormalizedPosition
                )
            );
            return playback;
        }

        /// <summary>
        /// Creates a playback of the given loaded sound and starts playing it.
        /// </summary>
        /// <param name="sound">The loaded sound to play.</param>
        /// <param name="volume">The volume of the playback, in the range 0 to 1.</param>
        /// <returns>The created playback.</returns>
        /// <remarks>The returned instance is exactly the one created by the derived type.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="sound"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="sound"/> has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> is less than 0 or greater than 1.</exception>
        /// <exception cref="KeyNotFoundException"><paramref name="sound"/> is not loaded by this manager.</exception>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public Playback<T> Play(Sound<T> sound, double volume = 1)
        {
            var playback = CreatePlayback(sound, volume);
            playback.Play();
            return playback;
        }

        /// <summary>
        /// Loads the sound identified by <paramref name="id"/>, creates a playback of it, and starts playing it.
        /// </summary>
        /// <param name="id">The identifier of the sound to play.</param>
        /// <param name="volume">The volume of the playback, in the range 0 to 1.</param>
        /// <param name="cancellationToken">The cancellation token for the load.</param>
        /// <returns>A task that completes with the created playback.</returns>
        /// <remarks>The instance the task completes with is exactly the one created by the derived type.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> is less than 0 or greater than 1.</exception>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> has been canceled.</exception>
        public Task<Playback<T>> PlayAsync(T id, double volume = 1, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (volume is double.NaN or < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(volume), volume, null);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<Playback<T>>(cancellationToken);
            }
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token,
                cancellationToken
            );
            var taskCompletionSource = new TaskCompletionSource<Playback<T>>();
            TaskUtility.ContinueWithSynchronously(
                LoadAsync(id, cancellationTokenSource.Token),
                PlayOnLoadCompleted,
                Tuple.Create(this, taskCompletionSource, cancellationTokenSource, volume)
            );
            return taskCompletionSource.Task;
        }

        private static readonly Action<Task<Sound<T>>, object> PlayOnLoadCompleted = (ancestor, state) =>
        {
            var (@this, taskCompletionSource, cancellationTokenSource, volume) =
                (Tuple<AudioManager<T>, TaskCompletionSource<Playback<T>>, CancellationTokenSource, double>)state;
            try
            {
                if (TaskUtility.HandleFaultsAndCancellation(
                        ancestor,
                        taskCompletionSource,
                        cancellationTokenSource.Token
                    ))
                {
                    return;
                }
                try
                {
                    var sound    = ancestor.Result;
                    var playback = @this.Play(sound, volume);
                    taskCompletionSource.TrySetResult(playback);
                }
                catch (Exception e)
                {
                    taskCompletionSource.TrySetException(e);
                }
            }
            finally
            {
                cancellationTokenSource.Dispose();
            }
        };

        /// <summary>
        /// Begins loading and playing the sound identified by <paramref name="id"/>, without returning the playback.
        /// </summary>
        /// <param name="id">The identifier of the sound to play.</param>
        /// <param name="volume">The volume of the playback, in the range 0 to 1.</param>
        /// <param name="cancellationToken">The cancellation token for the load.</param>
        /// <remarks>The playback is disposed automatically by <see cref="Update"/> once it stops.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> is less than 0 or greater than 1.</exception>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public void BeginPlayAndForget(T id, double volume = 1, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (volume is double.NaN or < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(volume), volume, null);
            }
            TaskUtility.BeginAwait(PlayAndForgetAsync(id, volume, cancellationToken));
        }

        private Task PlayAndForgetAsync(T id, double volume, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<Playback<T>>(cancellationToken);
            }
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token,
                cancellationToken
            );
            var taskCompletionSource = new TaskCompletionSource<Playback<T>>();
            TaskUtility.ContinueWithSynchronously(
                LoadAsync(id, cancellationTokenSource.Token),
                PlayAndSetAutoDisposeWhenStoppedOnLoadCompleted,
                Tuple.Create(this, taskCompletionSource, cancellationTokenSource, volume)
            );
            return taskCompletionSource.Task;
        }

        private static readonly Action<Task<Sound<T>>, object> PlayAndSetAutoDisposeWhenStoppedOnLoadCompleted =
            (ancestor, state) =>
            {
                var (@this, taskCompletionSource, cancellationTokenSource, volume) =
                    (Tuple<AudioManager<T>, TaskCompletionSource<Playback<T>>, CancellationTokenSource, double>)state;
                try
                {
                    if (TaskUtility.HandleFaultsAndCancellation(
                            ancestor,
                            taskCompletionSource,
                            cancellationTokenSource.Token
                        ))
                    {
                        return;
                    }
                    try
                    {
                        var sound    = ancestor.Result;
                        var playback = @this.CreatePlayback(sound, volume);
                        playback.InternalAutoDisposeWhenStopped = true;
                        playback.Play();
                        taskCompletionSource.TrySetResult(playback);
                    }
                    catch (Exception e)
                    {
                        taskCompletionSource.TrySetException(e);
                    }
                }
                finally
                {
                    cancellationTokenSource.Dispose();
                }
            };

        /// <summary>
        /// Creates a playback of the given sound.
        /// </summary>
        /// <param name="id">The identifier of the playback.</param>
        /// <param name="sound">The loaded sound to create a playback of.</param>
        /// <returns>The created playback.</returns>
        /// <remarks>When overriding this method, call <see cref="ThrowIfDisposed"/> first.</remarks>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        protected abstract Playback<T> CreatePlaybackImpl(int id, Sound<T> sound);

        /// <summary>
        /// Loads the sound identified by <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The identifier of the sound to load.</param>
        /// <param name="cancellationToken">The cancellation token for the load.</param>
        /// <returns>A task that completes with the loaded sound.</returns>
        /// <remarks>When overriding this method, call <see cref="ThrowIfDisposed"/> first.</remarks>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
        protected abstract Task<Sound<T>> LoadAsyncImpl(T id, CancellationToken cancellationToken);

        /// <summary>
        /// Polls active playbacks and releases the ones that meet either of the following conditions:
        /// <list type="table">
        /// <listheader><term>Condition</term><description>Description</description></listheader>
        /// <item><term>Disposed</term><description><see cref="Playback{T}.IsDisposed"/> is <see langword="true"/>.</description></item>
        /// <item><term>Stopped</term><description><see cref="Playback{T}.AutoDisposeWhenStopped"/> is <see langword="true"/> and the <see cref="Playback{T}.Status"/> is <see cref="PlaybackStatus.None"/>.</description></item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this once per frame, or at short intervals.
        /// </para>
        /// <para>
        /// Change events are raised in a first pass and playbacks are released in a second pass, so a playback released by this call still reports its last changes.
        /// </para>
        /// <para>
        /// Releasing a playback decrements the playback count of its sound; when the count reaches zero, the sound is disposed as well.
        /// </para>
        /// <para>
        /// When overriding this method, call <see cref="ThrowIfDisposed"/> first, and call the base implementation as well.
        /// </para>
        /// </remarks>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public virtual void Update()
        {
            ThrowIfDisposed();
            if (_playbackInfos.Count == 0)
            {
                return;
            }
            var playbackInfos = PredefinedPools<int, PlaybackInfo<T>>.Dictionary.Get();
            try
            {
                foreach (var (id, playbackInfo) in _playbackInfos)
                {
                    playbackInfos.Add(id, playbackInfo);
                }

                foreach (var (id, _) in playbackInfos)
                {
                    if (!_playbackInfos.TryGetValue(id, out var playbackInfo))
                    {
                        continue;
                    }
                    var playback = playbackInfo.Playback;
                    if (playback.IsDisposed)
                    {
                        continue;
                    }
                    var status = playback.Status;
                    if (status != playbackInfo.PreviousStatus)
                    {
                        playbackInfo.PreviousStatus = status;
                        playback.OnStatusChanged(status);
                        if (IsDisposed)
                        {
                            return;
                        }
                        if (playback.IsDisposed)
                        {
                            continue;
                        }
                    }
                    var volume = playback.Volume;
                    if (volume != playbackInfo.PreviousVolume)
                    {
                        playbackInfo.PreviousVolume = volume;
                        playback.OnVolumeChanged(volume);
                        if (IsDisposed)
                        {
                            return;
                        }
                        if (playback.IsDisposed)
                        {
                            continue;
                        }
                    }
                    var position = playback.Position;
                    if (position != playbackInfo.PreviousPosition)
                    {
                        playbackInfo.PreviousPosition = position;
                        playback.OnPositionChanged(position);
                        if (IsDisposed)
                        {
                            return;
                        }
                        if (playback.IsDisposed)
                        {
                            continue;
                        }
                        var normalizedPosition = playback.NormalizedPosition;
                        if (normalizedPosition != playbackInfo.PreviousNormalizedPosition)
                        {
                            playbackInfo.PreviousNormalizedPosition = normalizedPosition;
                            playback.OnNormalizedPositionChanged(normalizedPosition);
                            if (IsDisposed)
                            {
                                return;
                            }
                            // Commented out because this is at the end of the loop.
                            // if (playback.IsDisposed)
                            // {
                            //     continue;
                            // }
                        }
                    }
                }

                foreach (var (id, _) in playbackInfos)
                {
                    if (!_playbackInfos.TryGetValue(id, out var playbackInfo))
                    {
                        continue;
                    }
                    var playback = playbackInfo.Playback;
                    if (playback.IsDisposed)
                    {
                        var soundId = playbackInfo.SoundId;
                        _playbackInfos.Remove(id);
                        if (!_soundInfos.TryGetValue(soundId, out var soundInfo) || --soundInfo.PlaybackCount > 0)
                        {
                            continue;
                        }
                        _soundInfos.Remove(soundId);
                        DisposeNoThrow(soundInfo.LoadTask.Result);
                    }
                    else if (playback.InternalAutoDisposeWhenStopped && playback.Status == PlaybackStatus.None)
                    {
                        var soundId = playbackInfo.SoundId;
                        _playbackInfos.Remove(id);
                        DisposeNoThrow(playback);
                        if (!_soundInfos.TryGetValue(soundId, out var soundInfo) || --soundInfo.PlaybackCount > 0)
                        {
                            continue;
                        }
                        _soundInfos.Remove(soundId);
                        DisposeNoThrow(soundInfo.LoadTask.Result);
                    }
                }
            }
            finally
            {
                PredefinedPools<int, PlaybackInfo<T>>.Dictionary.Return(playbackInfos);
            }
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> if this manager has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="AudioManager{T}"/> class.
        /// </summary>
        ~AudioManager()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="AudioManager{T}"/> class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="AudioManager{T}"/> class and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_cancellationTokenSource == null)
            {
                return;
            }
            ExceptionDispatchInfo exceptionDispatchInfo;
            try
            {
                _cancellationTokenSource.Cancel();
                exceptionDispatchInfo = null;
            }
            catch (Exception e)
            {
                exceptionDispatchInfo = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
            if (disposing)
            {
                if (_playbackInfos.Count > 0)
                {
                    foreach (var (_, playbackInfo) in _playbackInfos)
                    {
                        var playback = playbackInfo.Playback;
                        if (!playback.IsDisposed)
                        {
                            DisposeNoThrow(playback);
                        }
                    }
                    _playbackInfos.Clear();
                }
                if (_soundInfos.Count > 0)
                {
                    foreach (var (_, soundInfo) in _soundInfos)
                    {
                        if (soundInfo.LoadTask.IsCompletedSuccessfully && soundInfo.LoadTask.Result is var sound &&
                            !sound.IsDisposed)
                        {
                            DisposeNoThrow(sound);
                        }
                    }
                    _soundInfos.Clear();
                }
            }
            _playbackInfos = null;
            _soundInfos    = null;
            exceptionDispatchInfo?.Throw();
        }

        /// <summary>
        /// Disposes every active playback of the sound identified by <paramref name="id"/> and unloads the sound itself.
        /// </summary>
        /// <param name="id">The identifier of the sound to unload.</param>
        /// <remarks>Do not unload a sound that is still loading.</remarks>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
        /// <exception cref="KeyNotFoundException">The sound identified by <paramref name="id"/> is not loaded by this manager.</exception>
        /// <exception cref="InvalidOperationException">The sound is still loading.</exception>
        public void DisposeSound(T id)
        {
            ThrowIfDisposed();
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            var soundInfo = _soundInfos[id];
            var loadTask  = soundInfo.LoadTask;
            if (!loadTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Unable to dispose a loading sound.");
            }
            var playbackInfos = PredefinedPools<int, PlaybackInfo<T>>.Dictionary.Get();
            try
            {
                foreach (var (playbackId, playbackInfo) in _playbackInfos)
                {
                    if (EqualityComparer<T>.Default.Equals(playbackInfo.SoundId, id))
                    {
                        playbackInfos.Add(playbackId, playbackInfo);
                    }
                }
                foreach (var (playbackId, playbackInfo) in playbackInfos)
                {
                    _playbackInfos.Remove(playbackId);
                    if (playbackInfo.Playback is var playback && !playback.IsDisposed)
                    {
                        DisposeNoThrow(playback);
                    }
                }
            }
            finally
            {
                PredefinedPools<int, PlaybackInfo<T>>.Dictionary.Return(playbackInfos);
            }
            if (_soundInfos.Remove(id) && loadTask.Result is var sound && !sound.IsDisposed)
            {
                DisposeNoThrow(sound);
            }
        }

        private static void DisposeNoThrow(Sound<T> sound)
        {
            try
            {
                sound.Dispose();
            }
            catch (Exception e)
            {
                Log.E(e);
            }
        }

        private static void DisposeNoThrow(Playback<T> playback)
        {
            try
            {
                playback.Dispose();
            }
            catch (Exception e)
            {
                Log.E(e);
            }
        }
    }
}
